namespace TaskTracker;

// Дошка команди. Спершу тут був лише Move
public class TaskBoard
{
    private BoardRepository repo;
    public Dictionary<Column, int> WipLimits = new Dictionary<Column, int>();
    public string LastError;            // причина останньої відмови
    // --- люди ---
    private UserDirectory _directory = new UserDirectory();
    public List<User> Users => _directory.Users;
    private const int PersonalWipLimit = 2;     // скільки задач у роботі може мати людина
    private const int ExpediteCutoffHour = 19;  // після цієї години експрес-лінія закрита
    private const int DefaultPriority = 3;
    private const int MinPriority = 1;
    private const int MaxPriority = 4;
    private const int SmallTaskPoints = 1;      // дрібниці до цього розміру можна без рев'ю
    private const int TodoWipLimit = 6;
    private const int InProgressWipLimit = 3;
    private const int ReviewWipLimit = 2;
    private static readonly int ColumnCount = Enum.GetValues(typeof(Column)).Length;
    // --- збереження у файл ---
    private BoardStorage _storage = new BoardStorage();
    public string DataFile
    {
        get { return _storage.DataFile; }
        set { _storage.DataFile = value; }
    }
    // --- сповіщення ---
    private Notifier _notifier = new Notifier();
    public List<string> Sent => _notifier.Sent;
    public bool Quiet
    {
        get { return _notifier.Quiet; }
        set { _notifier.Quiet = value; }
    }
    // --- статистика і пошук ---
    public static int MovesToday;
    private List<string> recentQueries = new List<string>();

    public TaskBoard(BoardRepository repo)
    {
        this.repo = repo;
        WipLimits.Add(Column.Todo, TodoWipLimit);
        WipLimits.Add(Column.InProgress, InProgressWipLimit);
        WipLimits.Add(Column.Review, ReviewWipLimit);
        // TODO: load the team from HR system
        AddUser("olena", "Olena Kravets", "lead");
        AddUser("ivan", "Ivan Moroz", "dev");
        AddUser("taras", "Taras Hnatiuk", "dev");
        AddUser("sofia", "Sofiia Lytvyn", "qa");
        AddUser("petro", "Petro Savchuk", "dev");
        FindUser("petro").Active = false;   // тимчасово, поки нема Deactivate
    }

    public TaskCard CreateTask(string title, int points, int prio, string tags, string by)
    {
        if (title == null || title.Trim() == "")
            return null;
        TaskCard t = new TaskCard();
        t.Id = BoardRepository.nextId++;
        t.Key = "TT-" + t.Id;
        t.Title = title.Trim();
        t.Points = points;
        t.Priority = prio < MinPriority || prio > MaxPriority ? DefaultPriority : prio;
        t.Tags = tags == null ? "" : tags.ToLower();
        t.Reporter = by;
        t.CreatedAt = DateTime.Now;
        repo.Tasks.Add(t);
        return t;
    }

    // Переносить картку (drag&drop). false - див. LastError
    public bool Move(int id, Column to, string who, bool expedite,
    DateTime? now = null)
    {
        TaskCard t;
        User user;
        if (!TryGetTaskAndUser(id, who, out t, out user))
            return false;
        Column from = t.Column;
        if (!CheckMoveAllowed(t, to, who, expedite, now))
            return false;
        if (to == Column.InProgress && t.Assignee == null)
            t.Assignee = user.Login;       // хто взяв, той і робить
        if (to == Column.Done)
            t.DoneAt = DateTime.Now;
        t.Column = to;
        MovesToday++;
        RecordHistory(t, from, to, user);
        Notify(t.Assignee, MoveNotice(t, from, to));
        return true;
    }

    private bool CheckMoveAllowed(TaskCard t, Column to, string who, bool expedite, DateTime? now)
    {
        if (t.Blocked)
        {
            LastError = t.Key + " is blocked: " + t.BlockReason;
            return false;
        }
        // з беклогу одразу в Done не можна
        if (t.Column == Column.Backlog && to == Column.Done)
        {
            LastError = "cannot close " + t.Key + " straight from Backlog";
            return false;
        }
        if (!CheckLane(to, expedite, now))
            return false;
        return to != Column.Done || CheckDoneRules(t, who);
    }

    private bool CheckLane(Column to, bool expedite, DateTime? now)
    {
        if (expedite)
        {
            // поки є черговий; WIP не дивимось
            if ((now ?? DateTime.Now).Hour >= ExpediteCutoffHour)
            {
                LastError = "expedite lane is closed after " + ExpediteCutoffHour + ":00";
                return false;
            }
        }
        else if (WipLimits.ContainsKey(to) && CountInColumn(to) >= WipLimits[to])
        {
            LastError = "WIP limit " + WipLimits[to] + " reached in " + to;
            return false;
        }
        return true;
    }

    private bool CheckDoneRules(TaskCard t, string who)
    {
        // рев'ю обов'язкове, крім дрібниць на 1 sp
        if (IsReviewRequired(t, t.Column))
        {
            LastError = t.Key + " must go through Review";
            return false;
        }
        // автор сам собі задачу не приймає
        if (IsOwnTask(t, who))
        {
            LastError = "author cannot accept own task " + t.Key;
            return false;
        }
        return true;
    }

    private void RecordHistory(TaskCard t, Column from, Column to, User user)
    {
        HistoryEntry entry = new HistoryEntry();
        entry.TaskId = t.Id;
        entry.From = from;
        entry.To = to;
        entry.By = user.Login;
        entry.At = DateTime.Now;
        repo.History.Add(entry);
    }

    // рахує всі картки колонки, і заблоковані теж (раніше коментар казав протилежне)
    private int CountInColumn(Column column)
    {
        int count = 0;
        foreach (TaskCard card in repo.Tasks)
        {
            if (card.Column == column)
                count++;
        }
        return count;
    }

    private static bool IsReviewRequired(TaskCard t, Column from)
    {
        return from != Column.Review && t.Points > SmallTaskPoints;
    }

    private static bool IsOwnTask(TaskCard t, string who)
    {
        return who == t.Assignee;
    }

    // порожня причина = зняти блок
    public bool Block(int id, string reason)
    {
        TaskCard t = repo.FindTask(id);
        if (t == null)
            return false;
        t.Blocked = reason != null && reason.Trim() != "";
        t.BlockReason = t.Blocked ? reason : null;
        if (t.Blocked)
            Notify(t.Assignee, t.Key + " is blocked: " + reason);
        return true;
    }

    // ================= люди =================
    public bool AddUser(string login, string name, string role)
    {
        return _directory.AddUser(login, name, role);
    }

    public User FindUser(string login)
    {
        return _directory.FindUser(login);
    }

    // скидає LastError і знаходить картку та активну людину; false - причина в LastError
    private bool TryGetTaskAndUser(int id, string who, out TaskCard task, out User user)
    {
        LastError = null;
        user = null;
        return TryGetTask(id, out task) && TryGetActiveUser(who, out user);
    }

    private bool TryGetTask(int id, out TaskCard task)
    {
        task = repo.FindTask(id);
        if (task == null)
        {
            LastError = "task #" + id + " not found";
            return false;
        }
        return true;
    }

    private bool TryGetActiveUser(string who, out User user)
    {
        string login = who.Trim().ToLower();
        user = null;
        foreach (User candidate in Users)
        {
            if (candidate.Login == login)
                user = candidate;
        }
        if (user == null || !user.Active)
        {
            LastError = "unknown or inactive user '" + who + "'";
            return false;
        }
        return true;
    }

    public bool Assign(int id, string who)
    {
        TaskCard t;
        User user;
        if (!TryGetTaskAndUser(id, who, out t, out user))
            return false;
        if (busyCount(user.Login) >= PersonalWipLimit)
        {
            LastError = user.Name + " already has " + busyCount(user.Login) + " task(s) in work";
            return false;
        }
        t.Assignee = user.Login;
        Notify(user.Login, t.Key + " was assigned to you");
        return true;
    }

    // задачі людини в роботі (In progress + Review)
    public int busyCount(string who)
    {
        int n = 0;
        foreach (TaskCard c in repo.Tasks)
        {
            if (c.Assignee == who && (c.Column == Column.InProgress
                || c.Column == Column.Review))
                n++;
        }
        return n;
    }

    // ================= файл =================
    public int Save()
    {
        return _storage.Save(repo.Tasks);
    }

    public string cardToLine(TaskCard c)
    {
        return _storage.cardToLine(c);
    }

    // ================= сповіщення =================
    public void Notify(string login, string text)
    {
        _notifier.Notify(login, text);
    }

    public string MoveNotice(TaskCard t, Column from, Column to)
    {
        return _notifier.MoveNotice(t, from, to);
    }

    // ================= звіти і пошук =================
    public void PrintBoard()
    {
        foreach (Column col in Enum.GetValues(typeof(Column)))
        {
            string head = "  " + col;
            if (WipLimits.ContainsKey(col))
                head += " (WIP " + WipLimits[col] + ")";
            Console.WriteLine(head + ":");
            PrintCards(col);
        }
    }

    private void PrintCards(Column col)
    {
        foreach (TaskCard c in repo.Tasks)
        {
            if (c.Column == col)
                Console.WriteLine("    " + c.Describe());
        }
    }

    public void PrintStats()
    {
        int[] counts = new int[ColumnCount];
        int sp = 0;
        foreach (TaskCard c in repo.Tasks)
            counts[(int)c.Column]++;
        foreach (TaskCard c in repo.Tasks)
        {
            if (c.Column == Column.Done)
                sp += c.Points;
        }
        Console.WriteLine("  backlog " + counts[0] + ", todo " + counts[1] + ", in progress "
            + counts[2] + ", review " + counts[3] + ", done " + counts[4] + " (" + sp + " sp)");
        Console.WriteLine("  avg lead time: " + AvgLeadTime() + " day(s), moves: "
            + MovesToday + ", searches: " + recentQueries.Count);
    }

    // середній lead time у днях: від створення до Done
    public int AvgLeadTime()
    {
        int sum = 0, n = 0;
        foreach (TaskCard c in repo.Tasks)
        {
            if (c.Column != Column.Done || c.DoneAt == DateTime.MinValue)
                continue;
            sum += (c.DoneAt.Date - c.CreatedAt.Date).Days;
            n++;
        }
        return n == 0 ? 0 : sum / n;
    }

    // @логін, #тег або шматок назви
    public List<TaskCard> Search(string q)
    {
        List<TaskCard> res = new List<TaskCard>();
        if (q == null || q.Trim() == "")
            return res;
        q = q.Trim().ToLower();
        recentQueries.Add(q);
        foreach (TaskCard c in repo.Tasks)
        {
            if (Matches(c, q))
                res.Add(c);
        }
        return res;
    }

    private static bool Matches(TaskCard c, string q)
    {
        if (q.StartsWith("@"))
            return c.Assignee == q.Substring(1);
        if (q.StartsWith("#"))
            return c.Tags.Contains(q.Substring(1));
        return c.Title.ToLower().Contains(q);
    }
}
