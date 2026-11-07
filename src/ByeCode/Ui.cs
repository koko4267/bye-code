using System.Security.Principal;

namespace ByeCode;

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
