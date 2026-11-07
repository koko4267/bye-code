using System.Security.Principal;
using System.Text;

namespace ByeCode;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        bool scanOnly = false;
        bool cleanOnly = false;
        bool force = false;
        bool includeInsiders = true;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].ToLowerInvariant();
            switch (arg)
            {
                case "--scan":
                case "-s":
                    scanOnly = true;
                    break;
                case "--clean":
                case "-c":
                    cleanOnly = true;
                    break;
                case "--force":
                case "-f":
                    force = true;
                    break;
                case "--no-insiders":
                    includeInsiders = false;
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    return;
            }
        }

        if (scanOnly)
        {
            Cleaner.Scan(includeInsiders);
            return;
        }

        if (cleanOnly)
        {
            var scan = Cleaner.Scan(includeInsiders);

            if (!scan.HasArtifacts)
            {
                Console.WriteLine("\nследов vs code не обнаружено, удаление не требуется.");
                return;
            }

            if (!force)
            {
                Console.WriteLine();
                string prompt = scan.TotalBytes > 0
                    ? $"будет удалено {Ui.FormatBytes(scan.TotalBytes)}. продолжить? [y/n]: "
                    : "найдены системные записи (реестр/учетные данные). очистить? [y/n]: ";

                Console.Write(prompt);
                var key = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (key != "y" && key != "д")
                {
                    Console.WriteLine("отменено.");
                    return;
                }
            }

            Cleaner.Clean(includeInsiders);
            return;
        }

        ShowMenu(includeInsiders);
    }

    private static void ShowMenu(bool includeInsiders)
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine("bye-code");
            Console.WriteLine("удаление visual studio code и всех его данных\n");

            if (!Ui.IsAdmin())
            {
                Ui.Warn("внимание: программа запущена без прав администратора\n");
            }

            Console.WriteLine("1. сканирование");
            Console.WriteLine("2. полное удаление");
            Console.WriteLine("3. выход\n");

            Console.Write("действие [1-3]: ");
            var choice = Console.ReadLine()?.Trim();

            switch (choice)
            {
                case "1":
                    Cleaner.Scan(includeInsiders);
                    Console.WriteLine("\nнажмите enter для возврата в меню...");
                    Console.ReadLine();
                    break;
                case "2":
                    var scan = Cleaner.Scan(includeInsiders);

                    if (!scan.HasArtifacts)
                    {
                        Console.WriteLine("\nследов vs code не обнаружено, удаление не требуется.");
                        Console.WriteLine("\nнажмите enter для возврата в меню...");
                        Console.ReadLine();
                        break;
                    }

                    Console.WriteLine();
                    string prompt = scan.TotalBytes > 0
                        ? $"будет удалено {Ui.FormatBytes(scan.TotalBytes)}. подтверждаете? [y/n]: "
                        : "найдены системные записи (реестр/учетные данные). подтверждаете очистку? [y/n]: ";

                    Console.Write(prompt);
                    var confirm = Console.ReadLine()?.Trim().ToLowerInvariant();

                    if (confirm == "y" || confirm == "д")
                    {
                        Cleaner.Clean(includeInsiders);
                    }
                    else
                    {
                        Console.WriteLine("отменено.");
                    }

                    Console.WriteLine("\nнажмите enter для завершения...");
                    Console.ReadLine();
                    return;
                case "3":
                    return;
            }
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"bye-code: удаление visual studio code из windows

использование:
  ByeCode.exe [параметры]

параметры:
  -s, --scan         анализ системы без удаления
  -c, --clean        удаление
  -f, --force        пропуск подтверждения
  --no-insiders      исключить vs code insiders
  -h, --help         справка
");
    }
}

public static class Ui
{
    private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bye-code.log");
    private static readonly object Sync = new();

    public static void Log(string message)
    {
        Console.WriteLine($"  {message}");
        WriteFile(message);
    }

    public static void Header(string message)
    {
        Console.WriteLine($"\n{message}");
        WriteFile($"=== {message} ===");
    }

    public static void Warn(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
        WriteFile($"[WARN] {message}");
    }

    public static void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  ошибка: {message}");
        Console.ResetColor();
        WriteFile($"[ERROR] {message}");
    }

    public static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1024L * 1024L * 1024L => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} гб",
            >= 1024L * 1024L => $"{bytes / (1024.0 * 1024.0):F2} мб",
            >= 1024L => $"{bytes / 1024.0:F2} кб",
            _ => $"{bytes} б"
        };
    }

    private static void WriteFile(string text)
    {
        lock (Sync)
        {
            try
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}
