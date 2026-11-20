# Реєстр дефектів проєктування — TaskTracker (варіант 15, джерело А)

Код: `src/` (5 файлів, 598 фізичних рядків, 403 логічні). Метрики: `docs/metrics-before.csv`.
Серйозність S (1–5), частота F (кількість місць, не більше 4), пріоритет P = S × F.
Номери рядків дано за комітом `baseline`.

| ID | Тип дефекту | Місце | S | F | P |
|---|---|---|---|---|---|
| D-01 | Великий клас | TaskBoard.cs:4–371 | 5 | 4 | 20 |
| D-09 | Одержимість елементарними типами | Models.cs:14, 15, 28; TaskBoard.cs:172, 358 | 4 | 4 | 16 |
| D-06 | Магічні числа | TaskBoard.cs:11, 25–27, 46, 90, 120, 313; SprintReport.cs:17–31 | 3 | 4 | 12 |
| D-13 | Дробовик (Shotgun Surgery) | Models.cs:3; TaskBoard.cs:25, 267, 313; SprintReport.cs:41 | 3 | 4 | 12 |
| D-03 | Дублювання (К2) | TaskBoard.cs:64–74, 196–206 | 3 | 3 | 9 |
| D-05 | Дублювання (К3) | Program.cs:62–69; TaskBoard.cs:299–306; SprintReport.cs:44–51 | 3 | 3 | 9 |
| D-11 | Мертвий код | TaskBoard.cs:133–134, 367–370; SprintReport.cs:29; Models.cs:20 | 2 | 4 | 8 |
| D-12 | Коментарі-виправдання | TaskBoard.cs:11, 34, 89, 98, 171 | 2 | 4 | 8 |
| D-07 | Заздрість до чужих даних | SprintReport.cs:14–34; TaskBoard.cs:246–252 | 3 | 2 | 6 |
| D-08 | Довгий список параметрів | BoardRepository.cs:27; TaskBoard.cs:37 | 3 | 2 | 6 |
| D-14 | Тимчасові поля | Models.cs:17, 20; TaskBoard.cs:14, 17 | 2 | 3 | 6 |
| D-02 | Довгий метод | TaskBoard.cs:55–146 (`Move`) | 5 | 1 | 5 |
| D-04 | Дублювання (К1) | TaskBoard.cs:57–63, 189–195 | 2 | 2 | 4 |
| D-10 | Проковтнутий виняток | TaskBoard.cs:242 (`Save`) | 4 | 1 | 4 |

Усього 14 дефектів 12 різних типів (великий клас, довгий метод, дублювання, магічні числа, заздрість до чужих даних, довгий список параметрів, одержимість елементарними типами, проковтнутий виняток, мертвий код, коментарі-виправдання, дробовик, тимчасові поля).

---

## D-01. Великий клас `TaskBoard`

- **Місце:** `TaskBoard.cs:4–371`.
- **Доказ** (поля, рядки 6–20):
```csharp
private BoardRepository repo;
public Dictionary<Column, int> WipLimits = new Dictionary<Column, int>();
public string LastError;
// --- люди ---
public List<User> Users = new List<User>();
private int personalLimit = 2;      // max 3 задачі на людину
// --- збереження у файл ---
public string DataFile = Path.Combine(Path.GetTempPath(), "falcon_board.txt");
private int save_no;
// --- сповіщення ---
public List<string> Sent = new List<string>();
public bool Quiet;
// --- статистика і пошук ---
```
- **Суть:** один клас відповідає за правила дошки (`Move`, `Block`, `CreateTask`), довідник людей (`Users`, `AddUser`, `FindUser`, `busyCount`, `Assign`), збереження (`Save`, `cardToLine`), сповіщення (`Notify`, `MoveNotice`), статистику (`PrintStats`, `AvgLeadTime`) і пошук (`Search`). 11 полів, 16 публічних методів, 371 рядок; коментарі-роздільники в полях самі називають окремі відповідальності.
- **Чим шкодить:** зміна формату файлу або тексту сповіщень зачіпає клас із найскладнішим методом; правила переміщення не можна перевірити без друку в консоль, бо `Notify` пише в `Console`.
- **Категорії впливу:** змінюваність, тестованість, читаність.
- **S = 5, F = 4, P = 20.**
- **Гіпотеза:** Extract Class (R09) для довідника людей, сповіщень і збереження; у `TaskBoard` лишаються правила переміщення. Перед цим Extract Interface для сповіщень.

## D-02. Довгий метод `TaskBoard.Move`

- **Місце:** `TaskBoard.cs:55–146`.
- **Доказ** (рядки 96–105, перевірка WIP-ліміту):
```csharp
else if (WipLimits.ContainsKey(to))
{
    // заблоковані в ліміт не рахуються
    int cnt = 0;
    foreach (TaskCard x in repo.Tasks)
    {
        if (x.Column == to)
            cnt++;
    }
    if (cnt >= WipLimits[to])
```
- **Суть:** 54 логічні рядки, СС = 21, вкладеність 4, 4 параметри. Метод перевіряє наявність картки, користувача, блок, шлях із Backlog, експедит, WIP, правила для InProgress і Done, пише історію й сповіщає.
- **Чим шкодить:** для покриття всіх шляхів потрібно щонайменше 21 тестовий випадок; будь-яка нова колонка чи правило додає ще одну гілку в цей же метод.
- **Категорії впливу:** змінюваність, тестованість, читаність.
- **S = 5, F = 1, P = 5.**
- **Гіпотеза:** Decompose Conditional (R05) для блоку Done, Guard Clauses (R06) для перевірок на початку, виділення перевірки WIP в окремий метод.

## D-03. Дублювання пошуку людини й перевірки «активна» (К2)

- **Місце:** `TaskBoard.cs:64–74` (`Move`) і `196–206` (`Assign`); третій варіант — `FindUser`, 177–185.
- **Доказ** (`Move`, рядки 64–74):
```csharp
User u = null;
foreach (User x in Users)
{
    if (x.Login == who.Trim().ToLower())
        u = x;
}
if (u == null || !u.Active)
{
    LastError = "unknown or inactive user '" + who + "'";
    return false;
}
```
- **Суть:** 11 логічних рядків повторено дослівно; нормалізація логіна `who.Trim().ToLower()` обчислюється на кожній ітерації й у трьох місцях.
- **Чим шкодить:** зміна правила пошуку (наприклад, регістр логіна) треба робити тричі; `FindUser` уже існує, але `Move` і `Assign` його не використовують.
- **Категорії впливу:** змінюваність, читаність.
- **S = 3, F = 3, P = 9.**
- **Гіпотеза:** Extract Method (R01) `TryGetActiveUser(string who, out User u)` з виставленням `LastError` усередині; Extract Variable (R03) для нормалізованого логіна.

## D-04. Дублювання пошуку картки й помилки «not found» (К1)

- **Місце:** `TaskBoard.cs:57–63` (`Move`) і `189–195` (`Assign`).
- **Доказ** (однаковий початок обох методів):
```csharp
LastError = null;
TaskCard t = repo.FindTask(id);
if (t == null)
{
    LastError = "task #" + id + " not found";
    return false;
}
```
- **Суть:** 7 рядків (5 логічних) повторено; разом із К2 це один неперервний блок 57–74 і 189–206.
- **Чим шкодить:** текст помилки й спосіб скидання `LastError` треба синхронізувати вручну.
- **Категорії впливу:** змінюваність.
- **S = 2, F = 2, P = 4.**
- **Гіпотеза:** Extract Method (R01) `TryGetTask(int id, out TaskCard t)`.

## D-05. Дублювання форматування рядка картки (К3)

- **Місце:** `Program.cs:62–69`, `TaskBoard.cs:299–306`, `SprintReport.cs:44–51`.
- **Доказ** (`PrintBoard`, рядки 299–306):
```csharp
string line = "    " + c.Key + " " + c.Title;
if (c.Assignee != null)
    line += " @" + c.Assignee;
if (c.Points > 0)
    line += " (" + c.Points + " sp)";
if (c.Blocked)
    line += " [blocked]";
Console.WriteLine(line);
```
- **Суть:** 8 рядків у трьох класах, відрізняються лише відступом на початку рядка (`"    "` проти `" "` у `PrintStandup`). Перевірка: `Select-String -Path src\*.cs -Pattern "\[blocked\]"` дає 3 збіги.
- **Чим шкодить:** новий атрибут картки (наприклад, пріоритет у виводі) треба додати в трьох місцях; у різних класах формат уже розходиться відступом.
- **Категорії впливу:** змінюваність, читаність.
- **S = 3, F = 3, P = 9.**
- **Гіпотеза:** Extract Method `Describe(TaskCard c)` і Move Method (R10) у `TaskCard`: `c.Describe()`.

## D-06. Магічні числа

- **Місце:** `TaskBoard.cs:11, 25–27, 46, 90, 120, 313`; `SprintReport.cs:17–31`.
- **Доказ** (`SprintReport.Score`, рядки 16–28):
```csharp
int s = 5;
if (c.Priority == 1)
    s = 100;
else if (c.Priority == 2)
    s = 50;
else if (c.Priority == 3)
    s = 20;
if (c.Tags.Contains("bug"))
    s += 30;
if (c.Blocked)
    s += 40;            // blockers first
if (c.Column == Column.Review)
    s += 15;
```
- **Суть:** ваги важливості (100/50/20/30/40/15/14), WIP-ліміти 6/3/2, годину закриття експедит-лінії `19`, поріг рев'ю `1 sp`, розмір масиву `new int[5]` записано числами в коді; жодне не названо.
- **Чим шкодить:** зміна правила (скажімо, закриття о 18:00) вимагає знайти число пошуком по коду; читач не знає, чому `Priority == 3` дає 20.
- **Категорії впливу:** читаність, змінюваність.
- **S = 3, F = 4, P = 12.**
- **Гіпотеза:** Magic Number → Constant (R04) для годин і лімітів; Conditional → Table (R08) для ваг пріоритету в `Score`.

## D-07. Заздрість до чужих даних

- **Місце:** `SprintReport.cs:14–34` (`Score`), `TaskBoard.cs:246–252` (`cardToLine`).
- **Доказ** (`cardToLine`, рядки 246–252):
```csharp
public string cardToLine(TaskCard c)
{
    string s = c.Id + ";" + c.Key + ";" + c.Title.Replace(";", ",") + ";" + c.Column;
    s += ";" + (c.Assignee == null ? "" : c.Assignee) + ";" + c.Points + ";" + c.Priority;
    return s + ";" + c.Tags + ";" + (c.Blocked ? "1" : "0") + ";"
        + c.CreatedAt.ToString("yyyy-MM-dd");
}
```
- **Суть:** обидва методи звертаються лише до полів `TaskCard` (у `Score` — сім звернень до `c.*`, жодного до власних даних класу). Логіка «температури» картки й серіалізації належить картці, а не звітові та дошці.
- **Чим шкодить:** зміна структури `TaskCard` змушує правити сторонні класи; поля картки публічні, тому ніщо не заважає цьому розповзанню.
- **Категорії впливу:** змінюваність, зв'язність.
- **S = 3, F = 2, P = 6.**
- **Гіпотеза:** Move Method (R10) у `TaskCard`; Replace Temp with Query (R15) для `age` у `Score`.

## D-08. Довгий список параметрів

- **Місце:** `BoardRepository.cs:27–28` (8 параметрів), `TaskBoard.cs:37` (5 параметрів).
- **Доказ:**
```csharp
public TaskCard Add(int id, string title, Column col, string who, int sp, int prio,
    string tags, int age)
// виклик, BoardRepository.cs:12
Add(101, "Login page returns 500", Column.InProgress, "ivan", 3, 1, "bug,auth", 6);
// TaskBoard.cs:37
public TaskCard CreateTask(string title, int points, int prio, string tags, string by)
```
- **Суть:** 8 позиційних параметрів, п'ять із яких цілі й рядки поспіль: легко переплутати `sp` і `prio`, `who` і `title`.
- **Чим шкодить:** помилка в порядку аргументів компілюється й не помітна; демо-дані читаються як таблиця чисел.
- **Категорії впливу:** читаність, надійність.
- **S = 3, F = 2, P = 6.**
- **Гіпотеза:** Introduce Parameter Object (R11) для `Add`.

## D-09. Одержимість елементарними типами

- **Місце:** `Models.cs:14, 15, 28`; `TaskBoard.cs:172, 358`.
- **Доказ:**
```csharp
public int Priority = 3;        // 1 - critical ... 4 - low      (Models.cs:14)
public string Tags = "";        // "bug,ui"                        (Models.cs:15)
public string Role;             // "lead", "dev", "qa"             (Models.cs:28)
u.Role = role == "lead" || role == "qa" ? role : "dev";           // TaskBoard.cs:172
if (c.Tags.Contains(q.Substring(1)))                              // TaskBoard.cs:358
```
- **Суть:** пріоритет — число 1–4 без типу, теги — один рядок через кому, роль — рядок із трьох допустимих значень. Пошук за тегом працює як пошук підрядка, тому запит `#ui` знаходить TT-104 («build,ci»), бо слово «build» містить «ui».
- **Чим шкодить:** реальна помилка пошуку (хибні збіги), немає перевірки допустимих значень на рівні типу, порівняння рядків у різних місцях.
- **Категорії впливу:** надійність, змінюваність.
- **S = 4, F = 4, P = 16.**
- **Гіпотеза:** `enum Priority` і `enum Role`, список тегів замість рядка (Encapsulate Collection R13 для колекцій). Поведінку підрядка спершу зафіксувати тестом, виправляти окремим комітом `fix:`.

## D-10. Проковтнутий виняток у `Save`

- **Місце:** `TaskBoard.cs:238–243`.
- **Доказ:**
```csharp
try
{
    File.WriteAllLines(DataFile, lines);
}
catch (Exception) { }
return lines.Count - 1;
```
- **Суть:** будь-яка помилка запису (немає доступу, диск повний) ігнорується, метод повертає кількість задач, ніби запис вдався.
- **Чим шкодить:** втрата даних без сліду; результат `Save` не можна вважати підтвердженням збереження; помилку неможливо перевірити тестом.
- **Категорії впливу:** надійність, тестованість.
- **S = 4, F = 1, P = 4.**
- **Гіпотеза:** після виділення сховища повертати результат або кидати зрозумілий виняток; до того поведінку зафіксувати характеризаційним тестом (дані: недоступний шлях → повертає 10) і записати в реєстр боргу.

## D-11. Мертвий код

- **Місце:** `TaskBoard.cs:133–134`, `367–370`; `SprintReport.cs:29`; `Models.cs:20`.
- **Доказ:**
```csharp
//if (from == Column.Done)
//    t.DoneAt = DateTime.MinValue;   // reopen - спитати Олену     (TaskBoard.cs:133–134)
//if (c.Points == 0) s += 10;                                      (SprintReport.cs:29)
public string LastQuery() { ... }                                  (TaskBoard.cs:367)
public DateTime StartedAt;                                         (Models.cs:20)
```
- **Суть:** два закоментовані блоки; `LastQuery` ніхто не викликає; `StartedAt` лише записується в `Move` (рядок 115) і ніде не читається (перевірка: `Select-String -Path src\*.cs -Pattern "LastQuery|StartedAt"`).
- **Чим шкодить:** читач вважає ці місця важливими й витрачає час; закоментований «reopen» натякає на незавершену вимогу.
- **Категорії впливу:** читаність.
- **S = 2, F = 4, P = 8.**
- **Гіпотеза:** Remove Dead Code (R17). Перед видаленням `LastQuery` переконатися, що тести його не потребують.

## D-12. Коментарі-виправдання

- **Місце:** `TaskBoard.cs:11, 34, 89, 98, 171`.
- **Доказ:**
```csharp
private int personalLimit = 2;      // max 3 задачі на людину        (11)
FindUser("petro").Active = false;   // тимчасово, поки нема Deactivate (34)
// поки є черговий; WIP не дивимось                                   (89)
// заблоковані в ліміт не рахуються                                   (98)
// старі ролі ("developer", "backend") - це dev                       (171)
```
- **Суть:** коментар на рядку 11 каже 3, код — 2; коментар на рядку 98 стверджує, що заблоковані картки не рахуються в ліміт, а цикл нижче (100–104) рахує всі картки колонки. Інші коментарі виправдовують тимчасові рішення замість назвати їх у коді.
- **Чим шкодить:** коментарі суперечать коду й вводять в оману; невідомо, яка версія правильна (вимога чи реалізація).
- **Категорії впливу:** читаність, надійність.
- **S = 2, F = 4, P = 8.**
- **Гіпотеза:** Rename (R02) і константи замість коментарів (`PersonalLimit`); розбіжності записати в реєстр боргу як питання до замовника.

## D-13. Дробовик (Shotgun Surgery)

- **Місце:** `Models.cs:3`; `TaskBoard.cs:25–27, 267–282, 313, 321–322`; `SprintReport.cs:41`.
- **Доказ:**
```csharp
public enum Column { Backlog, Todo, InProgress, Review, Done }     // Models.cs:3
WipLimits.Add(Column.Review, 2);                                   // TaskBoard.cs:27
switch (to) { case Column.Todo: ... case Column.Done: ... }        // TaskBoard.cs:269–282
int[] cnt = new int[5];                                            // TaskBoard.cs:313
"... backlog " + cnt[0] + ", todo " + cnt[1] + ...                 // TaskBoard.cs:321
|| c.Column == Column.Backlog ...                                  // SprintReport.cs:41
```
- **Суть:** щоб додати колонку, треба змінити `enum`, таблицю WIP, `switch` у `MoveNotice`, розмір масиву й індекси в `PrintStats`, умову у звіті, і компілятор не підкаже, що забули (індекси 0–4 і `new int[5]` — магічні числа).
- **Чим шкодить:** висока ймовірність пропуску одного з п'яти місць; помилка виявиться лише у виводі.
- **Категорії впливу:** змінюваність, надійність.
- **S = 3, F = 4, P = 12.**
- **Гіпотеза:** Split Loop і словник кількостей по `Column` у `PrintStats` (R16), Conditional → Table (R08) для повідомлень; повне розв'язання лежить за межами цієї роботи й іде в реєстр боргу.

## D-14. Тимчасові поля

- **Місце:** `Models.cs:17, 20`; `TaskBoard.cs:14, 17`.
- **Доказ:**
```csharp
public string BlockReason;          // заповнене лише коли Blocked == true   (Models.cs:17)
public DateTime StartedAt;          // пишеться в Move:115, ніде не читається (Models.cs:20)
private int save_no;                // лише нумерує заголовок файлу           (TaskBoard.cs:14)
public bool Quiet;                  // вмикається на час імпорту (імпорту нема) (TaskBoard.cs:17)
```
- **Суть:** поля мають зміст лише в окремих сценаріях; `Quiet` ніхто не вмикає, бо імпорту в коді немає.
- **Чим шкодить:** невідомо, які поля завжди заповнені; `BlockReason` залишається `null` після зняття блоку.
- **Категорії впливу:** читаність, надійність.
- **S = 2, F = 3, P = 6.**
- **Гіпотеза:** Remove Dead Code для `StartedAt` і `Quiet` (після перевірки використань); `save_no` та `BlockReason` залишити, але перейменувати й задокументувати.
