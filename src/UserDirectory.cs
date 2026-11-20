namespace TaskTracker;

// Довідник людей команди: додавання й пошук за логіном
public class UserDirectory
{
    public List<User> Users = new List<User>();

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
}
