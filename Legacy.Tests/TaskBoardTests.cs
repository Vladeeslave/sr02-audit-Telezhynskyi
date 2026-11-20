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
}
