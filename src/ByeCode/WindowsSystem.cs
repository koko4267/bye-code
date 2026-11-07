using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ByeCode;

public static class WindowsSystem
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
        uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    public static List<Process> FindProcesses()
    {
        var result = new List<Process>();

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                string name = p.ProcessName.ToLowerInvariant();
                if (name != "code" && name != "code-tunnel")
                {
                    string? path = null;
                    try { path = p.MainModule?.FileName; } catch { }

                    if (string.IsNullOrEmpty(path)) continue;

                    string low = path.ToLowerInvariant();
                    if (!low.Contains(@"\microsoft vs code\") && !low.Contains(@"\.vscode\"))
                        continue;
                }

                string? exe = null;
                try { exe = p.MainModule?.FileName; } catch { }

                if (!string.IsNullOrEmpty(exe) && exe.Contains("antigravity", StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(p);
            }
            catch
            {
            }
        }

        return result;
    }

    public static void StopProcesses(List<Process> processes)
    {
        foreach (var p in processes)
        {
            try
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(3000);
                Ui.Log($"остановлен процесс: {p.ProcessName} ({p.Id})");
            }
            catch (Exception ex)
            {
                Ui.Error($"не удалось остановить {p.Id}: {ex.Message}");
            }
        }
    }

    public static List<string> GetTargetRegistryKeys(bool includeInsiders)
    {
        var keys = new List<string>
        {
            @"HKCU\Software\Classes\*\shell\VSCode",
            @"HKCU\Software\Classes\Directory\shell\VSCode",
            @"HKCU\Software\Classes\Directory\Background\shell\VSCode",
            @"HKCU\Software\Classes\Drive\shell\VSCode",
            @"HKLM\SOFTWARE\Classes\*\shell\VSCode",
            @"HKLM\SOFTWARE\Classes\Directory\shell\VSCode",
            @"HKLM\SOFTWARE\Classes\Directory\Background\shell\VSCode",
            @"HKLM\SOFTWARE\Classes\Drive\shell\VSCode",
            @"HKCU\Software\Classes\vscode",
            @"HKLM\SOFTWARE\Classes\vscode",
            @"HKCU\Software\Classes\Applications\Code.exe",
            @"HKLM\SOFTWARE\Classes\Applications\Code.exe",
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\Code.exe",
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Code.exe"
        };

        if (includeInsiders)
        {
            keys.AddRange(new[]
            {
                @"HKCU\Software\Classes\*\shell\VSCodeInsiders",
                @"HKCU\Software\Classes\Directory\shell\VSCodeInsiders",
                @"HKCU\Software\Classes\Directory\Background\shell\VSCodeInsiders",
                @"HKCU\Software\Classes\Drive\shell\VSCodeInsiders",
                @"HKLM\SOFTWARE\Classes\*\shell\VSCodeInsiders",
                @"HKLM\SOFTWARE\Classes\Directory\shell\VSCodeInsiders",
                @"HKLM\SOFTWARE\Classes\Directory\Background\shell\VSCodeInsiders",
                @"HKLM\SOFTWARE\Classes\Drive\shell\VSCodeInsiders",
                @"HKCU\Software\Classes\vscode-insiders",
                @"HKLM\SOFTWARE\Classes\vscode-insiders",
                @"HKCU\Software\Classes\Applications\code-insiders.exe",
                @"HKLM\SOFTWARE\Classes\Applications\code-insiders.exe",
                @"HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\code-insiders.exe",
                @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\code-insiders.exe"
            });
        }

        FindUninstallKeys(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", "HKCU", keys);
        FindUninstallKeys(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM", keys);
        FindUninstallKeys(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM", keys);

        return keys;
    }

    public static bool KeyExists(string fullKeyPath)
    {
        var (root, subPath) = ParseKeyPath(fullKeyPath);
        if (root == null || string.IsNullOrWhiteSpace(subPath)) return false;

        try
        {
            using var key = root.OpenSubKey(subPath);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    public static bool DeleteKey(string fullKeyPath)
    {
        var (root, subPath) = ParseKeyPath(fullKeyPath);
        if (root == null || string.IsNullOrWhiteSpace(subPath)) return false;

        var parts = subPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        string leaf = parts[^1].ToLowerInvariant();
        if (!leaf.Contains("code") && !leaf.Contains("vscode") && !leaf.Contains('{'))
            return false;

        try
        {
            root.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static List<string> CleanToasts(bool dryRun)
    {
        var found = new List<string>();

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\ApplicationAssociationToasts",
                writable: !dryRun);

            if (key == null) return found;

            foreach (var name in key.GetValueNames())
            {
                if (name.Contains("Code.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("vscode", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(name);
                    if (!dryRun)
                    {
                        key.DeleteValue(name, false);
                    }
                }
            }
        }
        catch
        {
        }

        return found;
    }

    public static List<string> FindCredentials()
    {
        var targets = new List<string>();

        try
        {
            var psi = new ProcessStartInfo("cmdkey", "/list")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null) return targets;

            string text = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int idx = line.IndexOf("Target:", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) idx = line.IndexOf("target=", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) idx = line.IndexOf("Цель:", StringComparison.OrdinalIgnoreCase);

                if (idx < 0) continue;

                string target = line.Substring(idx).Split(':', 2).Last().Trim();
                if (target.StartsWith("target=", StringComparison.OrdinalIgnoreCase))
                    target = target.Substring(7).Trim();

                string low = target.ToLowerInvariant();
                if ((low.Contains("vscode") || low == "settingskeytag" || low == "settingsivtag") &&
                    !low.Contains("antigravity"))
                {
                    if (!targets.Contains(target))
                        targets.Add(target);
                }
            }
        }
        catch
        {
        }

        return targets;
    }

    public static bool DeleteCredential(string target)
    {
        try
        {
            var psi = new ProcessStartInfo("cmdkey", $"/delete:\"{target}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            p?.WaitForExit();
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static List<string> FindPathEntries()
    {
        var found = new List<string>();
        var scopes = Ui.IsAdmin()
            ? new[] { EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine }
            : new[] { EnvironmentVariableTarget.User };

        foreach (var target in scopes)
        {
            string? raw = Environment.GetEnvironmentVariable("Path", target);
            if (string.IsNullOrEmpty(raw)) continue;

            foreach (var segment in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string low = segment.Trim().ToLowerInvariant();
                if ((low.Contains(@"\microsoft vs code") || low.Contains(@"\.vscode")) && !low.Contains("antigravity"))
                {
                    found.Add($"[{target.ToString().ToLowerInvariant()}] {segment}");
                }
            }
        }

        return found;
    }

    public static void CleanPath()
    {
        var scopes = Ui.IsAdmin()
            ? new[] { EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine }
            : new[] { EnvironmentVariableTarget.User };

        bool changed = false;

        foreach (var target in scopes)
        {
            string? raw = Environment.GetEnvironmentVariable("Path", target);
            if (string.IsNullOrEmpty(raw)) continue;

            var parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var kept = new List<string>();

            foreach (var p in parts)
            {
                string low = p.Trim().ToLowerInvariant();
                if ((low.Contains(@"\microsoft vs code") || low.Contains(@"\.vscode")) && !low.Contains("antigravity"))
                {
                    changed = true;
                    Ui.Log($"path: удален {p}");
                }
                else
                {
                    kept.Add(p);
                }
            }

            if (changed)
            {
                if (target == EnvironmentVariableTarget.Machine && kept.Count == 0 && raw.Length > 20)
                {
                    Ui.Error("защитная отмена: попытка полной очистки системного path");
                    continue;
                }

                Environment.SetEnvironmentVariable("Path", string.Join(";", kept), target);
            }
        }

        if (changed)
        {
            try
            {
                UIntPtr result = UIntPtr.Zero;
                SendMessageTimeout((IntPtr)0xffff, 0x001A, UIntPtr.Zero, "Environment", 2, 5000, out result);
            }
            catch
            {
            }
        }
    }

    private static void FindUninstallKeys(RegistryKey root, string subKeyPath, string prefix, List<string> list)
    {
        try
        {
            using var baseKey = root.OpenSubKey(subKeyPath);
            if (baseKey == null) return;

            foreach (var subName in baseKey.GetSubKeyNames())
            {
                try
                {
                    using var sub = baseKey.OpenSubKey(subName);
                    string? disp = sub?.GetValue("DisplayName")?.ToString();
                    if (!string.IsNullOrEmpty(disp) && disp.Contains("Visual Studio Code", StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add($@"{prefix}\{subKeyPath}\{subName}");
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static (RegistryKey? root, string subPath) ParseKeyPath(string fullPath)
    {
        if (fullPath.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase))
            return (Registry.CurrentUser, fullPath.Substring(5));
        if (fullPath.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase))
            return (Registry.LocalMachine, fullPath.Substring(5));
        return (null, string.Empty);
    }
}
