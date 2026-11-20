namespace TaskTracker;

// Збереження дошки у текстовий файл
public class BoardStorage
{
    public string DataFile = Path.Combine(Path.GetTempPath(), "falcon_board.txt");
    private int _saveNo;

    public int Save(List<TaskCard> tasks)
    {
        _saveNo++;
        List<string> lines = new List<string>();
        lines.Add("#falcon v" + _saveNo);
        foreach (TaskCard c in tasks)
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
}
