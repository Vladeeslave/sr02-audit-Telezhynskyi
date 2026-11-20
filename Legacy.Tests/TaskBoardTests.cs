using TaskTracker;

namespace Legacy.Tests;

public class TaskBoardTests
{
    private readonly BoardRepository _repo = new();
    private readonly TaskBoard _board;

    public TaskBoardTests()
    {
        BoardRepository.nextId = 111;
        TaskBoard.MovesToday = 0;
        _board = new TaskBoard(_repo) { Quiet = true };
    }

    private bool Move(int id, Column to, string who)
        => _board.Move(id, to, who, false);

    private static string Capture(Action action)
    {
        var old = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try { action(); }
        finally { Console.SetOut(old); }
        return writer.ToString();
    }

    [Fact]
    public void Move_UnknownTask_RefusedWithMessage()
    {
        Assert.False(Move(999, Column.Todo, "olena"));
        Assert.Equal("task #999 not found", _board.LastError);
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("petro")]
    public void Move_UnknownOrInactiveUser_Refused(string who)
    {
        Assert.False(Move(110, Column.Todo, who));
        Assert.Equal(
            $"unknown or inactive user '{who}'", _board.LastError);
    }

    [Fact]
    public void Move_BlockedTask_RefusedWithReason()
    {
        Assert.False(Move(106, Column.InProgress, "taras"));
        Assert.Equal(
            "TT-106 is blocked: waiting for bank sandbox keys",
            _board.LastError);
    }

    [Fact]
    public void Move_BacklogToDone_Refused()
    {
        Assert.False(Move(110, Column.Done, "olena"));
        Assert.Equal("cannot close TT-110 straight from Backlog",
            _board.LastError);
    }

    [Fact]
    public void Move_WipLimitReached_Refused()
    {
        Assert.False(Move(102, Column.InProgress, "sofia"));
        Assert.Equal("WIP limit 3 reached in InProgress",
            _board.LastError);
    }

    [Fact]
    public void Move_ToDoneWithoutReview_Refused()
    {
        Assert.False(Move(107, Column.Done, "ivan"));
        Assert.Equal("TT-107 must go through Review",
            _board.LastError);
    }

    [Fact]
    public void Move_AcceptOwnTask_SameCase_Refused()
    {
        Assert.False(Move(108, Column.Done, "taras"));
        Assert.Equal("author cannot accept own task TT-108",
            _board.LastError);
    }

    // Фіксуємо наявну поведінку: перевірка автора враховує регістр
    [Fact]
    public void Move_AcceptOwnTask_OtherCase_IsAccepted()
    {
        Assert.True(Move(108, Column.Done, "Taras"));
        Assert.Equal(Column.Done, _repo.FindTask(108).Column);
        Assert.Equal(
            "taras: TT-108 is accepted, thanks! "
            + "Critical - keep an eye on prod.",
            _board.Sent[0]);
    }

    [Fact]
    public void Move_WhoWithSpacesAndCase_IsNormalized()
    {
        Assert.True(Move(103, Column.Todo, "  OLENA "));
        Assert.Equal(Column.Todo, _repo.FindTask(103).Column);
        Assert.Equal(
            "team: TT-103 is planned: Dark theme for settings",
            _board.Sent[0]);
    }

    [Fact]
    public void Move_ToInProgress_AssignsMoverWhenFree()
    {
        Move(105, Column.Done, "sofia");    // звільняє Review
        Move(101, Column.Review, "ivan");   // звільняє InProgress
        Assert.True(Move(102, Column.InProgress, "Sofia "));
        Assert.Equal("sofia", _repo.FindTask(102).Assignee);
    }

    [Fact]
    public void Move_NullWho_ThrowsNullReference()
    {
        Assert.Throws<NullReferenceException>(
            () => Move(110, Column.Todo, null!));
    }

    [Fact]
    public void Move_Expedite_IgnoresWipBeforeSeven()
    {
        var evening = new DateTime(2026, 1, 5, 18, 59, 0);
        Assert.True(_board.Move(
            102, Column.InProgress, "sofia", true, evening));
    }

    [Fact]
    public void Move_Expedite_ClosedFromSeven()
    {
        var late = new DateTime(2026, 1, 5, 19, 0, 0);
        Assert.False(_board.Move(
            102, Column.InProgress, "sofia", true, late));
        Assert.Equal("expedite lane is closed after 19:00",
            _board.LastError);
    }
    [Fact]
    public void CreateTask_NormalizesInput()
    {
        var t = _board.CreateTask(
            "  Two-factor login ", 8, 7, "Feature,Auth", "olena");
        Assert.Equal("TT-111", t.Key);
        Assert.Equal("Two-factor login", t.Title);
        Assert.Equal(3, t.Priority);
        Assert.Equal("feature,auth", t.Tags);
        Assert.Equal(Column.Backlog, t.Column);
        Assert.Contains(t, _repo.Tasks);
    }

    [Fact]
    public void CreateTask_BlankTitle_ReturnsNull()
    {
        Assert.Null(_board.CreateTask("   ", 1, 1, "", "olena"));
    }

    [Fact]
    public void Assign_UnknownTask_Refused()
    {
        Assert.False(_board.Assign(404, "ivan"));
        Assert.Equal("task #404 not found", _board.LastError);
    }

    [Fact]
    public void Assign_PersonalLimitReached_Refused()
    {
        Assert.False(_board.Assign(103, "taras"));
        Assert.Equal(
            "Taras Hnatiuk already has 3 task(s) in work",
            _board.LastError);
    }

    [Fact]
    public void Assign_FreePerson_SetsAssigneeAndNotifies()
    {
        Assert.True(_board.Assign(110, "olena"));
        Assert.Equal("olena", _repo.FindTask(110).Assignee);
        Assert.Equal("olena: TT-110 was assigned to you",
            _board.Sent[0]);
    }

    [Fact]
    public void AddUser_UnknownRole_BecomesDev()
    {
        Assert.True(
            _board.AddUser("Nadia", "Nadiia Bondar", "designer"));
        Assert.Equal("dev", _board.FindUser("nadia").Role);
        Assert.False(_board.AddUser("OLENA", "x", "qa"));
        Assert.False(_board.AddUser(" ", "x", "qa"));
    }

    [Fact]
    public void Block_SetsAndClearsReason()
    {
        var t = _repo.FindTask(104);
        Assert.True(_board.Block(104, "disk full"));
        Assert.True(t.Blocked);
        Assert.Equal("disk full", t.BlockReason);
        Assert.Contains("taras: TT-104 is blocked: disk full",
            _board.Sent);
        Assert.True(_board.Block(104, "  "));
        Assert.False(t.Blocked);
        Assert.Null(t.BlockReason);
        Assert.False(_board.Block(999, "x"));
    }

    // TT-104 знайдено через підрядок "ui" у слові "build"
    [Theory]
    [InlineData("#ui", "TT-103,TT-104,TT-108")]
    [InlineData("@Taras", "TT-104,TT-107,TT-108")]
    [InlineData("crash", "TT-108")]
    [InlineData("", "")]
    public void Search_ReturnsMatchingKeys(string q, string expected)
    {
        var keys = _board.Search(q).Select(c => c.Key);
        Assert.Equal(expected, string.Join(",", keys));
    }

    [Fact]
    public void Save_WritesHeaderAndOneLinePerTask()
    {
        var path = Path.Combine(
            Path.GetTempPath(), "sr02_" + Guid.NewGuid() + ".txt");
        _board.DataFile = path;
        Assert.Equal(10, _board.Save());
        var lines = File.ReadAllLines(path);
        File.Delete(path);
        Assert.Equal(11, lines.Length);
        Assert.Equal("#falcon v1", lines[0]);
    }

    // Фіксуємо проковтнутий виняток: помилку запису не видно
    [Fact]
    public void Save_WriteFails_StillReturnsCount()
    {
        _board.DataFile = Path.Combine(Path.GetTempPath(),
            "no_such_dir_" + Guid.NewGuid(), "board.txt");
        Assert.Equal(10, _board.Save());
    }

    [Fact]
    public void CardToLine_FormatsFields()
    {
        var day = DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd");
        Assert.Equal(
            "101;TT-101;Login page returns 500;InProgress;ivan;"
            + "3;1;bug,auth;0;" + day,
            _board.cardToLine(_repo.FindTask(101)));
    }

    [Fact]
    public void AvgLeadTime_OneDoneTask_ReturnsEight()
    {
        Assert.Equal(8, _board.AvgLeadTime());
    }

    [Fact]
    public void AvgLeadTime_UsesIntegerDivision()
    {
        Move(108, Column.Done, "Taras");
        Assert.Equal(6, _board.AvgLeadTime());   // (8 + 5) / 2
    }

    [Fact]
    public void PrintStats_ShowsCountsAndLeadTime()
    {
        var text = Capture(() => _board.PrintStats());
        Assert.Equal(
            "  backlog 2, todo 2, in progress 3, review 2, "
            + "done 1 (3 sp)" + Environment.NewLine
            + "  avg lead time: 8 day(s), moves: 0, searches: 0"
            + Environment.NewLine, text);
    }

    [Fact]
    public void Notify_NotQuiet_WritesToConsole()
    {
        _board.Quiet = false;
        var text = Capture(() => _board.Notify("ivan", "hi"));
        Assert.Equal("  >> @ivan: hi" + Environment.NewLine, text);
    }

    [Fact]
    public void Notify_NullTextOrLogin_Handled()
    {
        _board.Notify("ivan", null);
        _board.Notify(null, "x");
        Assert.Equal(new[] { "team: x" }, _board.Sent.ToArray());
    }

}
public class SprintReportTests
{
    private readonly BoardRepository _repo = new();

    [Theory]
    [InlineData(101, 130)]   // 100 + 30 за bug
    [InlineData(106, 105)]   // 50 + 40 blocked + 15 за вік 15 днів
    [InlineData(103, 25)]    // 5 + 20 за вік 20 днів
    public void Score_KnownCards(int id, int expected)
    {
        var report = new SprintReport(_repo);
        Assert.Equal(expected, report.Score(_repo.FindTask(id)));
    }
}
