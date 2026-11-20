namespace TaskTracker;

// Сповіщення про події на дошці: журнал надісланого і вивід у консоль
public class Notifier
{
    public List<string> Sent = new List<string>();
    public bool Quiet;                  // вмикається на час імпорту

    public void Notify(string login, string text)
    {
        if (text == null)
            return;
        string to = login ?? "team";
        Sent.Add(to + ": " + text);
        if (Quiet)
            return;
        // TODO: пошта/Slack, коли дадуть доступ
        // TODO TD-07: вивід прив'язано до Console, немає інтерфейсу сповіщувача (R14); строк до ЛР 8
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
}
