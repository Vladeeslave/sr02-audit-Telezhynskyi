namespace TaskTracker;

// Дошка команди. Спершу тут був лише Move
public class TaskBoard
{
    private BoardRepository repo;
    public Dictionary<Column, int> WipLimits = new Dictionary<Column, int>();
    public string LastError;            // причина останньої відмови
    // --- люди ---
    public List<User> Users = new List<User>();
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
    public string DataFile = Path.Combine(Path.GetTempPath(), "falcon_board.txt");
    private int _saveNo;
    // --- сповіщення ---
    public List<string> Sent = new List<string>();
    public bool Quiet;                  // вмикається на час імпорту
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
        LastError = null;
        TaskCard t;
        if (!TryGetTask(id, out t))
            return false;
        User user;
        if (!TryGetActiveUser(who, out user))
            return false;
        Column from = t.Column;
        if (t.Blocked)
        {
            LastError = t.Key + " is blocked: " + t.BlockReason;
            return false;
        }
        // з беклогу одразу в Done не можна
        if (from == Column.Backlog && to == Column.Done)
        {
            LastError = "cannot close " + t.Key + " straight from Backlog";
            return false;
        }
        if (expedite)
        {
            // поки є черговий; WIP не дивимось
            if ((now ?? DateTime.Now).Hour >= ExpediteCutoffHour)
            {
                LastError = "expedite lane is closed after " + ExpediteCutoffHour + ":00";
                return false;
            }
        }
        else if (WipLimits.ContainsKey(to))
        {
            // заблоковані в ліміт не рахуються
            int count = 0;
            foreach (TaskCard card in repo.Tasks)
            {
                if (card.Column == to)
                    count++;
            }
            if (count >= WipLimits[to])
            {
                LastError = "WIP limit " + WipLimits[to] + " reached in " + to;
                return false;
            }
        }
        if (to == Column.InProgress)
        {
            if (t.Assignee == null)
                t.Assignee = user.Login;       // хто взяв, той і робить
        }
        if (to == Column.Done)
        {
            // рев'ю обов'язкове, крім дрібниць на 1 sp
            if (IsReviewRequired(t, from))
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
            t.DoneAt = DateTime.Now;
        }
        t.Column = to;
        MovesToday++;
        HistoryEntry entry = new HistoryEntry();
        entry.TaskId = t.Id;
        entry.From = from;
        entry.To = to;
        entry.By = user.Login;
        entry.At = DateTime.Now;
        repo.History.Add(entry);
        Notify(t.Assignee, MoveNotice(t, from, to));
        return true;
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
        if (login == null || login.Trim() == "")
            return false;
        if (FindUser(login) != null)
            return false;
        User user = new User();
        user.Login = login.Trim().ToLower();
        user.Name = name;
        // старі ролі ("developer", "backend") - це dev
        user.Role = role == "lead" || role == "qa" ? role : "dev";
        Users.Add(user);
        return true;
    }

    public User FindUser(string login)
    {
        foreach (User user in Users)
        {
            if (user.Login == login.Trim().ToLower())
                return user;
        }
        return null;
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
        LastError = null;
        TaskCard t;
        if (!TryGetTask(id, out t))
            return false;
        User user;
        if (!TryGetActiveUser(who, out user))
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
        _saveNo++;
        List<string> lines = new List<string>();
        lines.Add("#falcon v" + _saveNo);
        foreach (TaskCard c in repo.Tasks)
            lines.Add(cardToLine(c));
        try
        {
            File.WriteAllLines(DataFile, lines);
        }
        catch (Exception) { }
        return lines.Count - 1;
    }

    public string cardToLine(TaskCard c)
    {
        string s = c.Id + ";" + c.Key + ";" + c.Title.Replace(";", ",") + ";" + c.Column;
        s += ";" + (c.Assignee == null ? "" : c.Assignee) + ";" + c.Points + ";" + c.Priority;
        return s + ";" + c.Tags + ";" + (c.Blocked ? "1" : "0") + ";"
            + c.CreatedAt.ToString("yyyy-MM-dd");
    }

    // ================= сповіщення =================
    public void Notify(string login, string text)
    {
        if (text == null)
            return;
        string to = login ?? "team";
        Sent.Add(to + ": " + text);
        if (Quiet)
            return;
        // TODO: пошта/Slack, коли дадуть доступ
        Console.WriteLine("  >> @" + to + ": " + text);
    }

    public string MoveNotice(TaskCard t, Column from, Column to)
    {
        switch (to)
        {
            case Column.Todo:
                return from == Column.Backlog ? t.Key + " is planned: " + t.Title : null;
            case Column.InProgress:
                return from == Column.Review ? t.Key + " needs rework" : null;
            case Column.Review:
                return t.Key + " is in the review queue (" + t.Points + " sp)";
            case Column.Done:
                string s = t.Key + " is accepted, thanks!";
                if (t.Priority == 1)
                    s += " Critical - keep an eye on prod.";
                return s;
        }
        return null;
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
            foreach (TaskCard c in repo.Tasks)
            {
                if (c.Column != col)
                    continue;
                Console.WriteLine("    " + c.Describe());
            }
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
            if (q.StartsWith("@"))
            {
                if (c.Assignee == q.Substring(1))
                    res.Add(c);
            }
            else if (q.StartsWith("#"))
            {
                if (c.Tags.Contains(q.Substring(1)))
                    res.Add(c);
            }
            else if (c.Title.ToLower().Contains(q))
                res.Add(c);
        }
        return res;
    }
}
