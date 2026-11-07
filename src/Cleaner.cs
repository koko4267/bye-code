using System.Diagnostics;

namespace ByeCode;

public record FoundItem(string Path, long Bytes, bool IsDirectory);

public record ScanResult(
    long TotalBytes,
    List<FoundItem> Items,
    List<string> RegistryKeys,
    List<string> Credentials,
    List<string> PathEntries,
    int ProcessCount)
{
    public bool HasArtifacts => Items.Count > 0 ||
                                RegistryKeys.Count > 0 ||
                                Credentials.Count > 0 ||
                                PathEntries.Count > 0 ||
                                ProcessCount > 0;
}

public static class Cleaner
{
    private static readonly HashSet<string> WhitelistedDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft VS Code",
        "Microsoft VS Code Insiders",
        "Code",
        "Code - Insiders",
        "Code - Exploration",
        ".vscode",
        ".vscode-insiders",
        ".vscode-cli",
        ".vscode-test",
        "Code-updater",
        "vscode-cpptools",
        "VSFeedbackIntelliCodeLogs"
    };

    public static ScanResult Scan(bool includeInsiders = true)
    {
        Ui.Header("сканирование...");

        var procs = WindowsSystem.FindProcesses();
        foreach (var p in procs)
        {
            Ui.Log($"процесс: {p.ProcessName} ({p.Id})");
        }

        CheckUninstaller(dryRun: true);

        var targetPaths = GetTargetPaths(includeInsiders);
        var foundItems = new List<FoundItem>();
        long totalBytes = 0;

        foreach (var path in targetPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path))
            {
                long len = 0;
                try { len = new FileInfo(path).Length; } catch { }
                totalBytes += len;
                foundItems.Add(new FoundItem(path, len, false));
                Ui.Log($"{path} ({Ui.FormatBytes(len)})");
            }
            else if (Directory.Exists(path))
            {
                long size = CalculateDirectorySize(path);
                totalBytes += size;
                foundItems.Add(new FoundItem(path, size, true));
                Ui.Log($"{path} ({Ui.FormatBytes(size)})");
            }
        }

        foreach (var file in GetDynamicFiles().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(file))
            {
                long len = 0;
                try { len = new FileInfo(file).Length; } catch { }
                totalBytes += len;
                foundItems.Add(new FoundItem(file, len, false));
                Ui.Log($"{file} ({Ui.FormatBytes(len)})");
            }
            else if (Directory.Exists(file))
            {
                long size = CalculateDirectorySize(file);
                totalBytes += size;
                foundItems.Add(new FoundItem(file, size, true));
                Ui.Log($"{file} ({Ui.FormatBytes(size)})");
            }
        }

        var regKeys = WindowsSystem.GetTargetRegistryKeys(includeInsiders);
        var foundKeys = new List<string>();
        foreach (var k in regKeys)
        {
            if (WindowsSystem.KeyExists(k))
            {
                foundKeys.Add(k);
                Ui.Log($"реестр: {k}");
            }
        }

        foreach (var t in WindowsSystem.CleanToasts(dryRun: true))
        {
            Ui.Log($"реестр toasts: {t}");
        }

        var credentials = WindowsSystem.FindCredentials();
        foreach (var cred in credentials)
        {
            Ui.Log($"учетные данные: {cred}");
        }

        var pathEntries = WindowsSystem.FindPathEntries();
        foreach (var pe in pathEntries)
        {
            Ui.Log($"path: {pe}");
        }

        Ui.Header("итог:");
        Ui.Log($"найдено объектов: {foundItems.Count}");
        Ui.Log($"объем к удалению: {Ui.FormatBytes(totalBytes)}");

        return new ScanResult(totalBytes, foundItems, foundKeys, credentials, pathEntries, procs.Count);
    }

    public static void Clean(bool includeInsiders = true)
    {
        Ui.Header("удаление vs code...");

        if (!Ui.IsAdmin())
        {
            Ui.Warn("внимание: программа запущена без прав администратора");
        }

        WindowsSystem.StopProcesses(WindowsSystem.FindProcesses());
        CheckUninstaller(dryRun: false);

        var targetPaths = GetTargetPaths(includeInsiders);
        targetPaths.AddRange(GetDynamicFiles());

        int deletedCount = 0;
        long freedBytes = 0;

        foreach (var path in targetPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsSafeToDelete(path))
            {
                continue;
            }

            if (File.Exists(path))
            {
                try
                {
                    long len = new FileInfo(path).Length;
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    freedBytes += len;
                    deletedCount++;
                    Ui.Log($"{path} ({Ui.FormatBytes(len)})");
                }
                catch (Exception ex)
                {
                    Ui.Error($"не удалось удалить {path}: {ex.Message}");
                }
            }
            else if (Directory.Exists(path))
            {
                try
                {
                    long size = CalculateDirectorySize(path);
                    DeleteDirectorySafe(path);
                    freedBytes += size;
                    deletedCount++;
                    Ui.Log($"{path} ({Ui.FormatBytes(size)})");
                }
                catch (Exception ex)
                {
                    Ui.Error($"не удалось удалить {path}: {ex.Message}");
                }
            }
        }

        foreach (var k in WindowsSystem.GetTargetRegistryKeys(includeInsiders))
        {
            if (WindowsSystem.KeyExists(k) && WindowsSystem.DeleteKey(k))
            {
                Ui.Log($"реестр: {k}");
            }
        }

        WindowsSystem.CleanToasts(dryRun: false);

        foreach (var cred in WindowsSystem.FindCredentials())
        {
            if (WindowsSystem.DeleteCredential(cred))
            {
                Ui.Log($"учетные данные: {cred}");
            }
        }

        WindowsSystem.CleanPath();

        Ui.Header("готово:");
        Ui.Log($"удалено объектов: {deletedCount}");
        Ui.Log($"освобождено: {Ui.FormatBytes(freedBytes)}");
    }

    public static bool IsSafeToDelete(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        string normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string root = Path.GetPathRoot(normalized) ?? string.Empty;

        if (string.Equals(normalized, root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return false;

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string[] criticalParents =
        {
            userProfile,
            appData,
            localAppData,
            temp,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        };

        foreach (var cp in criticalParents)
        {
            if (string.Equals(normalized, cp, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        string low = normalized.ToLowerInvariant();
        if (low.Contains("antigravity"))
            return false;

        string name = Path.GetFileName(normalized);
        if (string.IsNullOrEmpty(name)) return false;

        if (WhitelistedDirNames.Contains(name))
            return true;

        if (name.StartsWith("Visual Studio Code", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return true;

        if (low.StartsWith(temp.ToLowerInvariant()))
        {
            if (name.StartsWith("vscode-", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("code-tunnel-", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "VSFeedbackIntelliCodeLogs", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        string crashDumps = Path.Combine(localAppData, "CrashDumps").ToLowerInvariant();
        if (low.StartsWith(crashDumps) && name.StartsWith("code.exe.", StringComparison.OrdinalIgnoreCase))
            return true;

        string recent = Path.Combine(appData, @"Microsoft\Windows\Recent").ToLowerInvariant();
        if (low.StartsWith(recent))
        {
            if (name.StartsWith("1b7cd737ab932c0b", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("7e4dca80246863e3", StringComparison.OrdinalIgnoreCase) ||
                (name.Contains("visual studio code", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static void DeleteDirectorySafe(string path)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) return;

        if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            dir.Delete(false);
            return;
        }

        foreach (var sub in dir.GetDirectories())
        {
            if ((sub.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                sub.Delete(false);
            }
            else
            {
                DeleteDirectorySafe(sub.FullName);
            }
        }

        foreach (var file in dir.GetFiles())
        {
            file.Attributes = FileAttributes.Normal;
            file.Delete();
        }

        dir.Attributes = FileAttributes.Normal;
        dir.Delete(false);
    }

    private static long CalculateDirectorySize(string path)
    {
        long sum = 0;
        try
        {
            var dir = new DirectoryInfo(path);
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
                return 0;

            foreach (var fi in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                sum += fi.Length;
            }
        }
        catch
        {
        }

        return sum;
    }

    private static void CheckUninstaller(bool dryRun)
    {
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var candidates = new[]
        {
            Path.Combine(localApp, @"Programs\Microsoft VS Code\unins000.exe"),
            Path.Combine(localApp, @"Programs\Microsoft VS Code Insiders\unins000.exe"),
            Path.Combine(progFiles, @"Microsoft VS Code\unins000.exe"),
            Path.Combine(progFilesX86, @"Microsoft VS Code\unins000.exe"),
            Path.Combine(progFiles, @"Microsoft VS Code Insiders\unins000.exe"),
            Path.Combine(progFilesX86, @"Microsoft VS Code Insiders\unins000.exe")
        };

        foreach (var c in candidates)
        {
            if (!File.Exists(c)) continue;

            if (dryRun)
            {
                Ui.Log($"деинсталлятор: {c}");
            }
            else
            {
                Ui.Log($"запуск деинсталлятора: {c}...");
                try
                {
                    var psi = new ProcessStartInfo(c, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /FORCECLOSEAPPLICATIONS")
                    {
                        UseShellExecute = true
                    };
                    var p = Process.Start(psi);
                    p?.WaitForExit();
                    Ui.Log("деинсталлятор завершил работу");
                }
                catch (Exception ex)
                {
                    Ui.Error($"ошибка деинсталлятора: {ex.Message}");
                }
            }
        }
    }

    private static List<string> GetTargetPaths(bool includeInsiders)
    {
        var list = new List<string>();
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string desktopUser = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string desktopCommon = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        string startMenuUser = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        string startMenuCommon = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

        list.Add(Path.Combine(appData, "Code"));
        list.Add(Path.Combine(appData, "Code - Exploration"));
        if (includeInsiders) list.Add(Path.Combine(appData, "Code - Insiders"));

        list.Add(Path.Combine(userProfile, ".vscode"));
        list.Add(Path.Combine(userProfile, ".vscode-cli"));
        list.Add(Path.Combine(userProfile, ".vscode-test"));
        if (includeInsiders) list.Add(Path.Combine(userProfile, ".vscode-insiders"));

        list.Add(Path.Combine(localAppData, @"Programs\Microsoft VS Code"));
        if (includeInsiders) list.Add(Path.Combine(localAppData, @"Programs\Microsoft VS Code Insiders"));

        list.Add(@"C:\Program Files\Microsoft VS Code");
        list.Add(@"C:\Program Files (x86)\Microsoft VS Code");
        if (includeInsiders)
        {
            list.Add(@"C:\Program Files\Microsoft VS Code Insiders");
            list.Add(@"C:\Program Files (x86)\Microsoft VS Code Insiders");
        }

        list.Add(Path.Combine(localAppData, "Code-updater"));
        list.Add(Path.Combine(localAppData, @"Microsoft\vscode-cpptools"));

        list.Add(Path.Combine(desktopUser, "Visual Studio Code.lnk"));
        list.Add(Path.Combine(desktopCommon, "Visual Studio Code.lnk"));
        list.Add(Path.Combine(startMenuUser, "Visual Studio Code.lnk"));
        list.Add(Path.Combine(startMenuUser, "Visual Studio Code"));
        list.Add(Path.Combine(startMenuCommon, "Visual Studio Code.lnk"));
        list.Add(Path.Combine(startMenuCommon, "Visual Studio Code"));
        list.Add(Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\Visual Studio Code.lnk"));

        if (includeInsiders)
        {
            list.Add(Path.Combine(desktopUser, "Visual Studio Code - Insiders.lnk"));
            list.Add(Path.Combine(desktopCommon, "Visual Studio Code - Insiders.lnk"));
            list.Add(Path.Combine(startMenuUser, "Visual Studio Code - Insiders.lnk"));
            list.Add(Path.Combine(startMenuUser, "Visual Studio Code - Insiders"));
            list.Add(Path.Combine(startMenuCommon, "Visual Studio Code - Insiders.lnk"));
            list.Add(Path.Combine(startMenuCommon, "Visual Studio Code - Insiders"));
            list.Add(Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\Visual Studio Code - Insiders.lnk"));
        }

        return list;
    }

    private static List<string> GetDynamicFiles()
    {
        var list = new List<string>();
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string temp = Path.GetTempPath();
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        string crashDir = Path.Combine(localApp, "CrashDumps");
        if (Directory.Exists(crashDir))
        {
            try
            {
                foreach (var f in Directory.GetFiles(crashDir, "Code.exe.*"))
                    list.Add(f);
            }
            catch { }
        }

        if (Directory.Exists(temp))
        {
            try
            {
                foreach (var f in Directory.GetFiles(temp, "vscode-*")) list.Add(f);
                foreach (var d in Directory.GetDirectories(temp, "vscode-*")) list.Add(d);
                foreach (var f in Directory.GetFiles(temp, "vscode-inno-updater-*.log")) list.Add(f);
                foreach (var d in Directory.GetDirectories(temp, "VSFeedbackIntelliCodeLogs")) list.Add(d);
                foreach (var d in Directory.GetDirectories(temp, "code-tunnel-*")) list.Add(d);
            }
            catch { }
        }

        string recentAuto = Path.Combine(appData, @"Microsoft\Windows\Recent\AutomaticDestinations");
        string recentCustom = Path.Combine(appData, @"Microsoft\Windows\Recent\CustomDestinations");
        string[] hashes = { "1b7cd737ab932c0b", "7e4dca80246863e3" };

        foreach (var h in hashes)
        {
            string f1 = Path.Combine(recentAuto, $"{h}.automaticDestinations-ms");
            if (File.Exists(f1)) list.Add(f1);
            string f2 = Path.Combine(recentCustom, $"{h}.customDestinations-ms");
            if (File.Exists(f2)) list.Add(f2);
        }

        string recent = Path.Combine(appData, @"Microsoft\Windows\Recent");
        if (Directory.Exists(recent))
        {
            try
            {
                foreach (var f in Directory.GetFiles(recent, "*Visual Studio Code*.lnk"))
                    list.Add(f);
            }
            catch { }
        }

        return list;
    }
}
