global using System.IO;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace RustOptimizer.Core;

public enum TweakStatus { Good, Bad, Middle, Unsupported }

/// <summary>One optimization. Port of the original IOptimization.</summary>
public interface ITweak
{
    string Id { get; }
    string Title { get; }
    string Description { get; }
    bool NeedComputerRestart { get; }
    bool NeedSteamRestart { get; }
    TweakStatus GetStatus();
    /// <summary>good=true applies the optimized state, false reverts to the "bad" (stock) one.</summary>
    void Apply(bool good);
    /// <summary>Capture current state as JSON-serializable object (saved before first apply).</summary>
    object CaptureState();
    void RestoreState(JsonElement state);
}

public static class Logger
{
    static readonly object L = new();
    public static string LogPath => Path.Combine(AppPaths.Root, "log.txt");
    public static void Log(string s) { try { lock (L) File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {s}{Environment.NewLine}"); } catch { } }
    public static void Log(Exception e) => Log(e.ToString());
}

public static class AppPaths
{
    public static string Root { get { var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustOptimizer"); Directory.CreateDirectory(p); return p; } }
    public static string Backups { get { var p = Path.Combine(Root, "backups"); Directory.CreateDirectory(p); return p; } }
}

public static class PowershellTools
{
    /// <summary>Runs a PowerShell command and returns trimmed stdout (stderr is logged).</summary>
    public static string powershellExecute(string command)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command },
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd(); string e = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (!string.IsNullOrWhiteSpace(e)) Logger.Log("PS stderr: " + e.Trim());
        return o.Trim();
    }
    public static bool HaveDefender => !string.IsNullOrWhiteSpace(powershellExecute("(Get-Service WinDefend -ErrorAction SilentlyContinue).Status"));
}

/// <summary>Snapshot of one registry value (existed or not) so it can be restored.</summary>
public sealed class RegistryValueSnapshot
{
    public string Hive { get; set; } public string Path { get; set; } public string Name { get; set; }
    public bool Existed { get; set; } public string Kind { get; set; } public string ValueJson { get; set; }

    static RegistryKey Base(string hive) => RegistryKey.OpenBaseKey(Enum.Parse<RegistryHive>(hive), RegistryView.Registry64);

    public static RegistryValueSnapshot Capture(RegistryHive hive, string path, string name)
    {
        var s = new RegistryValueSnapshot { Hive = hive.ToString(), Path = path, Name = name };
        using var k = Base(s.Hive).OpenSubKey(path);
        var v = k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (v != null)
        {
            s.Existed = true; s.Kind = k.GetValueKind(name).ToString();
            s.ValueJson = JsonSerializer.Serialize(v is byte[] b ? Convert.ToBase64String(b) : v);
        }
        return s;
    }

    public void Restore()
    {
        using var root = Base(Hive);
        if (!Existed) { using var k = root.OpenSubKey(Path, true); k?.DeleteValue(Name, false); return; }
        using var key = root.CreateSubKey(Path, true);
        var kind = Enum.Parse<RegistryValueKind>(Kind);
        object val = kind switch
        {
            RegistryValueKind.DWord => JsonSerializer.Deserialize<int>(ValueJson),
            RegistryValueKind.QWord => JsonSerializer.Deserialize<long>(ValueJson),
            RegistryValueKind.Binary => Convert.FromBase64String(JsonSerializer.Deserialize<string>(ValueJson)),
            RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(ValueJson),
            _ => JsonSerializer.Deserialize<string>(ValueJson)
        };
        key.SetValue(Name, val, kind);
    }
}

public static class TweakBackup
{
    static string F(ITweak t) => Path.Combine(AppPaths.Backups, t.Id + ".json");
    public static bool Has(ITweak t) => File.Exists(F(t));
    public static void SaveIfMissing(ITweak t) { if (!Has(t)) File.WriteAllText(F(t), JsonSerializer.Serialize(t.CaptureState())); }
    public static void Restore(ITweak t) { using var d = JsonDocument.Parse(File.ReadAllText(F(t))); t.RestoreState(d.RootElement.Clone()); File.Delete(F(t)); }
    public static T Read<T>(JsonElement e) => e.Deserialize<T>();
}
