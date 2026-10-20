using System.Text.Json;
using RustOptimizer.Core;

namespace RustOptimizer.Tweaks;

/// <summary>
/// Sets Rust's "-cpu.affinity" Steam launch option to one logical CPU per physical performance core
/// (restricted to the largest/V-Cache L3 domain on multi-CCD / hybrid CPUs) and disables Rust's own
/// "system.auto_cpu_affinity" in client.cfg. Port of the original AutoCpuAffinity optimization.
/// </summary>
public sealed class AutoCpuAffinityTweak : ITweak
{
    const string ClientCfgKey = "system.auto_cpu_affinity";
    const string LaunchArgumentName = "-cpu.affinity";
    const int MaxZen2CcxPhysicalCores = 4;

    public string Id => "AutoCpuAffinity";
    public string Title => "Auto CPU affinity";
    public string Description => "Pins Rust to one logical CPU per physical performance core (the biggest L3 cache domain on multi-CCX/hybrid CPUs) through the -cpu.affinity launch option and turns off Rust's built-in auto affinity.";
    public bool NeedComputerRestart => false;
    public bool NeedSteamRestart => true;

    public AutoCpuAffinityTweak() { }

    // ---------------- state types (serialized into the backup) ----------------

    internal sealed class LaunchArgumentState
    {
        public bool Exists { get; set; }
        public string Value { get; set; }
    }

    internal sealed class ClientCfgState
    {
        public bool Exists { get; set; }
        public int Index { get; set; }
        public string Line { get; set; }
    }

    internal sealed class CpuAffinitySnapshotState
    {
        public string ConfigPath { get; set; }
        public LaunchArgumentState Launch { get; set; }
        public string ClientCfgPath { get; set; }
        public ClientCfgState ClientCfg { get; set; }
    }

    // ---------------- ITweak ----------------

    public TweakStatus GetStatus()
    {
        try
        {
            string cfgPath = AcpuSteam.GetClientCfgPath();
            if (!string.IsNullOrEmpty(cfgPath) && File.Exists(cfgPath))
            {
                var lines = File.ReadAllLines(cfgPath);
                int idx = FindClientCfgLineIndex(lines);
                if (idx != -1)
                    return IsDisabledLine(lines[idx]) ? TweakStatus.Good : TweakStatus.Bad;
            }

            var words = SplitWords(AcpuSteam.ReadLaunchOptions());
            string good = GetGoodCpuAffinity();
            int i = words.IndexOf(LaunchArgumentName);
            if (i != -1 && i + 1 < words.Count && string.Equals(words[i + 1].Trim('"', '\\'), good))
                return TweakStatus.Good;
            return TweakStatus.Bad;
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to get AutoCpuAffinity status");
            Logger.Log(ex);
        }
        return TweakStatus.Unsupported;
    }

    public object CaptureState()
    {
        string cfgPath = AcpuSteam.GetClientCfgPath();
        var words = SplitWords(AcpuSteam.ReadLaunchOptions());
        int i = words.IndexOf(LaunchArgumentName);
        bool hasArg = i != -1 && i + 1 < words.Count;
        bool hasCfg = !string.IsNullOrEmpty(cfgPath);
        var lines = hasCfg && File.Exists(cfgPath) ? File.ReadAllLines(cfgPath).ToList() : new List<string>();
        return new CpuAffinitySnapshotState
        {
            ConfigPath = AcpuSteam.GetLocalConfigPath(),
            Launch = new LaunchArgumentState { Exists = hasArg, Value = hasArg ? words[i + 1] : null },
            ClientCfgPath = hasCfg ? cfgPath : null,
            ClientCfg = hasCfg ? CaptureClientCfgState(lines) : null
        };
    }

    public void RestoreState(JsonElement state)
    {
        var snap = TweakBackup.Read<CpuAffinitySnapshotState>(state);
        if (snap.Launch == null)
            throw new InvalidOperationException("The snapshot has no launch options state.");

        string configPath = AcpuSteam.GetLocalConfigPath();
        if (string.IsNullOrEmpty(snap.ConfigPath))
            throw new InvalidOperationException("The snapshot does not contain the Steam config path.");
        if (!string.Equals(snap.ConfigPath, configPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Steam account changed: the snapshot belongs to another profile ({snap.ConfigPath}), currently active is {configPath}. Launch options were not touched.");

        var words = SplitWords(AcpuSteam.ReadLaunchOptions());
        RestoreLaunchArgument(words, LaunchArgumentName, snap.Launch);
        AcpuSteam.WriteLaunchOptions(string.Join(' ', words));

        if (!string.IsNullOrEmpty(snap.ClientCfgPath) && snap.ClientCfg != null)
        {
            string cfgPath = AcpuSteam.GetClientCfgPath();
            if (!string.Equals(snap.ClientCfgPath, cfgPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Rust folder changed: the snapshot belongs to {snap.ClientCfgPath}, currently selected is {cfgPath ?? "none"}. Launch options were restored, client.cfg was not.");

            var lines = File.Exists(snap.ClientCfgPath) ? File.ReadAllLines(snap.ClientCfgPath).ToList() : new List<string>();
            RestoreClientCfgLine(lines, snap.ClientCfg);
            File.WriteAllLines(snap.ClientCfgPath, lines);
        }
    }

    public void Apply(bool good)
    {
        try
        {
            string cfgPath = AcpuSteam.GetClientCfgPath();
            bool hasCfg = !string.IsNullOrEmpty(cfgPath);
            var lines = hasCfg && File.Exists(cfgPath) ? File.ReadAllLines(cfgPath).ToList() : new List<string>();
            var words = SplitWords(AcpuSteam.ReadLaunchOptions());
            int idx = words.IndexOf(LaunchArgumentName);

            if (good)
            {
                string affinity = GetGoodCpuAffinity();
                if (string.IsNullOrEmpty(affinity))
                {
                    // Several processor groups: a single mask can't express it, so just drop our argument.
                    if (idx != -1) RemoveLaunchArgument(words, idx);
                }
                else if (idx == -1) { words.Add(LaunchArgumentName); words.Add(affinity); }
                else if (idx + 1 < words.Count) words[idx + 1] = affinity;
                else words.Add(affinity);

                ReplaceClientCfgLine(lines, FindClientCfgLineIndex(lines), ClientCfgKey + " \"False\"");
            }
            else
            {
                if (idx != -1) RemoveLaunchArgument(words, idx);
                RemoveClientCfgLines(lines);
            }

            AcpuSteam.WriteLaunchOptions(string.Join(' ', words));
            if (hasCfg) File.WriteAllLines(cfgPath, lines);
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            throw;
        }
    }

    // ---------------- launch option helpers ----------------

    static List<string> SplitWords(string s) =>
        (s ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    static void RestoreLaunchArgument(List<string> words, string name, LaunchArgumentState state)
    {
        int i = words.IndexOf(name);
        if (!state.Exists) { if (i != -1) RemoveLaunchArgument(words, i); }
        else if (i == -1) { words.Add(name); words.Add(state.Value ?? ""); }
        else if (i + 1 < words.Count) words[i + 1] = state.Value ?? "";
        else words.Add(state.Value ?? "");
    }

    static void RemoveLaunchArgument(List<string> words, int index) =>
        words.RemoveRange(index, index + 1 >= words.Count ? 1 : 2);

    // ---------------- client.cfg helpers ----------------

    static ClientCfgState CaptureClientCfgState(IReadOnlyList<string> lines)
    {
        int i = FindClientCfgLineIndex(lines);
        return new ClientCfgState { Exists = i != -1, Index = i, Line = i != -1 ? lines[i] : null };
    }

    static int FindClientCfgLineIndex(IReadOnlyList<string> lines)
    {
        for (int i = 0; i < lines.Count; i++)
            if (IsClientCfgKeyLine(lines[i])) return i;
        return -1;
    }

    static bool IsClientCfgKeyLine(string line) =>
        !string.IsNullOrEmpty(line) && line.TrimStart().StartsWith(ClientCfgKey, StringComparison.OrdinalIgnoreCase);

    static bool IsDisabledLine(string line) =>
        string.Equals(line.Trim(), ClientCfgKey + " \"False\"", StringComparison.OrdinalIgnoreCase);

    static void RemoveClientCfgLines(List<string> lines) => lines.RemoveAll(IsClientCfgKeyLine);

    static void ReplaceClientCfgLine(List<string> lines, int index, string line)
    {
        RemoveClientCfgLines(lines);
        if (index < 0 || index > lines.Count) lines.Add(line); else lines.Insert(index, line);
    }

    static void RestoreClientCfgLine(List<string> lines, ClientCfgState state)
    {
        RemoveClientCfgLines(lines);
        if (!state.Exists) return;
        string item = string.IsNullOrEmpty(state.Line) ? ClientCfgKey + " \"False\"" : state.Line;
        if (state.Index < 0 || state.Index > lines.Count) lines.Add(item); else lines.Insert(state.Index, item);
    }

    // ---------------- recommendation ----------------

    internal static AcpuRecommendation BuildRecommendation()
    {
        string processorName = GetProcessorName();
        AcpuCpuSet[] cpuSets = AcpuNative.GetCpuSets().OrderBy(c => c.Group).ThenBy(c => c.LogicalProcessorIndex).ToArray();
        AcpuL3Group[] l3 = AcpuNative.GetL3CacheGroups().OrderBy(c => c.Group).ThenByDescending(c => c.CacheSizeBytes).ThenBy(c => c.SharedMask).ToArray();

        AcpuLogicalCpu[] performance = GetPerformanceLogicalCpus(cpuSets);
        AcpuLogicalCpu[] all = DistinctSorted(cpuSets.Select(c => c.LogicalCpu));
        AcpuL3Group target = SelectIsolationTargetDomain(l3, cpuSets);
        AcpuLogicalCpu[] targetCpus = target != null ? DistinctSorted(target.LogicalCpus) : Array.Empty<AcpuLogicalCpu>();

        AcpuLogicalCpu[] selected;
        AcpuLogicalCpu[] onePerCore;
        if (cpuSets.Select(c => c.EfficiencyClass).Distinct().Count() > 1 || target != null)
        {
            AcpuLogicalCpu[] second = target != null ? targetCpus : all;
            selected = DistinctSorted(performance.Intersect(second));
            if (selected.Length == 0) selected = all;
            onePerCore = GetOneLogicalCpuPerPhysicalCore(cpuSets, selected);
            onePerCore = KeepSingleGroup(onePerCore);
            selected = KeepSingleGroup(selected);
        }
        else
        {
            selected = all;
            onePerCore = all;
        }
        return new AcpuRecommendation(processorName, cpuSets, l3, performance, targetCpus, selected, onePerCore);
    }

    static string GetProcessorName()
    {
        string text = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null)?.ToString();
        if (!string.IsNullOrEmpty(text)) return text.Trim();
        return Environment.ProcessorCount.ToString();
    }

    static string GetGoodCpuAffinity()
    {
        var cpus = BuildRecommendation().OneLogicalCpuPerPhysicalCore;
        if (cpus.Select(c => c.Group).Distinct().Count() > 1) return "";
        return FormatCpuList(cpus);
    }

    static AcpuLogicalCpu[] GetPerformanceLogicalCpus(IReadOnlyCollection<AcpuCpuSet> cpuSets)
    {
        if (cpuSets.Count == 0) return Array.Empty<AcpuLogicalCpu>();
        byte[] classes = cpuSets.Select(c => c.EfficiencyClass).Distinct().Order().ToArray();
        byte performanceClass = classes[^1];
        return cpuSets
            .Where(c => classes.Length == 1 || c.EfficiencyClass == performanceClass)
            .Select(c => c.LogicalCpu).Distinct()
            .OrderBy(c => c.Group).ThenBy(c => c.Index).ToArray();
    }

    static AcpuL3Group SelectIsolationTargetDomain(IReadOnlyList<AcpuL3Group> l3Caches, IReadOnlyCollection<AcpuCpuSet> cpuSets)
    {
        if (l3Caches.Count <= 1) return null;
        bool differentSizes = l3Caches.Select(c => c.CacheSizeBytes).Distinct().Count() > 1;
        AcpuL3Group top = l3Caches
            .OrderByDescending(c => c.CacheSizeBytes)
            .ThenByDescending(c => CountPhysicalCores(c, cpuSets))
            .ThenBy(c => c.Group).ThenBy(c => c.SharedMask)
            .First();
        if (differentSizes) return top;
        if (CountPhysicalCores(top, cpuSets) <= MaxZen2CcxPhysicalCores) return null;
        return top;
    }

    static int CountPhysicalCores(AcpuL3Group domain, IReadOnlyCollection<AcpuCpuSet> cpuSets)
    {
        var domainLogical = domain.LogicalCpus.ToHashSet();
        return cpuSets.Where(c => domainLogical.Contains(c.LogicalCpu)).Select(c => (c.Group, c.CoreIndex)).Distinct().Count();
    }

    static AcpuLogicalCpu[] DistinctSorted(IEnumerable<AcpuLogicalCpu> cpus) =>
        cpus.Distinct().OrderBy(c => c.Group).ThenBy(c => c.Index).ToArray();

    static AcpuLogicalCpu[] KeepSingleGroup(IReadOnlyCollection<AcpuLogicalCpu> cpus)
    {
        if (cpus.Count == 0) return Array.Empty<AcpuLogicalCpu>();
        ushort[] groups = cpus.Select(c => c.Group).Distinct().ToArray();
        if (groups.Length <= 1) return DistinctSorted(cpus);
        ushort primary = groups.Contains((ushort)0)
            ? (ushort)0
            : cpus.GroupBy(c => c.Group).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        return DistinctSorted(cpus.Where(c => c.Group == primary));
    }

    static AcpuLogicalCpu[] GetOneLogicalCpuPerPhysicalCore(IReadOnlyCollection<AcpuCpuSet> cpuSets, IReadOnlyCollection<AcpuLogicalCpu> selectedLogical)
    {
        var selected = selectedLogical.ToHashSet();
        return cpuSets
            .Where(c => selected.Contains(c.LogicalCpu))
            .GroupBy(c => (c.Group, c.CoreIndex))
            .Select(g => g.OrderByDescending(c => c.LogicalProcessorIndex).First().LogicalCpu)
            .OrderBy(c => c.Group).ThenBy(c => c.Index).ToArray();
    }

    static string FormatCpuList(IEnumerable<AcpuLogicalCpu> cpus) =>
        string.Join(",", cpus.OrderBy(c => c.Group).ThenBy(c => c.Index).Select(c => c.Index.ToString()));
}
