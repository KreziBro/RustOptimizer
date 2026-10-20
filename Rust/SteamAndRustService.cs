using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RustOptimizer.Core;

namespace RustOptimizer.Rust;

/// <summary>
/// Locates Steam and Rust installations, parses loginusers.vdf and localconfig.vdf,
/// and reads/writes Rust launch options and client.cfg/keys.cfg paths.
/// </summary>
public static class SteamAndRustService
{
    public const int RustAppId = 252490;
    public const int RustStagingAppId = 700580;

    static string SelectedFolderFile => Path.Combine(AppPaths.Root, "selected_rust_folder.txt");

    public static string GetSteamInstallPath()
    {
        string[] keys =
        {
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"
        };
        foreach (string key in keys)
        {
            string val = (Registry.GetValue(key, "SteamPath", null) ?? Registry.GetValue(key, "InstallPath", null)) as string;
            if (!string.IsNullOrWhiteSpace(val))
            {
                string normalized = val.Replace('/', '\\');
                if (Directory.Exists(normalized))
                    return normalized;
            }
        }
        const string fallback = @"C:\Program Files (x86)\Steam";
        return Directory.Exists(fallback) ? fallback : null;
    }

    public static IReadOnlyList<string> GetSteamLibraryFolders(string steamPath = null)
    {
        steamPath ??= GetSteamInstallPath();
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
            return list;

        list.Add(steamPath);
        string[] candidates =
        {
            Path.Combine(steamPath, "steamapps", "libraryfolders.vdf"),
            Path.Combine(steamPath, "config", "libraryfolders.vdf")
        };
        string vdfPath = candidates.FirstOrDefault(File.Exists);
        if (vdfPath != null)
        {
            try
            {
                string content = File.ReadAllText(vdfPath);
                foreach (Match m in Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\""))
                {
                    string p = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(p))
                        list.Add(p);
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string FindGameInstallPath(int appId)
    {
        foreach (string lib in GetSteamLibraryFolders())
        {
            string manifest = Path.Combine(lib, "steamapps", $"appmanifest_{appId}.acf");
            if (!File.Exists(manifest))
                continue;
            try
            {
                string acf = File.ReadAllText(manifest);
                Match m = Regex.Match(acf, "\"installdir\"\\s+\"([^\"]+)\"");
                if (!m.Success)
                    continue;
                string installDir = Path.Combine(lib, "steamapps", "common", m.Groups[1].Value);
                if (Directory.Exists(installDir))
                    return installDir;
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        }
        return null;
    }

    public static List<string> FindAllRustFolders()
    {
        var result = new List<string>();
        string main = FindGameInstallPath(RustAppId);
        if (!string.IsNullOrWhiteSpace(main)) result.Add(main);
        string staging = FindGameInstallPath(RustStagingAppId);
        if (!string.IsNullOrWhiteSpace(staging)) result.Add(staging);

        if (File.Exists(SelectedFolderFile))
        {
            string saved = File.ReadAllText(SelectedFolderFile).Trim();
            if (!string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved) && !result.Contains(saved, StringComparer.OrdinalIgnoreCase))
                result.Insert(0, saved);
        }
        return result;
    }

    public static string GetSelectedRustFolder()
    {
        if (File.Exists(SelectedFolderFile))
        {
            string saved = File.ReadAllText(SelectedFolderFile).Trim();
            if (!string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved))
                return saved;
        }
        var detected = FindAllRustFolders().FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(detected))
        {
            SetSelectedRustFolder(detected);
            return detected;
        }
        return null;
    }

    public static void SetSelectedRustFolder(string folder)
    {
        if (!string.IsNullOrWhiteSpace(folder))
        {
            File.WriteAllText(SelectedFolderFile, folder.Trim());
            ClearReadOnlySafe(Path.Combine(folder, "cfg", "client.cfg"));
            ClearReadOnlySafe(Path.Combine(folder, "cfg", "keys.cfg"));
        }
    }

    public static string GetClientCfgPath()
    {
        string folder = GetSelectedRustFolder();
        return string.IsNullOrWhiteSpace(folder) ? null : Path.Combine(folder, "cfg", "client.cfg");
    }

    public static string GetKeysCfgPath()
    {
        string folder = GetSelectedRustFolder();
        return string.IsNullOrWhiteSpace(folder) ? null : Path.Combine(folder, "cfg", "keys.cfg");
    }

    public static int GetActiveRustAppId()
    {
        string folder = GetSelectedRustFolder();
        if (!string.IsNullOrWhiteSpace(folder) && string.Equals(Path.GetFileName(folder.TrimEnd('\\', '/')), "RustStaging", StringComparison.OrdinalIgnoreCase))
            return RustStagingAppId;
        return RustAppId;
    }

    public static string GetConfigPathToLastUser()
    {
        string steamPath = GetSteamInstallPath();
        if (string.IsNullOrWhiteSpace(steamPath))
            throw new InvalidOperationException("Steam installation path was not detected.");

        string loginUsersPath = Path.Combine(steamPath, "config", "loginusers.vdf");
        if (File.Exists(loginUsersPath))
        {
            string vdfText = File.ReadAllText(loginUsersPath);
            string lastSteamId = GetLastUsedSteamId(vdfText);
            if (long.TryParse(lastSteamId, out long steamId64))
            {
                long accountId = steamId64 - 76561197960265728L;
                string candidate = Path.Combine(steamPath, "userdata", accountId.ToString(), "config", "localconfig.vdf");
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        // Fallback: pick most recently modified userdata/<id>/config/localconfig.vdf
        string userdataDir = Path.Combine(steamPath, "userdata");
        if (Directory.Exists(userdataDir))
        {
            var best = Directory.GetDirectories(userdataDir)
                .Select(d => Path.Combine(d, "config", "localconfig.vdf"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (best != null)
                return best;
        }

        throw new FileNotFoundException("Steam localconfig.vdf not found.");
    }

    public static string GetLastUsedSteamId(string vdfText)
    {
        var regex = new Regex("\"(\\d+)\"\\s*\\{([^}]*)\\}", RegexOptions.Singleline);
        string bestByTimestamp = null;
        long maxTs = long.MinValue;
        string lastSeen = null;
        int count = 0;

        foreach (Match match in regex.Matches(vdfText))
        {
            string steamId = match.Groups[1].Value;
            string body = match.Groups[2].Value;
            count++;
            lastSeen = steamId;

            if (Regex.IsMatch(body.ToLowerInvariant(), "\"mostrecent\"\\s*\"1\""))
                return steamId;

            Match tsMatch = Regex.Match(body, "\"Timestamp\"\\s*\"(\\d+)\"", RegexOptions.IgnoreCase);
            if (tsMatch.Success && long.TryParse(tsMatch.Groups[1].Value, out long ts) && ts > maxTs)
            {
                maxTs = ts;
                bestByTimestamp = steamId;
            }
        }

        return bestByTimestamp ?? (count == 1 ? lastSeen : null);
    }

    public static string ReadLaunchOptions(string configPath = null, int? appId = null)
    {
        try
        {
            configPath ??= GetConfigPathToLastUser();
            if (!File.Exists(configPath))
                return "";

            string text = File.ReadAllText(configPath);
            int targetAppId = appId ?? GetActiveRustAppId();
            string appKey = $"\"{targetAppId}\"";
            int softwareIdx = text.LastIndexOf("Software", StringComparison.Ordinal);
            if (softwareIdx == -1) softwareIdx = 0;

            int appIdx = text.IndexOf(appKey, softwareIdx, StringComparison.Ordinal);
            if (appIdx == -1)
                return "";

            int closeIdx = FindClosingBrace(text, appIdx);
            string launchKey = "\"LaunchOptions\"";
            int loIdx = text.IndexOf(launchKey, appIdx, StringComparison.Ordinal);
            if (loIdx == -1 || (closeIdx != -1 && loIdx > closeIdx))
                return "";

            int q1 = FindUnescapedQuote(text, loIdx + launchKey.Length);
            if (q1 == -1) return "";
            int q2 = FindUnescapedQuote(text, q1 + 1);
            if (q2 == -1) return "";

            return text.Substring(q1 + 1, q2 - q1 - 1);
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return "";
        }
    }

    public static void WriteLaunchOptions(string launchOptions, string configPath = null, int? appId = null)
    {
        configPath ??= GetConfigPathToLastUser();
        if (!File.Exists(configPath))
            throw new FileNotFoundException("localconfig.vdf not found", configPath);

        ClearReadOnlySafe(configPath);
        string text = File.ReadAllText(configPath);
        int targetAppId = appId ?? GetActiveRustAppId();
        string appKey = $"\"{targetAppId}\"";

        int softwareIdx = text.LastIndexOf("Software", StringComparison.Ordinal);
        if (softwareIdx == -1)
            throw new InvalidOperationException("Section 'Software' not found in localconfig.vdf");

        int appIdx = text.IndexOf(appKey, softwareIdx, StringComparison.Ordinal);
        if (appIdx == -1)
        {
            text = AddRustSection(text, appKey);
            appIdx = text.IndexOf(appKey, softwareIdx, StringComparison.Ordinal);
        }

        text = UpdateRustLaunchOptions(text, appIdx, launchOptions ?? "");
        File.WriteAllText(configPath, text);
    }

    public static void EnsureSameConfig(string capturedConfigPath, string currentConfigPath)
    {
        if (string.IsNullOrEmpty(capturedConfigPath))
            throw new InvalidOperationException("The snapshot does not contain the Steam config path.");
        if (!string.Equals(capturedConfigPath, currentConfigPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Steam account changed: snapshot belongs to ({capturedConfigPath}), currently active is {currentConfigPath}. Launch options were not touched.");
    }

    static string UpdateRustLaunchOptions(string content, int appIdIndex, string launchOptions)
    {
        const string key = "\"LaunchOptions\"";
        int loIdx = content.IndexOf(key, appIdIndex, StringComparison.Ordinal);
        int closeBrace = FindClosingBrace(content, appIdIndex);

        if (loIdx != -1 && (closeBrace == -1 || loIdx < closeBrace))
        {
            int q1 = FindUnescapedQuote(content, loIdx + key.Length);
            int q2 = FindUnescapedQuote(content, q1 + 1);
            if (q1 != -1 && q2 != -1)
            {
                content = content.Remove(q1 + 1, q2 - q1 - 1);
                content = content.Insert(q1 + 1, launchOptions);
            }
        }
        else if (closeBrace != -1)
        {
            string line = $"\t\t\t\t\"LaunchOptions\"\t\t\"{launchOptions}\"\n\t\t\t";
            content = content.Insert(closeBrace, line);
        }
        return content;
    }

    static string AddRustSection(string content, string rustId)
    {
        int appsIdx = content.LastIndexOf("\"apps\"", StringComparison.Ordinal);
        if (appsIdx == -1)
            throw new InvalidOperationException("Section 'apps' not found in localconfig.vdf");
        int openBrace = content.IndexOf('{', appsIdx);
        string section = $"\n\t\t\t{rustId}\n\t\t\t{{\n\t\t\t\t\"LaunchOptions\"\t\t\"\"\n\t\t\t}}\n";
        return content.Insert(openBrace + 1, section);
    }

    static int FindClosingBrace(string content, int startIndex)
    {
        int depth = 0;
        bool started = false;
        for (int i = startIndex; i < content.Length; i++)
        {
            if (content[i] == '{') { started = true; depth++; }
            else if (content[i] == '}')
            {
                depth--;
                if (started && depth == 0) return i;
            }
        }
        return -1;
    }

    static int FindUnescapedQuote(string text, int startIndex)
    {
        while (startIndex < text.Length)
        {
            int idx = text.IndexOf('"', startIndex);
            if (idx == -1) return -1;
            int slashes = 0;
            for (int j = idx - 1; j >= 0 && text[j] == '\\'; j--)
                slashes++;
            if (slashes % 2 == 0)
                return idx;
            startIndex = idx + 1;
        }
        return -1;
    }

    public static void ClearReadOnlySafe(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                var fi = new FileInfo(path);
                if (fi.IsReadOnly) fi.IsReadOnly = false;
            }
        }
        catch { }
    }

    public static void StartRust()
    {
        int appId = GetActiveRustAppId();
        Process.Start(new ProcessStartInfo($"steam://rungameid/{appId}") { UseShellExecute = true });
    }
}
