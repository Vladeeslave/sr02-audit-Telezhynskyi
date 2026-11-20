namespace TaskTracker;

// Звіти для стендапу (писав Андрій)
public class SprintReport
{
    private BoardRepository repo;
    private const int BaseScore = 5;
    private const int CriticalScore = 100;
    private const int HighScore = 50;
    private const int NormalScore = 20;
    private const int BugBonus = 30;
    private const int BlockedBonus = 40;
    private const int ReviewBonus = 15;
    private const int StaleAgeDays = 14;
    private const int HotScore = 50;            // з цього балу картка йде на стендап

    public SprintReport(BoardRepository repo)
    {
        this.repo = repo;
    }

    // "температура" картки: що обговорюємо першим
    public int Score(TaskCard c)
    {
        int s = BaseScore;
        if (c.Priority == 1)
            s = CriticalScore;
        else if (c.Priority == 2)
            s = HighScore;
        else if (c.Priority == 3)
            s = NormalScore;
        if (c.Tags.Contains("bug"))
            s += BugBonus;
        if (c.Blocked)
            s += BlockedBonus;            // blockers first
        if (c.Column == Column.Review)
            s += ReviewBonus;
        int age = (DateTime.Today - c.CreatedAt.Date).Days;
        if (age > StaleAgeDays)
            s += age;           // давні задачі піднімаємо
        return s;
    }

    // hot cards for the standup: everything on the board with 50+
    public void PrintStandup()
    {
        foreach (TaskCard c in repo.Tasks)
        {
            if (c.Column == Column.Done || c.Column == Column.Backlog || Score(c) < HotScore)
                continue;
            Console.Write("  [" + Score(c) + "]");
            string line = " " + c.Key + " " + c.Title;
            if (c.Assignee != null)
                line += " @" + c.Assignee;
            if (c.Points > 0)
                line += " (" + c.Points + " sp)";
            if (c.Blocked)
                line += " [blocked]";
            Console.WriteLine(line);
        }
    }
}
