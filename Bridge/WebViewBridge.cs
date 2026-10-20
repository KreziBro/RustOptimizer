using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using RustOptimizer.Core;
using RustOptimizer.Rust;
using RustOptimizer.Tweaks;

namespace RustOptimizer.Bridge;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public class JsBridge
{
    private readonly MainWindow _window;
    private static readonly object FileLock = new();

    public JsBridge(MainWindow window)
    {
        _window = window;
        EnsurePathsAndConfigsInitialized();
    }

    public string GetSteamId()
    {
        try
        {
            string steamPath = SteamAndRustService.GetSteamInstallPath();
            if (!string.IsNullOrWhiteSpace(steamPath))
            {
                string loginUsers = Path.Combine(steamPath, "config", "loginusers.vdf");
                if (File.Exists(loginUsers))
                {
                    string id = SteamAndRustService.GetLastUsedSteamId(File.ReadAllText(loginUsers));
                    if (!string.IsNullOrWhiteSpace(id)) return id;
                }
            }
        }
        catch { }
        return "76561198000000000";
    }

    // --- Language & Logging ---
    private static string LangFilePath => Path.Combine(AppPaths.Root, "lang.json");
    private static string PathsFilePath => Path.Combine(AppPaths.Root, "paths.json");
    private static string ConfigsStoreFilePath => Path.Combine(AppPaths.Root, "configs_registry.json");
    private static string BindsStoreFilePath => Path.Combine(AppPaths.Root, "binds_registry.json");
    private static string BindsCacheFilePath => Path.Combine(AppPaths.Root, "binds_cache.json");
    private static string FavouritesFilePath => Path.Combine(AppPaths.Root, "favourite_commands.json");

    public string getCurrentLang()
    {
        try
        {
            if (!File.Exists(LangFilePath))
                File.WriteAllText(LangFilePath, "{\"lang\":\"ru\"}", Encoding.UTF8);
            return File.ReadAllText(LangFilePath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return "{\"lang\":\"ru\"}";
        }
    }

    public string updateLang(string newLangJson)
    {
        try
        {
            string content = string.IsNullOrWhiteSpace(newLangJson) ? "{\"lang\":\"ru\"}" : newLangJson;
            File.WriteAllText(LangFilePath, content, Encoding.UTF8);
            _window?.Dispatcher.InvokeAsync(() => _window.RefreshWindowTitle());
            return content;
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return "{\"lang\":\"ru\"}";
        }
    }

    public void jsLog(object type, object data)
    {
        Logger.Log($"WEBVIEW2| {type}: {data}");
    }

    public void openLink(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
        }
    }

    public void RestartComputer()
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { UseShellExecute = true, CreateNoWindow = true });
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
        }
    }

    // --- Steam & Rust Process Helpers ---
    public bool SteamIsRunning()
    {
        try
        {
            var procs = Process.GetProcessesByName("steam");
            bool running = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
            return running;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> CloseSteam()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("steam"))
            {
                try { p.Kill(); } catch { }
                p.Dispose();
            }
            for (int i = 0; i < 30; i++)
            {
                if (!SteamIsRunning()) return true;
                await Task.Delay(250);
            }
            return !SteamIsRunning();
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return false;
        }
    }

    public void StartSteam()
    {
        try
        {
            string steamPath = SteamAndRustService.GetSteamInstallPath();
            string exe = !string.IsNullOrWhiteSpace(steamPath) ? Path.Combine(steamPath, "steam.exe") : "steam://open/main";
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
        }
    }

    public void StartRust()
    {
        try
        {
            string folder = GetSelectedFolder();
            if (!string.IsNullOrWhiteSpace(folder))
                SteamAndRustService.SetSelectedRustFolder(folder);
            SteamAndRustService.StartRust();
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
        }
    }

    // --- File / Folder Dialogs ---
    public Task<string> selectFolder()
    {
        var tcs = new TaskCompletionSource<string>();
        _window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Выберите папку с установленным Rust",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = false
                };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                {
                    tcs.SetResult(dlg.SelectedPath);
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
            tcs.SetResult(null);
        });
        return tcs.Task;
    }

    public Task<string> selectFile()
    {
        var tcs = new TaskCompletionSource<string>();
        _window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Выберите файл конфигурации (.cfg)",
                    Filter = "Config files (*.cfg)|*.cfg|All files (*.*)|*.*"
                };
                if (dlg.ShowDialog(_window) == true && !string.IsNullOrWhiteSpace(dlg.FileName))
                {
                    tcs.SetResult(dlg.FileName);
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
            tcs.SetResult(null);
        });
        return tcs.Task;
    }

    public Task<bool> saveBenchmarkBlob(string base64Content, string fileName)
    {
        var tcs = new TaskCompletionSource<bool>();
        _window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                string raw = base64Content ?? "";
                int comma = raw.IndexOf(',');
                if (comma >= 0) raw = raw[(comma + 1)..];
                byte[] bytes = Convert.FromBase64String(raw);
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = string.IsNullOrWhiteSpace(fileName) ? "benchmark.png" : fileName,
                    Filter = "PNG Image (*.png)|*.png|All files (*.*)|*.*"
                };
                if (dlg.ShowDialog(_window) == true)
                {
                    File.WriteAllBytes(dlg.FileName, bytes);
                    tcs.SetResult(true);
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
            tcs.SetResult(false);
        });
        return tcs.Task;
    }

    // --- Game Paths Management (paths.json) ---
    public class PathNodeDto
    {
        public bool is_select { get; set; }
        public bool have_warn { get; set; }
        public string folder { get; set; }
    }

    private void EnsurePathsAndConfigsInitialized()
    {
        lock (FileLock)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                List<PathNodeDto> nodes = null;
                if (File.Exists(PathsFilePath))
                {
                    try
                    {
                        nodes = JsonSerializer.Deserialize<List<PathNodeDto>>(File.ReadAllText(PathsFilePath, Encoding.UTF8));
                    }
                    catch { nodes = null; }
                }

                if (nodes == null || nodes.Count == 0)
                {
                    nodes = new List<PathNodeDto>();
                    var found = SteamAndRustService.FindAllRustFolders();
                    for (int i = 0; i < found.Count; i++)
                    {
                        EnsureRustCfgFilesExist(found[i]);
                        nodes.Add(new PathNodeDto
                        {
                            is_select = (i == 0),
                            have_warn = false,
                            folder = found[i]
                        });
                    }
                    if (nodes.Count > 0)
                        SteamAndRustService.SetSelectedRustFolder(nodes[0].folder);
                    SavePathsInternal(nodes);
                }
                else
                {
                    if (!nodes.Any(n => n.is_select) && nodes.Count > 0)
                    {
                        nodes[0].is_select = true;
                        SavePathsInternal(nodes);
                    }
                    foreach (var n in nodes)
                    {
                        if (!string.IsNullOrWhiteSpace(n.folder) && Directory.Exists(n.folder))
                            EnsureRustCfgFilesExist(n.folder);
                    }
                    var sel = nodes.FirstOrDefault(n => n.is_select);
                    if (sel != null && !string.IsNullOrWhiteSpace(sel.folder))
                        SteamAndRustService.SetSelectedRustFolder(sel.folder);
                }

                EnsureDefaultProfilesExist();
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        }
    }

    private static void EnsureRustCfgFilesExist(string rustFolder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rustFolder) || !Directory.Exists(rustFolder)) return;
            string cfgDir = Path.Combine(rustFolder, "cfg");
            Directory.CreateDirectory(cfgDir);
            string clientCfg = Path.Combine(cfgDir, "client.cfg");
            if (!File.Exists(clientCfg)) File.WriteAllText(clientCfg, "", Encoding.UTF8);
            string keysCfg = Path.Combine(cfgDir, "keys.cfg");
            if (!File.Exists(keysCfg)) File.WriteAllText(keysCfg, "", Encoding.UTF8);
            SteamAndRustService.ClearReadOnlySafe(clientCfg);
            SteamAndRustService.ClearReadOnlySafe(keysCfg);
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
        }
    }

    private static List<PathNodeDto> LoadPathsInternal()
    {
        lock (FileLock)
        {
            try
            {
                if (File.Exists(PathsFilePath))
                {
                    var list = JsonSerializer.Deserialize<List<PathNodeDto>>(File.ReadAllText(PathsFilePath, Encoding.UTF8));
                    if (list != null) return list;
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
            return new List<PathNodeDto>();
        }
    }

    private static void SavePathsInternal(List<PathNodeDto> list)
    {
        lock (FileLock)
        {
            string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PathsFilePath, json, Encoding.UTF8);
            var sel = list.FirstOrDefault(x => x.is_select);
            if (sel != null && !string.IsNullOrWhiteSpace(sel.folder))
                SteamAndRustService.SetSelectedRustFolder(sel.folder);
        }
    }

    public string checkFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return "DIR_NOTFOUND";
        if (!File.Exists(Path.Combine(path, "Rust.exe")) && !File.Exists(Path.Combine(path, "RustClient.exe")))
            return "RUSTEXE_NOTFOUND";
        EnsureRustCfgFilesExist(path);
        if (!File.Exists(Path.Combine(path, "cfg", "client.cfg")))
            return "CLIENTCFG_NOTFOUND";
        if (!File.Exists(Path.Combine(path, "cfg", "keys.cfg")))
            return "KEYSCFG_NOTFOUND";
        return "ALLGOOD";
    }

    public bool pathsCheck()
    {
        var list = LoadPathsInternal();
        if (!list.Any(x => x.is_select))
        {
            EnsurePathsAndConfigsInitialized();
            list = LoadPathsInternal();
        }
        return list.Any(x => x.is_select);
    }

    public bool checkPathsHealth()
    {
        var list = LoadPathsInternal();
        bool anyHealthy = false;
        foreach (var item in list)
        {
            bool ok = checkFolder(item.folder) == "ALLGOOD";
            item.have_warn = !ok;
            if (ok) anyHealthy = true;
        }
        SavePathsInternal(list);
        return anyHealthy || list.Count == 0;
    }

    public string[] getPaths()
    {
        return LoadPathsInternal().Select(x => x.folder).ToArray();
    }

    public string getFullPaths()
    {
        var list = LoadPathsInternal();
        return JsonSerializer.Serialize(list);
    }

    public string GetSelectedFolder()
    {
        var list = LoadPathsInternal();
        var selected = list.FirstOrDefault(x => x.is_select) ?? list.FirstOrDefault();
        if (selected?.folder != null)
        {
            SteamAndRustService.SetSelectedRustFolder(selected.folder);
            return selected.folder;
        }
        return SteamAndRustService.GetSelectedRustFolder();
    }

    public bool addNewPath(string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath)) return false;
        EnsureRustCfgFilesExist(newPath);
        var list = LoadPathsInternal();
        if (list.Any(x => string.Equals(x.folder, newPath, StringComparison.OrdinalIgnoreCase)))
            return false;
        bool selectIt = !list.Any(x => x.is_select);
        list.Add(new PathNodeDto
        {
            is_select = selectIt,
            have_warn = checkFolder(newPath) != "ALLGOOD",
            folder = newPath
        });
        SavePathsInternal(list);
        return true;
    }

    public void selectPath(string path)
    {
        var list = LoadPathsInternal();
        foreach (var item in list)
            item.is_select = string.Equals(item.folder, path, StringComparison.OrdinalIgnoreCase);
        if (!list.Any(x => x.is_select) && !string.IsNullOrWhiteSpace(path))
        {
            list.Add(new PathNodeDto { is_select = true, have_warn = false, folder = path });
        }
        SavePathsInternal(list);
    }

    public string chengeCurrentFolder(string newPath)
    {
        string status = checkFolder(newPath);
        if (status != "ALLGOOD") return status;
        var list = LoadPathsInternal();
        var current = list.FirstOrDefault(x => x.is_select);
        if (current != null)
            current.folder = newPath;
        else
            list.Add(new PathNodeDto { is_select = true, have_warn = false, folder = newPath });
        SavePathsInternal(list);
        return "ALLGOOD";
    }

    public string setPathToFolder(string targetPath, string newPath)
    {
        string status = checkFolder(newPath);
        if (status != "ALLGOOD") return status;
        var list = LoadPathsInternal();
        var node = list.FirstOrDefault(x => string.Equals(x.folder, targetPath, StringComparison.OrdinalIgnoreCase));
        if (node == null) return "FOLDER_NOT_FOUND";
        node.folder = newPath;
        node.have_warn = false;
        SavePathsInternal(list);
        return "ALLGOOD";
    }

    public bool removePath(string targetPath)
    {
        var list = LoadPathsInternal();
        int removed = list.RemoveAll(x => string.Equals(x.folder, targetPath, StringComparison.OrdinalIgnoreCase));
        if (removed <= 0) return false;
        if (!list.Any(x => x.is_select) && list.Count > 0)
            list[0].is_select = true;
        SavePathsInternal(list);
        return true;
    }

    public string[] findRusts() => SteamAndRustService.FindAllRustFolders().ToArray();

    public string getCurrentRustId()
    {
        string folder = GetSelectedFolder();
        bool isStaging = !string.IsNullOrWhiteSpace(folder) &&
                         string.Equals(Path.GetFileName(folder.TrimEnd('\\', '/')), "RustStaging", StringComparison.OrdinalIgnoreCase);
        return isStaging ? "\"700580\"" : "\"252490\"";
    }

    public string getCurrentParamsLaunch()
    {
        GetSelectedFolder();
        return SteamAndRustService.ReadLaunchOptions() ?? "";
    }

    // --- In-Game Tweaks & Graphics (Direct Local Implementation) ---
    public Task<string> getStartupTweaks()
    {
        return Task.Run(() =>
        {
            GetSelectedFolder();
            var cfg = RustGameTweaksAndGraphics.ReadClientCfgDictionary();
            var launch = RustGameTweaksAndGraphics.ParseLaunchParamsToKeyValues(SteamAndRustService.ReadLaunchOptions());

            var result = new Dictionary<string, string>();
            foreach (var tweak in RustGameTweaksAndGraphics.InGameTweaks)
            {
                bool on = false;
                try
                {
                    on = RustGameTweaksAndGraphics.IsInGameTweakEnabled(tweak, cfg, launch);
                }
                catch (Exception ex)
                {
                    Logger.Log(ex);
                }
                result[tweak.Id] = on ? "on" : "off";
            }
            return JsonSerializer.Serialize(result);
        });
    }

    public Task<string> applyTweaks(string data)
    {
        return Task.Run(() =>
        {
            try
            {
                GetSelectedFolder();
                if (!string.IsNullOrWhiteSpace(data))
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(data);
                    if (dict != null)
                    {
                        foreach (var kv in dict)
                        {
                            string val = kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() : kv.Value.ToString();
                            if (val != "on" && val != "off") continue;
                            var def = RustGameTweaksAndGraphics.InGameTweaks.FirstOrDefault(t => string.Equals(t.Id, kv.Key, StringComparison.OrdinalIgnoreCase));
                            if (def != null)
                                RustGameTweaksAndGraphics.SetInGameTweak(def, val == "on");
                        }
                    }
                }
                syncConfigsFromGame();
                return "true";
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                return "false";
            }
        });
    }

    public Task<string> getStartupGraphics()
    {
        return Task.Run(() =>
        {
            try
            {
                GetSelectedFolder();
                var cfg = RustGameTweaksAndGraphics.ReadClientCfgDictionary();
                var launch = RustGameTweaksAndGraphics.ParseLaunchParamsToKeyValues(SteamAndRustService.ReadLaunchOptions());

                var result = new Dictionary<string, int>();
                foreach (var cat in RustGameTweaksAndGraphics.GraphicsCategories)
                {
                    int lvl = RustGameTweaksAndGraphics.GetCurrentGraphicsLevel(cat, cfg, launch);
                    result[cat.Id] = lvl >= 0 ? lvl : 0;
                }

                // glass_reflection checkbox on GraphicsPage (0 = reflections disabled, 2 = enabled)
                var glassTweak = RustGameTweaksAndGraphics.InGameTweaks.FirstOrDefault(t => t.Id == "glass_reflection");
                bool glassDisabled = glassTweak != null && RustGameTweaksAndGraphics.IsInGameTweakEnabled(glassTweak, cfg, launch);
                result["glass_reflection"] = glassDisabled ? 0 : 2;

                return JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                return "{\"shadows\":0,\"textures\":0,\"lighting\":0,\"trees\":0,\"reflections_on_the_water\":0,\"grass\":0,\"clouds\":0,\"smoothing\":0,\"glass_reflection\":0}";
            }
        });
    }

    public async Task<string> applyGraphics(string data)
    {
        try
        {
            GetSelectedFolder();
            if (!string.IsNullOrWhiteSpace(data))
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(data);
                if (dict != null)
                {
                    var levels = new Dictionary<string, int>();
                    foreach (var kv in dict)
                    {
                        int val = -1;
                        if (kv.Value.ValueKind == JsonValueKind.Number && kv.Value.TryGetInt32(out int intVal))
                            val = intVal;
                        else if (int.TryParse(kv.Value.ToString(), out int parsed))
                            val = parsed;

                        if (kv.Key == "glass_reflection" && val >= 0)
                        {
                            var glassTweak = RustGameTweaksAndGraphics.InGameTweaks.FirstOrDefault(t => t.Id == "glass_reflection");
                            if (glassTweak != null)
                                RustGameTweaksAndGraphics.SetInGameTweak(glassTweak, val == 0);
                        }
                        else if (val >= 0)
                        {
                            levels[kv.Key] = val;
                        }
                    }
                    if (levels.Count > 0)
                        RustGameTweaksAndGraphics.ApplyGraphicsSelections(levels);
                }
            }
            syncConfigsFromGame();
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
        }
        return await getStartupGraphics();
    }

    public bool resetGraphics()
    {
        try
        {
            GetSelectedFolder();
            RustGameTweaksAndGraphics.ResetGraphicsSettings();
            syncConfigsFromGame();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return false;
        }
    }

    // --- Configs & Binds Profiles Management ---
    public class ProfileRecordDto
    {
        public string id { get; set; }
        public bool is_select { get; set; }
        public string name { get; set; }
        public string content { get; set; }
        public string launch_params { get; set; } = "";
    }

    private void EnsureDefaultProfilesExist()
    {
        lock (FileLock)
        {
            string configsDir = Path.Combine(AppPaths.Root, "Configs");
            string bindsDir = Path.Combine(AppPaths.Root, "Binds");
            Directory.CreateDirectory(configsDir);
            Directory.CreateDirectory(bindsDir);

            var cfgList = LoadProfilesInternal(ConfigsStoreFilePath);
            if (cfgList.Count == 0)
            {
                string id = Guid.NewGuid().ToString("N");
                string filePath = Path.Combine(configsDir, $"{id}.cfg");
                string gameCfg = SteamAndRustService.GetClientCfgPath();
                string initialContent = (!string.IsNullOrWhiteSpace(gameCfg) && File.Exists(gameCfg)) ? File.ReadAllText(gameCfg, Encoding.UTF8) : "";
                File.WriteAllText(filePath, initialContent, Encoding.UTF8);
                cfgList.Add(new ProfileRecordDto
                {
                    id = id,
                    is_select = true,
                    name = "Стандартный конфиг",
                    content = filePath,
                    launch_params = getCurrentParamsLaunch()
                });
                SaveProfilesInternal(ConfigsStoreFilePath, cfgList);
            }

            var bindList = LoadProfilesInternal(BindsStoreFilePath);
            if (bindList.Count == 0)
            {
                string id = Guid.NewGuid().ToString("N");
                string filePath = Path.Combine(bindsDir, $"{id}.cfg");
                string gameKeys = SteamAndRustService.GetKeysCfgPath();
                string initialContent = (!string.IsNullOrWhiteSpace(gameKeys) && File.Exists(gameKeys)) ? File.ReadAllText(gameKeys, Encoding.UTF8) : "";
                File.WriteAllText(filePath, initialContent, Encoding.UTF8);
                bindList.Add(new ProfileRecordDto
                {
                    id = id,
                    is_select = true,
                    name = "Стандартные бинды",
                    content = filePath,
                    launch_params = ""
                });
                SaveProfilesInternal(BindsStoreFilePath, bindList);
            }
        }
    }

    private static List<ProfileRecordDto> LoadProfilesInternal(string storePath)
    {
        lock (FileLock)
        {
            try
            {
                if (File.Exists(storePath))
                {
                    var list = JsonSerializer.Deserialize<List<ProfileRecordDto>>(File.ReadAllText(storePath, Encoding.UTF8));
                    if (list != null) return list;
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
            return new List<ProfileRecordDto>();
        }
    }

    private static void SaveProfilesInternal(string storePath, List<ProfileRecordDto> list)
    {
        lock (FileLock)
        {
            string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(storePath, json, Encoding.UTF8);
        }
    }

    public string getFullConfigs()
    {
        EnsureDefaultProfilesExist();
        return JsonSerializer.Serialize(LoadProfilesInternal(ConfigsStoreFilePath));
    }

    public string getFullBinds()
    {
        EnsureDefaultProfilesExist();
        return JsonSerializer.Serialize(LoadProfilesInternal(BindsStoreFilePath));
    }

    public string getConfigVersions(string id) => "[]";
    public string getBindVersions(string id) => "[]";
    public string getConfigsBackup() => "[]";
    public string getKeysBackup() => "[]";
    public bool CreateBackupToConfig(string path) => true;
    public bool CreateBackupToBind(string path) => true;
    public string restoreConfigVersion(string backupFile) => "OK";
    public string restoreBindVersion(string backupFile) => "OK";

    public string createConfig()
    {
        GetSelectedFolder();
        var list = LoadProfilesInternal(ConfigsStoreFilePath);
        string id = Guid.NewGuid().ToString("N");
        string filePath = Path.Combine(AppPaths.Root, "Configs", $"{id}.cfg");
        string gameCfg = SteamAndRustService.GetClientCfgPath();
        string content = (!string.IsNullOrWhiteSpace(gameCfg) && File.Exists(gameCfg)) ? File.ReadAllText(gameCfg, Encoding.UTF8) : "";
        File.WriteAllText(filePath, content, Encoding.UTF8);
        foreach (var item in list) item.is_select = false;
        list.Add(new ProfileRecordDto
        {
            id = id,
            is_select = true,
            name = $"New Config {list.Count + 1}",
            content = filePath,
            launch_params = getCurrentParamsLaunch()
        });
        SaveProfilesInternal(ConfigsStoreFilePath, list);
        return filePath;
    }

    public string createBind()
    {
        GetSelectedFolder();
        var list = LoadProfilesInternal(BindsStoreFilePath);
        string id = Guid.NewGuid().ToString("N");
        string filePath = Path.Combine(AppPaths.Root, "Binds", $"{id}.cfg");
        string gameKeys = SteamAndRustService.GetKeysCfgPath();
        string content = (!string.IsNullOrWhiteSpace(gameKeys) && File.Exists(gameKeys)) ? File.ReadAllText(gameKeys, Encoding.UTF8) : "";
        File.WriteAllText(filePath, content, Encoding.UTF8);
        foreach (var item in list) item.is_select = false;
        list.Add(new ProfileRecordDto
        {
            id = id,
            is_select = true,
            name = $"New Binds {list.Count + 1}",
            content = filePath,
            launch_params = ""
        });
        SaveProfilesInternal(BindsStoreFilePath, list);
        return filePath;
    }

    public bool addConfig(string path, string name) => RegisterOrUpdateProfile(ConfigsStoreFilePath, path, name, null);
    public bool addConfigFull(string path, string launchParams, string fileName) => RegisterOrUpdateProfile(ConfigsStoreFilePath, path, fileName, launchParams);
    public bool addConfigOnlyPath(string path) => RegisterOrUpdateProfile(ConfigsStoreFilePath, path, null, null);
    public bool addConfigPathAndLaunchParams(string path, string launchParams) => RegisterOrUpdateProfile(ConfigsStoreFilePath, path, null, launchParams);
    public bool createOrUpdateConfigsElement(string clientPath, string launchParams) => RegisterOrUpdateProfile(ConfigsStoreFilePath, clientPath, null, launchParams);
    public bool addBind(string path, string name) => RegisterOrUpdateProfile(BindsStoreFilePath, path, name, null);
    public bool addBindOnlyPath(string path) => RegisterOrUpdateProfile(BindsStoreFilePath, path, null, null);

    private bool RegisterOrUpdateProfile(string storePath, string path, string name, string launchParams)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var list = LoadProfilesInternal(storePath);
            var existing = list.FirstOrDefault(x => string.Equals(x.content, path, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                string id = Guid.NewGuid().ToString("N");
                string targetDir = Path.Combine(AppPaths.Root, storePath == ConfigsStoreFilePath ? "Configs" : "Binds");
                Directory.CreateDirectory(targetDir);
                string storedFile = path;
                if (File.Exists(path) && !path.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase))
                {
                    storedFile = Path.Combine(targetDir, $"{id}.cfg");
                    File.Copy(path, storedFile, overwrite: true);
                }
                existing = new ProfileRecordDto
                {
                    id = id,
                    is_select = list.Count == 0,
                    name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name,
                    content = storedFile,
                    launch_params = launchParams ?? ""
                };
                list.Add(existing);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(name)) existing.name = name;
                if (launchParams != null) existing.launch_params = launchParams;
            }
            SaveProfilesInternal(storePath, list);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return false;
        }
    }

    public string copyConfig(string sourcePath) => DuplicateProfile(ConfigsStoreFilePath, sourcePath)?.content;
    public string copyBind(string sourcePath) => DuplicateProfile(BindsStoreFilePath, sourcePath)?.content;
    public string createCopy(string sourcePath)
    {
        var rec = DuplicateProfile(ConfigsStoreFilePath, sourcePath);
        return rec == null ? null : JsonSerializer.Serialize(new { name = rec.name, path = rec.content });
    }
    public string createCopyBind(string sourcePath)
    {
        var rec = DuplicateProfile(BindsStoreFilePath, sourcePath);
        return rec == null ? null : JsonSerializer.Serialize(new { name = rec.name, path = rec.content });
    }
    public string copyFileInConfigs(string sourcePath) => copyConfig(sourcePath);

    private ProfileRecordDto DuplicateProfile(string storePath, string sourcePath)
    {
        try
        {
            var list = LoadProfilesInternal(storePath);
            var src = list.FirstOrDefault(x => string.Equals(x.content, sourcePath, StringComparison.OrdinalIgnoreCase));
            string id = Guid.NewGuid().ToString("N");
            string targetDir = Path.Combine(AppPaths.Root, storePath == ConfigsStoreFilePath ? "Configs" : "Binds");
            Directory.CreateDirectory(targetDir);
            string destFile = Path.Combine(targetDir, $"{id}.cfg");
            if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
                File.Copy(sourcePath, destFile, overwrite: true);
            else if (src != null && File.Exists(src.content))
                File.Copy(src.content, destFile, overwrite: true);
            else
                File.WriteAllText(destFile, "", Encoding.UTF8);

            var copy = new ProfileRecordDto
            {
                id = id,
                is_select = false,
                name = (src != null ? src.name : Path.GetFileNameWithoutExtension(sourcePath)) + " (Copy)",
                content = destFile,
                launch_params = src?.launch_params ?? ""
            };
            list.Add(copy);
            SaveProfilesInternal(storePath, list);
            return copy;
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return null;
        }
    }

    public Task exportConfig(string path, string name = null)
    {
        var tcs = new TaskCompletionSource();
        _window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        FileName = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : (name.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) ? name : name + ".cfg"),
                        Filter = "Config files (*.cfg)|*.cfg|All files (*.*)|*.*"
                    };
                    if (dlg.ShowDialog(_window) == true)
                        File.Copy(path, dlg.FileName, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
            tcs.SetResult();
        });
        return tcs.Task;
    }

    public string selectConfig(string path) => ApplyProfileByPath(ConfigsStoreFilePath, path, isBinds: false);
    public string selectBind(string path) => ApplyProfileByPath(BindsStoreFilePath, path, isBinds: true);
    public void applyConfig(string sourcePath) => ApplyProfileByPath(ConfigsStoreFilePath, sourcePath, isBinds: false);
    public void applyBind(string sourcePath) => ApplyProfileByPath(BindsStoreFilePath, sourcePath, isBinds: true);

    private string ApplyProfileByPath(string storePath, string path, bool isBinds)
    {
        try
        {
            var list = LoadProfilesInternal(storePath);
            var target = list.FirstOrDefault(x => string.Equals(x.content, path, StringComparison.OrdinalIgnoreCase));
            if (target == null) return "NOT_FOUND";
            foreach (var item in list) item.is_select = (item == target);
            SaveProfilesInternal(storePath, list);

            string folder = GetSelectedFolder();
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) && File.Exists(target.content))
            {
                EnsureRustCfgFilesExist(folder);
                string gameFile = isBinds ? SteamAndRustService.GetKeysCfgPath() : SteamAndRustService.GetClientCfgPath();
                if (!string.IsNullOrWhiteSpace(gameFile))
                    File.Copy(target.content, gameFile, overwrite: true);
                if (!isBinds && target.launch_params != null)
                {
                    SteamAndRustService.WriteLaunchOptions(target.launch_params);
                }
            }
            return "OK";
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return "ERROR";
        }
    }

    public string renameConfig(string path, string newName) => RenameProfileByPath(ConfigsStoreFilePath, path, newName);
    public string renameBind(string path, string newName) => RenameProfileByPath(BindsStoreFilePath, path, newName);

    private static string RenameProfileByPath(string storePath, string path, string newName)
    {
        var list = LoadProfilesInternal(storePath);
        var target = list.FirstOrDefault(x => string.Equals(x.content, path, StringComparison.OrdinalIgnoreCase));
        if (target == null) return "NOT_FOUND";
        target.name = newName ?? target.name;
        SaveProfilesInternal(storePath, list);
        return "OK";
    }

    public string removeConfig(string path) => RemoveProfileByPath(ConfigsStoreFilePath, path);
    public string removeBind(string path) => RemoveProfileByPath(BindsStoreFilePath, path);

    private static string RemoveProfileByPath(string storePath, string path)
    {
        var list = LoadProfilesInternal(storePath);
        int removed = list.RemoveAll(x => string.Equals(x.content, path, StringComparison.OrdinalIgnoreCase));
        if (removed <= 0) return "NOT_FOUND";
        if (!list.Any(x => x.is_select) && list.Count > 0)
            list[0].is_select = true;
        SaveProfilesInternal(storePath, list);
        return "OK";
    }

    public bool UpdateCfgToCurrentSettings(string path) => SyncGameToProfile(ConfigsStoreFilePath, path, isBinds: false);
    public bool UpdateBindToCurrentSettings(string path) => SyncGameToProfile(BindsStoreFilePath, path, isBinds: true);

    private bool SyncGameToProfile(string storePath, string path, bool isBinds)
    {
        try
        {
            GetSelectedFolder();
            var list = LoadProfilesInternal(storePath);
            var target = string.IsNullOrWhiteSpace(path)
                ? list.FirstOrDefault(x => x.is_select)
                : list.FirstOrDefault(x => string.Equals(x.content, path, StringComparison.OrdinalIgnoreCase));
            if (target == null) return false;
            string gameFile = isBinds ? SteamAndRustService.GetKeysCfgPath() : SteamAndRustService.GetClientCfgPath();
            if (!string.IsNullOrWhiteSpace(gameFile) && File.Exists(gameFile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target.content)!);
                File.Copy(gameFile, target.content, overwrite: true);
            }
            if (!isBinds)
            {
                target.launch_params = getCurrentParamsLaunch();
                SaveProfilesInternal(storePath, list);
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return false;
        }
    }

    public bool syncConfigsFromGame() => SyncGameToProfile(ConfigsStoreFilePath, null, isBinds: false);
    public bool syncBindsFromGame() => SyncGameToProfile(BindsStoreFilePath, null, isBinds: true);

    public bool ensureConfigsInitialized(string settingsName, string bindsName)
    {
        EnsureDefaultProfilesExist();
        return true;
    }

    public bool fileComparison(string path1, string path2)
    {
        if (string.IsNullOrWhiteSpace(path1) || string.IsNullOrWhiteSpace(path2) || !File.Exists(path1) || !File.Exists(path2))
            return false;
        string c1 = File.ReadAllText(path1, Encoding.UTF8).Replace("\r\n", "\n").Trim();
        string c2 = File.ReadAllText(path2, Encoding.UTF8).Replace("\r\n", "\n").Trim();
        return string.Equals(c1, c2, StringComparison.Ordinal);
    }

    public bool replaceFileContent(string pathFrom, string pathTo)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pathFrom) || string.IsNullOrWhiteSpace(pathTo) || !File.Exists(pathFrom))
                return false;
            File.Copy(pathFrom, pathTo, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return false;
        }
    }

    public bool updateLaunchParamsInConfig(string pathToCfg, string newLaunchParams)
    {
        try
        {
            var list = LoadProfilesInternal(ConfigsStoreFilePath);
            var target = list.FirstOrDefault(x => string.Equals(x.content, pathToCfg, StringComparison.OrdinalIgnoreCase));
            if (target == null) return false;
            target.launch_params = newLaunchParams ?? "";
            SaveProfilesInternal(ConfigsStoreFilePath, list);
            if (target.is_select)
            {
                GetSelectedFolder();
                SteamAndRustService.WriteLaunchOptions(target.launch_params);
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return false;
        }
    }

    // --- Binds Page (Local Parser & Builder for keys.cfg) ---
    private static readonly Dictionary<string, string> KeyIdToRustKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["key_1"] = "mouse0", ["key_2"] = "mouse2", ["key_3"] = "mouse1", ["key_4"] = "mouse3",
        ["key_5"] = "mouse4", ["key_6"] = "mouse5", ["key_7"] = "mouse6",
        ["key_8"] = "mousewheeldown", ["key_9"] = "mousewheelup",
        ["key_10"] = "a", ["key_11"] = "b", ["key_12"] = "c", ["key_13"] = "d", ["key_14"] = "e",
        ["key_15"] = "f", ["key_16"] = "g", ["key_17"] = "h", ["key_18"] = "i", ["key_19"] = "j",
        ["key_20"] = "k", ["key_21"] = "l", ["key_22"] = "m", ["key_23"] = "n", ["key_24"] = "o",
        ["key_25"] = "p", ["key_26"] = "q", ["key_27"] = "r", ["key_28"] = "s", ["key_29"] = "t",
        ["key_30"] = "u", ["key_31"] = "v", ["key_32"] = "w", ["key_33"] = "x", ["key_34"] = "y",
        ["key_35"] = "z",
        ["key_36"] = "1", ["key_37"] = "2", ["key_38"] = "3", ["key_39"] = "4", ["key_40"] = "5",
        ["key_41"] = "6", ["key_42"] = "7", ["key_43"] = "8", ["key_44"] = "9", ["key_45"] = "0",
        ["key_46"] = "space", ["key_47"] = "backquote", ["key_48"] = "minus", ["key_49"] = "equals",
        ["key_50"] = "insert", ["key_51"] = "home", ["key_52"] = "pageup", ["key_53"] = "pagedown",
        ["key_54"] = "delete", ["key_55"] = "end", ["key_56"] = "tab",
        ["key_57"] = "f1", ["key_58"] = "f2", ["key_59"] = "f3", ["key_60"] = "f4", ["key_61"] = "f5",
        ["key_62"] = "f6", ["key_63"] = "f7", ["key_64"] = "f8", ["key_65"] = "f9", ["key_66"] = "f10",
        ["key_67"] = "f11", ["key_68"] = "f12", ["key_69"] = "f13", ["key_70"] = "f14", ["key_71"] = "f15",
        ["key_72"] = "print", ["key_73"] = "scrolllock", ["key_74"] = "numlock",
        ["key_75"] = "keypaddivide", ["key_76"] = "keypadmultiply", ["key_77"] = "keypadminus",
        ["key_78"] = "keypadplus", ["key_79"] = "keypadenter", ["key_80"] = "keypadperiod",
        ["key_81"] = "keypad1", ["key_82"] = "keypad2", ["key_83"] = "keypad3", ["key_84"] = "keypad4",
        ["key_85"] = "keypad5", ["key_86"] = "keypad6", ["key_87"] = "keypad7", ["key_88"] = "keypad8",
        ["key_89"] = "keypad9", ["key_90"] = "keypad0",
        ["key_91"] = "up", ["key_92"] = "down", ["key_93"] = "left", ["key_94"] = "right",
        ["key_95"] = "rightcontrol", ["key_96"] = "leftcontrol",
        ["key_97"] = "rightshift", ["key_98"] = "leftshift",
        ["key_99"] = "rightalt", ["key_100"] = "leftalt",
        ["key_101"] = "slash", ["key_102"] = "period", ["key_103"] = "comma", ["key_104"] = "quote",
        ["key_105"] = "backquote", ["key_106"] = "semicolon", ["key_107"] = "leftbracket", ["key_108"] = "rightbracket",
        ["key_109"] = "return", ["key_110"] = "backslash", ["key_111"] = "capslock",
        ["key_112"] = "leftcommand", ["key_113"] = "backspace"
    };

    private static readonly Dictionary<string, string> RustKeyToKeyId =
        KeyIdToRustKey.GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                      .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> CommandIdToCmd = new(StringComparer.OrdinalIgnoreCase)
    {
        ["com_1"] = "audio.eventaudio", ["com_2"] = "audio.game", ["com_3"] = "audio.instruments", ["com_4"] = "audio.master",
        ["com_5"] = "audio.musicvolume", ["com_6"] = "audio.musicvolumemenu", ["com_7"] = "audio.ui", ["com_8"] = "audio.voices",
        ["com_9"] = "debug.drawcolliders", ["com_10"] = "debug.stall", ["com_11"] = "buttons.chat", ["com_12"] = "buttons.clearmarkers",
        ["com_13"] = "buttons.compass", ["com_14"] = "buttons.focusmap", ["com_15"] = "buttons.gestures", ["com_16"] = "buttons.map",
        ["com_17"] = "graphics.compass", ["com_18"] = "graphics.hud", ["com_19"] = "debug.debugcamera", ["com_20"] = "client.consoletoggle",
        ["com_21"] = "client.fps", ["com_22"] = "client.ping", ["com_23"] = "console.clear", ["com_24"] = "graphics.vram_report",
        ["com_25"] = "client.showsashes", ["com_26"] = "effects.showoutlines", ["com_27"] = "graphics.shaderlod",
        ["com_28"] = "graphicssettings.globaltexturemipmaplimit", ["com_29"] = "client.building_guide_mode", ["com_30"] = "effects.antialiasing",
        ["com_31"] = "input.ads_sensitivity", ["com_32"] = "input.sensitivity", ["com_33"] = "input.vehicle_sensitivity",
        ["com_34"] = "mesh.quality", ["com_35"] = "global.perf", ["com_36"] = "fps.limit", ["com_37"] = "graphics.drawdistance",
        ["com_38"] = "graphics.fov", ["com_39"] = "client.drawrangevolumes", ["com_40"] = "client.hitcross", ["com_41"] = "client.crosshair",
        ["com_42"] = "client.hurtpunch", ["com_43"] = "client.headbob", ["com_44"] = "graphics.dof", ["com_45"] = "graphics.vm_horizontal_flip",
        ["com_46"] = "nametags.enabled", ["com_47"] = "effects.ao", ["com_48"] = "effects.sharpen", ["com_49"] = "effects.motionblur",
        ["com_50"] = "effects.vignet", ["com_51"] = "global.adminambientmultiplier", ["com_52"] = "global.admintime",
        ["com_53"] = "debug.noclip", ["com_54"] = "debug.repair_inventory", ["com_55"] = "global.teleport2death",
        ["com_56"] = "global.teleport2marker", ["com_57"] = "global.teleporteveryone2me", ["com_58"] = "growableentity.growall",
        ["com_59"] = "inventory.clearinventory", ["com_60"] = "inventory.pipette", ["com_61"] = "creative.freebuild",
        ["com_62"] = "env.progresstime", ["com_63"] = "global.godforceoffoverlay", ["com_64"] = "global.god", ["com_65"] = "debug.invis",
        ["com_66"] = "player.infiniteammo", ["com_67"] = "creative.unlimitedio", ["com_68"] = "global.enable_marker_teleport",
        ["com_69"] = "entity.spawn", ["com_70"] = "global.say", ["com_71"] = "client.openuseroverlay", ["com_72"] = "gametip.showtoast",
        ["com_73"] = "client.reportplayer", ["com_74"] = "fps.startlogging", ["com_75"] = "fps.stoplogging", ["com_76"] = "global.buildinfo",
        ["com_77"] = "ai.killanimals", ["com_78"] = "ai.killscientists", ["com_79"] = "ai.ignoreplayers", ["com_80"] = "ai.move",
        ["com_81"] = "ai.npc_enable", ["com_82"] = "server.stability", ["com_83"] = "global.adminrain", ["com_84"] = "global.adminclouds",
        ["com_85"] = "global.adminfog", ["com_86"] = "client.camlookspeed", ["com_87"] = "client.camspeed", ["com_88"] = "client.camzoomspeed",
        ["com_89"] = "client.camlerp", ["com_90"] = "client.camzoomlerp", ["com_91"] = "global.adminlight", ["com_92"] = "client.camdist",
        ["com_93"] = "client.camfov", ["com_94"] = "debugcamera_targetbind", ["com_95"] = "graphics.biomefogdensityscale",
        ["com_96"] = "demo.timescale", ["com_97"] = "global.underwater_cinematic", ["com_98"] = "global.underwater_effect",
        ["com_99"] = "global.underwater_scatter", ["com_100"] = "inventory.selectitem", ["com_101"] = "craft.add", ["com_102"] = "craft.cancel",
        ["com_103"] = "client.lookatradius", ["com_104"] = "buttons.altlook", ["com_105"] = "buttons.attack", ["com_106"] = "buttons.attack2",
        ["com_107"] = "buttons.attack3", ["com_108"] = "buttons.autowalk", ["com_109"] = "buttons.backpack", ["com_110"] = "buttons.backward",
        ["com_111"] = "buttons.dropitemsingle", ["com_112"] = "buttons.dropitemstack", ["com_113"] = "buttons.duck", ["com_114"] = "buttons.firemode",
        ["com_115"] = "buttons.forward", ["com_116"] = "buttons.holsteritem", ["com_117"] = "buttons.hoverloot", ["com_118"] = "buttons.invnext",
        ["com_119"] = "buttons.invprev", ["com_120"] = "client.disconnect", ["com_121"] = "inventory.examineheld", ["com_122"] = "inventory.lighttoggle",
        ["com_123"] = "inventory.toggle", ["com_124"] = "inventory.togglecrafting", ["com_125"] = "screenshot.takehiresscreenshot",
        ["com_126"] = "buttons.jump", ["com_127"] = "buttons.left", ["com_128"] = "buttons.markcurrentpos", ["com_129"] = "buttons.nextskin",
        ["com_130"] = "buttons.ping", ["com_131"] = "buttons.prevskin", ["com_132"] = "buttons.reload", ["com_133"] = "buttons.right",
        ["com_134"] = "buttons.slot1", ["com_135"] = "buttons.slot2", ["com_136"] = "buttons.slot3", ["com_137"] = "buttons.slot4",
        ["com_138"] = "buttons.slot5", ["com_139"] = "buttons.slot6", ["com_140"] = "buttons.sprint", ["com_141"] = "buttons.use",
        ["com_142"] = "buttons.voice", ["com_143"] = "buttons.wireslackdown", ["com_144"] = "buttons.wireslackup",
        ["com_145"] = "buttons.zoomdecrease", ["com_146"] = "buttons.zoomincrease", ["com_147"] = "buttons.zoomtoggle",
        ["com_148"] = "chat.clear", ["com_149"] = "chat.open", ["com_150"] = "chat.enabled", ["com_151"] = "graphics.chat",
        ["com_152"] = "chat.muteglobal", ["com_153"] = "clan.toggleclan", ["com_154"] = "ent", ["com_155"] = "env.skyrotation",
        ["com_156"] = "buttons.pets", ["com_157"] = "chat.add", ["com_158"] = "global.kill", ["com_159"] = "buttons.notea",
        ["com_160"] = "buttons.noteb", ["com_161"] = "buttons.notec", ["com_162"] = "buttons.noted", ["com_163"] = "buttons.notee",
        ["com_164"] = "buttons.notef", ["com_165"] = "buttons.noteg", ["com_166"] = "buttons.notesharpmod", ["com_167"] = "buttons.noteoctaveupmod",
        ["com_168"] = "vehicle.swaptoseat", ["com_169"] = "vehicle.swapseats", ["com_170"] = "inventory.give", ["com_171"] = "inventory.givearm"
    };

    private static readonly Dictionary<string, string> CmdToCommandId =
        CommandIdToCmd.GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                      .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);

    public Task<string> getBindsList()
    {
        return Task.Run(() =>
        {
            try
            {
                GetSelectedFolder();
                string keysPath = SteamAndRustService.GetKeysCfgPath();
                var result = new List<object>();
                if (string.IsNullOrWhiteSpace(keysPath) || !File.Exists(keysPath))
                    return "[]";

                foreach (string rawLine in File.ReadAllLines(keysPath, Encoding.UTF8))
                {
                    string trimmed = rawLine.Trim();
                    if (string.IsNullOrEmpty(trimmed) || !trimmed.StartsWith("bind ", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string rest = trimmed[5..].Trim();
                    int sp = rest.IndexOf(' ');
                    if (sp <= 0) continue;

                    string keySpec = rest[..sp].Trim();
                    string cmdSpec = rest[(sp + 1)..].Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(cmdSpec) || cmdSpec == "7") continue;

                    string[] rustKeys = keySpec.Trim('[', ']').Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var keyIds = rustKeys.Select(k => RustKeyToKeyId.TryGetValue(k, out string kid) ? kid : k).ToArray();

                    string cleanCmd = cmdSpec.TrimStart('+', '~');
                    string firstToken = cleanCmd.Split(' ', ';')[0];
                    bool isHold = cmdSpec.StartsWith("+");
                    string cond = isHold ? "cond_2" : "cond_1";

                    string cid = "custom";
                    object valObj = new object[] { cmdSpec };
                    if (!cmdSpec.Contains(';') && !cmdSpec.Contains('~'))
                    {
                        if (CmdToCommandId.TryGetValue(firstToken, out string mappedCid))
                        {
                            cid = mappedCid;
                            string argPart = cleanCmd.Length > firstToken.Length ? cleanCmd[firstToken.Length..].Trim() : "";
                            valObj = string.IsNullOrEmpty(argPart) ? Array.Empty<object>() : new object[] { argPart };
                        }
                        else if (CmdToCommandId.TryGetValue("buttons." + firstToken, out string btnCid))
                        {
                            cid = btnCid;
                            valObj = Array.Empty<object>();
                        }
                    }

                    bool isDefault = cleanCmd.StartsWith("buttons.", StringComparison.OrdinalIgnoreCase) ||
                                     cleanCmd.Equals("client.consoletoggle", StringComparison.OrdinalIgnoreCase) ||
                                     cleanCmd.StartsWith("inventory.toggle", StringComparison.OrdinalIgnoreCase);

                    result.Add(new
                    {
                        raw = rawLine,
                        keys = keyIds,
                        is_default = isDefault,
                        commands = new[]
                        {
                            new
                            {
                                enabled = true,
                                stage = 1,
                                command_id = cid,
                                condition = cond,
                                value = valObj
                            }
                        }
                    });
                }

                return JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                return "[]";
            }
        });
    }

    public Task<string> createBindCommand(string jsonObject)
    {
        return Task.Run(() =>
        {
            try
            {
                var node = JsonNode.Parse(jsonObject);
                if (node == null) return "";

                var keysArr = node["keys"]?.AsArray();
                if (keysArr == null || keysArr.Count == 0) return "";
                var rustKeys = keysArr.Select(k => k?.ToString() ?? "")
                                      .Where(k => !string.IsNullOrWhiteSpace(k))
                                      .Select(k => KeyIdToRustKey.TryGetValue(k, out string rk) ? rk : k)
                                      .ToList();
                string keyStr = rustKeys.Count > 1 ? $"[{string.Join("+", rustKeys)}]" : rustKeys[0];

                var cmdsArr = node["commands"]?.AsArray();
                if (cmdsArr == null || cmdsArr.Count == 0)
                    return $"bind {keyStr} \"\"";

                var stageGroups = new SortedDictionary<int, List<string>>();
                foreach (var cmdNode in cmdsArr)
                {
                    if (cmdNode == null) continue;
                    bool enabled = cmdNode["enabled"]?.GetValue<bool>() ?? true;
                    if (!enabled) continue;

                    int stage = cmdNode["stage"]?.GetValue<int>() ?? 1;
                    string cid = cmdNode["command_id"]?.ToString() ?? "custom";
                    string cond = cmdNode["condition"]?.ToString() ?? "cond_1";
                    var valNode = cmdNode["value"];

                    string builtCmd;
                    if (cid == "custom")
                    {
                        if (valNode is JsonArray va && va.Count > 0)
                            builtCmd = string.Join(" ", va.Select(x => x?.ToString()));
                        else
                            builtCmd = valNode?.ToString() ?? "";
                    }
                    else
                    {
                        string baseCmd = CommandIdToCmd.GetValueOrDefault(cid, cid);
                        var args = new List<string>();
                        if (valNode is JsonArray va)
                        {
                            foreach (var item in va)
                            {
                                if (item == null) continue;
                                args.Add(item.ToString());
                            }
                        }
                        else if (valNode != null)
                        {
                            args.Add(valNode.ToString());
                        }
                        builtCmd = args.Count > 0 ? $"{baseCmd} {string.Join(" ", args)}" : baseCmd;
                    }

                    if (string.IsNullOrWhiteSpace(builtCmd)) continue;

                    if (cond == "cond_2" && !builtCmd.StartsWith("+"))
                        builtCmd = "+" + builtCmd;
                    else if (cond == "cond_3")
                        builtCmd = $"+meta.if_true \"{builtCmd}\"";
                    else if (cond == "cond_4")
                        builtCmd = $"+meta.if_false \"{builtCmd}\"";

                    if (!stageGroups.TryGetValue(stage, out var list))
                    {
                        list = new List<string>();
                        stageGroups[stage] = list;
                    }
                    list.Add(builtCmd);
                }

                string finalCommand;
                if (stageGroups.Count == 0)
                    finalCommand = "";
                else if (stageGroups.Count == 1)
                    finalCommand = string.Join(";", stageGroups.Values.First());
                else
                    finalCommand = string.Join(";", stageGroups.Values.Select(g => "~" + string.Join(";", g)));

                return $"bind {keyStr} {finalCommand}";
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                return "";
            }
        });
    }

    public Task applyBindInFile(string bindLine) => replaceBindInFile(bindLine, null);

    public Task replaceBindInFile(string bindLine, string previousLine)
    {
        return Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(bindLine)) return;
                string folder = GetSelectedFolder();
                string keysPath = SteamAndRustService.GetKeysCfgPath();
                if (string.IsNullOrWhiteSpace(keysPath)) return;
                EnsureRustCfgFilesExist(folder);

                var lines = File.Exists(keysPath) ? File.ReadAllLines(keysPath, Encoding.UTF8).ToList() : new List<string>();
                bool replaced = false;
                if (!string.IsNullOrWhiteSpace(previousLine))
                {
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (string.Equals(lines[i].Trim(), previousLine.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            lines[i] = bindLine;
                            replaced = true;
                            break;
                        }
                    }
                }
                if (!replaced)
                    lines.Add(bindLine);

                File.WriteAllLines(keysPath, lines, Encoding.UTF8);
                syncBindsFromGame();
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        });
    }

    public Task deleteBindFromCfg(string element)
    {
        return Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(element)) return;
                var node = JsonNode.Parse(element);
                string raw = node?["raw"]?.ToString();
                if (string.IsNullOrWhiteSpace(raw)) return;

                GetSelectedFolder();
                string keysPath = SteamAndRustService.GetKeysCfgPath();
                if (string.IsNullOrWhiteSpace(keysPath) || !File.Exists(keysPath)) return;

                var lines = File.ReadAllLines(keysPath, Encoding.UTF8)
                                .Where(l => !string.Equals(l.Trim(), raw.Trim(), StringComparison.OrdinalIgnoreCase))
                                .ToArray();
                File.WriteAllLines(keysPath, lines, Encoding.UTF8);
                syncBindsFromGame();
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        });
    }

    public void saveBindObject(string bindList, string path)
    {
        lock (FileLock)
        {
            try
            {
                Dictionary<string, string> dict = null;
                if (File.Exists(BindsCacheFilePath))
                {
                    try { dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(BindsCacheFilePath, Encoding.UTF8)); }
                    catch { dict = null; }
                }
                dict ??= new Dictionary<string, string>();
                dict[path ?? "tmp"] = bindList ?? "[]";
                File.WriteAllText(BindsCacheFilePath, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        }
    }

    public bool containsBindObject(string path)
    {
        lock (FileLock)
        {
            try
            {
                if (!File.Exists(BindsCacheFilePath)) return false;
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(BindsCacheFilePath, Encoding.UTF8));
                return dict != null && dict.ContainsKey(path ?? "tmp");
            }
            catch
            {
                return false;
            }
        }
    }

    public string getBindObject(string path)
    {
        lock (FileLock)
        {
            try
            {
                if (!File.Exists(BindsCacheFilePath)) return "[]";
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(BindsCacheFilePath, Encoding.UTF8));
                return dict != null && dict.TryGetValue(path ?? "tmp", out string val) ? val : "[]";
            }
            catch
            {
                return "[]";
            }
        }
    }

    public string getBindNameByElement(string element, string path) => null;

    public void updateFavouriteCommands(string json)
    {
        lock (FileLock)
        {
            try
            {
                File.WriteAllText(FavouritesFilePath, string.IsNullOrWhiteSpace(json) ? "[]" : json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        }
    }

    public string[] getFavouriteCommands()
    {
        lock (FileLock)
        {
            try
            {
                if (File.Exists(FavouritesFilePath))
                {
                    var arr = JsonSerializer.Deserialize<string[]>(File.ReadAllText(FavouritesFilePath, Encoding.UTF8));
                    if (arr != null) return arr;
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
            return Array.Empty<string>();
        }
    }

    public Task<float> getCurrentSensivity()
    {
        return Task.Run(() =>
        {
            try
            {
                GetSelectedFolder();
                string cfgPath = SteamAndRustService.GetClientCfgPath();
                if (!string.IsNullOrWhiteSpace(cfgPath) && File.Exists(cfgPath))
                {
                    foreach (string line in File.ReadAllLines(cfgPath, Encoding.UTF8))
                    {
                        if (line.Contains("ads_sensitivity", StringComparison.OrdinalIgnoreCase))
                        {
                            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && float.TryParse(parts[1].Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                                return val;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
            return 1f;
        });
    }

    // --- Instruments: Screen Stretch & Zapret Fix ---
    public string getScreenStretch()
    {
        var (curW, curH, _) = ScreenStretchService.GetCurrentDisplayMode();
        bool active = ScreenStretchService.IsStretchActive();
        return JsonSerializer.Serialize(new
        {
            state = active ? "on" : "off",
            width = curW,
            height = curH,
            status = "ALLGOOD"
        });
    }

    public string setScreenStretch(string state)
    {
        var s = ScreenStretchService.LoadSettings();
        bool turnOn = string.Equals(state?.Trim('"'), "on", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(state?.Trim('"'), "true", StringComparison.OrdinalIgnoreCase);
        if (turnOn)
            ScreenStretchService.EnableStretch(s.SourceWidth, s.SourceHeight, s.TargetWidth, s.TargetHeight);
        else
            ScreenStretchService.DisableStretch();
        return getScreenStretch();
    }

    public string getScreenStretchSettings()
    {
        var s = ScreenStretchService.LoadSettings();
        var (curW, curH, _) = ScreenStretchService.GetCurrentDisplayMode();
        var resolutions = ScreenStretchService.GetSupportedResolutions()
            .Select(r => new { width = r.Width, height = r.Height, label = r.Label })
            .ToList();
        int targetIdx = resolutions.FindIndex(r => r.width == s.TargetWidth && r.height == s.TargetHeight);
        return JsonSerializer.Serialize(new
        {
            state = ScreenStretchService.IsStretchActive() ? "on" : "off",
            status = "ALLGOOD",
            current = new { width = curW, height = curH, label = $"{curW}x{curH}" },
            source = new { width = s.SourceWidth, height = s.SourceHeight, label = $"{s.SourceWidth}x{s.SourceHeight}" },
            target = new { width = s.TargetWidth, height = s.TargetHeight, label = $"{s.TargetWidth}x{s.TargetHeight}" },
            targetIndex = targetIdx >= 0 ? targetIdx : 0,
            resolutions
        });
    }

    public string setScreenStretchSettings(string data)
    {
        try
        {
            var s = ScreenStretchService.LoadSettings();
            var node = JsonNode.Parse(data ?? "{}");
            if (node != null)
            {
                var src = node["source"] ?? node;
                var tgt = node["target"] ?? node;
                int sw = src["sourceWidth"]?.GetValue<int>() ?? src["width"]?.GetValue<int>() ?? s.SourceWidth;
                int sh = src["sourceHeight"]?.GetValue<int>() ?? src["height"]?.GetValue<int>() ?? s.SourceHeight;
                int tw = tgt["targetWidth"]?.GetValue<int>() ?? tgt["width"]?.GetValue<int>() ?? s.TargetWidth;
                int th = tgt["targetHeight"]?.GetValue<int>() ?? tgt["height"]?.GetValue<int>() ?? s.TargetHeight;
                s.SourceWidth = sw;
                s.SourceHeight = sh;
                s.TargetWidth = tw;
                s.TargetHeight = th;
                ScreenStretchService.SaveSettings(s);
                if (ScreenStretchService.IsStretchActive())
                    ScreenStretchService.EnableStretch(sw, sh, tw, th);
            }
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
        }
        return getScreenStretchSettings();
    }

    public string getZapretFix()
    {
        bool zapret = ZapretFixService.IsZapretRunning();
        string drvState = ZapretFixService.GetWinDivertServiceState();
        bool driverActive = drvState.Contains("Работает", StringComparison.OrdinalIgnoreCase);
        return JsonSerializer.Serialize(new
        {
            state = !zapret ? "custom" : (!driverActive ? "on" : "off"),
            zapretRunning = zapret,
            drivers = new[]
            {
                new
                {
                    name = "windivert",
                    exists = driverActive,
                    runState = driverActive ? "Running" : "NotInstalled"
                }
            }
        });
    }

    public void applyZapretFix() => ZapretFixService.ApplyFix();

    // --- Camera Codes (Monument CCTV) ---
    public Task<string> getCameraCategories()
    {
        return Task.Run(() =>
        {
            var categories = CameraCodesCatalog.All
                .GroupBy(c => c.Monument)
                .Select(g => new
                {
                    id = ToCategorySlug(g.Key),
                    name = new { ru = g.Key, en = g.Key },
                    iconUrl = ""
                })
                .ToArray();
            return JsonSerializer.Serialize(categories);
        });
    }

    public Task<string> getCameraCodes()
    {
        return Task.Run(() =>
        {
            var codes = CameraCodesCatalog.All.Select(c =>
            {
                string catId = ToCategorySlug(c.Monument);
                return new
                {
                    id = c.Code,
                    code = c.Code,
                    name = new { ru = c.Location, en = c.Location },
                    tooltip = new { ru = $"{c.Monument} — {c.Location}", en = $"{c.Monument} — {c.Location}" },
                    category = new { id = catId, name = new { ru = c.Monument, en = c.Monument } },
                    thumbnailUrl = "",
                    videoUrl = ""
                };
            }).ToArray();
            return JsonSerializer.Serialize(codes);
        });
    }

    public string getCameraCodesJson()
    {
        var cats = JsonNode.Parse(getCameraCategories().GetAwaiter().GetResult());
        var codes = JsonNode.Parse(getCameraCodes().GetAwaiter().GetResult());
        var obj = new JsonObject
        {
            ["categories"] = cats,
            ["camera_codes"] = codes
        };
        return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ToCategorySlug(string monument)
    {
        if (string.IsNullOrWhiteSpace(monument)) return "other";
        int paren = monument.IndexOf('(');
        string english = paren >= 0 ? monument[(paren + 1)..].TrimEnd(')') : monument;
        return new string(english.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
    }

    // --- Offline Stubs for Statistics & Leaderboard ---
    public Task<string> getStatistics(string id) => Task.FromResult<string>(null);
    public Task<string> getFriendsPage(string id, int offset) => Task.FromResult("[]");
    public Task<string> getSimpleUserInfo(string id) => Task.FromResult<string>(null);
    public Task<string> getLeaderboard(string statType, int limit) => Task.FromResult("[]");
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public class OptimizationsBridge
{
    private static readonly Dictionary<string, Func<ITweak>> TweaksById = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disable-hags"] = () => new DisableHags(),
        ["disable-hvci"] = () => new DisableHvci(),
        ["enable-game-mode"] = () => new EnableGameModeOptimization(),
        ["disable-drtp"] = () => new DisableDRTP(),
        ["disable-pcie-lpm"] = () => new DisablePciLpm(),
        ["windows-power-plan"] = () => new WindowsPowerPlan(),
        ["disable-unused-services"] = () => new DisableUnusedServices(),
        ["disable-xbox-game-bar"] = () => new DisableXboxGameBarOptimization(),
        ["auto-pagefile"] = () => new PagefileOptimization(),
        ["auto-gc-buffer"] = () => new AutoGcBuffer(),
        ["disable-exclusive-fullscreen"] = () => new DisableExclusiveFullscreen(),
        ["auto-cpu-affinity"] = () => new AutoCpuAffinityTweak()
    };

    public Task<string> GetOptimizationStatus(string id)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!TweaksById.TryGetValue(id ?? "", out var factory))
                {
                    return JsonSerializer.Serialize(new
                    {
                        CurrentStatus = "Unsupported",
                        IsSupported = false,
                        NeedComputerRestart = false,
                        NeedSteamRestart = false
                    });
                }

                ITweak tweak = factory();
                TweakStatus status = tweak.GetStatus();
                return JsonSerializer.Serialize(new
                {
                    CurrentStatus = status.ToString(),
                    IsSupported = (status != TweakStatus.Unsupported),
                    NeedComputerRestart = tweak.NeedComputerRestart,
                    NeedSteamRestart = tweak.NeedSteamRestart
                });
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                return JsonSerializer.Serialize(new
                {
                    CurrentStatus = "Unsupported",
                    IsSupported = false,
                    NeedComputerRestart = false,
                    NeedSteamRestart = false
                });
            }
        });
    }

    public Task SetOptimizationStatus(string id, int targetStatus)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!TweaksById.TryGetValue(id ?? "", out var factory)) return;
                ITweak tweak = factory();
                TweakBackup.SaveIfMissing(tweak);
                // OptimizationTargetStatus: Good = 0, Bad = 1
                bool good = (targetStatus == 0);
                tweak.Apply(good);
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        });
    }

    public Task<string> ListOptimizationsBackups()
    {
        return Task.Run(() =>
        {
            try
            {
                var availableIds = TweaksById.Where(kv => TweakBackup.Has(kv.Value())).Select(kv => kv.Key).ToArray();
                if (availableIds.Length == 0) return "[]";
                string nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var entry = new[]
                {
                    new
                    {
                        id = "local-session-backup",
                        createdAtUtc = nowIso,
                        sessionStartedAtUtc = nowIso,
                        capturedAtUtc = nowIso,
                        machineName = Environment.MachineName,
                        ids = availableIds,
                        failedIds = Array.Empty<string>()
                    }
                };
                return JsonSerializer.Serialize(entry);
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                return "[]";
            }
        });
    }

    public Task<int> RestoreOptimizationsBackup(string snapshotId)
    {
        return Task.Run(() =>
        {
            try
            {
                int restored = 0;
                foreach (var kv in TweaksById)
                {
                    ITweak tweak = kv.Value();
                    if (TweakBackup.Has(tweak))
                    {
                        TweakBackup.Restore(tweak);
                        restored++;
                    }
                }
                return restored;
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                return -1;
            }
        });
    }

    public Task<int> RollbackOptimizations() => RestoreOptimizationsBackup("local-session-backup");
    public Task<bool> HasPendingBenchmarkBackups() => Task.FromResult(false);
    public Task<bool> RestorePendingBenchmarkBackups() => Task.FromResult(false);
    public bool consumeBenchmarkResultRedirectFlag() => false;
    public Task<int> initOptimizationPage() => Task.FromResult(200);

    // --- Local Hardware Monitoring Status ---
    public Task<string> getDiskStatus()
    {
        return Task.Run(() =>
        {
            try
            {
                string sysRoot = Path.GetPathRoot(Environment.SystemDirectory);
                if (!string.IsNullOrWhiteSpace(sysRoot))
                {
                    var drive = new DriveInfo(sysRoot);
                    if (drive.IsReady && drive.TotalSize > 0)
                    {
                        double freePct = drive.AvailableFreeSpace * 100.0 / drive.TotalSize;
                        if (freePct < 10.0)
                            return "[4]"; // LowSystemDiskFreeSpace
                    }
                }
                return "[1]"; // Ok
            }
            catch
            {
                return "[1]";
            }
        });
    }

    public Task<string> getRamStatus() => Task.FromResult("[1]");
    public Task<string> getTemperatureStatus() => Task.FromResult("[1]");

    // --- Offline Stubs for Benchmark Endpoints ---
    public Task<string> getMyDashboardInfo(bool frameGen = false) => Task.FromResult<string>(null);
    public Task<string> getMeHasHardwareTest() => Task.FromResult("{\"hasHardwareTest\":false}");
    public Task<string> getMeHistory(int limit, int offset) => Task.FromResult("{\"items\":[],\"total\":0}");
    public Task<string> getTopFiltered(string cpu, string gpu, string ramType, string ramVolume, int limit, int offset, string search = null) => Task.FromResult("{\"items\":[],\"total\":0}");
    public Task<string> getBenchmarkDevices() => Task.FromResult("{\"cpus\":[],\"gpus\":[],\"ramTypes\":[],\"ramVolumes\":[]}");
    public Task<string> getMeBenchmarkInfo(int submissionId) => Task.FromResult<string>(null);
    public Task<string> getUserLastBenchmark(string steamId, bool? frameGen = null) => Task.FromResult<string>(null);
    public Task<string> getHistogram() => Task.FromResult("[]");
    public Task<string> getUserBenchmarkBySubmission(string steamId, int submissionId) => Task.FromResult<string>(null);
    public Task<string> getUserBenchmarkHistory(string steamId, int limit, int offset) => Task.FromResult("{\"items\":[],\"total\":0}");
    public Task<string> getUserSettings() => Task.FromResult("{\"hiddenFromLeaderboard\":false}");
    public Task<string> setUserSettings(string patchJson) => Task.FromResult(patchJson ?? "{\"hiddenFromLeaderboard\":false}");
    public Task<int> startbenchmark(string id) => Task.FromResult(-1);
}
