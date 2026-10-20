using System;
using System.Management;
using System.Text.Json;
using System.Text.RegularExpressions;
using RustOptimizer.Core;
using RustOptimizer.Rust;

namespace RustOptimizer.Tweaks;

/// <summary>
/// Configures the optimal Unity Garbage Collector buffer (-gc.buffer) in Steam Launch Options based on RAM and CPU cores.
/// </summary>
public sealed class AutoGcBuffer : ITweak
{
    private static readonly Regex GcBufferRegex = new(@"-gc\.buffer\s+(?:""(?<val>\d+)""|(?<val>\d+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Id => "AutoGcBuffer";
    public string Title => "Автоматический буфер сборщика мусора (-gc.buffer)";
    public string Description => "Подбирает оптимальный размер буфера сборщика мусора Unity под объём ОЗУ и ядра процессора, убирая микрофризы.";
    public bool NeedComputerRestart => false;
    public bool NeedSteamRestart => true;

    public sealed class GcBufferSnapshot
    {
        public string ConfigPath { get; set; }
        public string LaunchOptions { get; set; }
    }

    public TweakStatus GetStatus()
    {
        try
        {
            string options = SteamAndRustService.ReadLaunchOptions();
            Match m = GcBufferRegex.Match(options ?? "");
            if (!m.Success || !int.TryParse(m.Groups["val"].Value, out int current))
                return TweakStatus.Bad;

            int recommended = CalculateGoodGcBuffer();
            return current >= recommended ? TweakStatus.Good : TweakStatus.Bad;
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return TweakStatus.Bad;
        }
    }

    public void Apply(bool good)
    {
        string cfgPath = SteamAndRustService.GetConfigPathToLastUser();
        string current = SteamAndRustService.ReadLaunchOptions(cfgPath) ?? "";
        string cleaned = GcBufferRegex.Replace(current, "").Trim();
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ");

        if (good)
        {
            int val = CalculateGoodGcBuffer();
            cleaned = string.IsNullOrWhiteSpace(cleaned)
                ? $"-gc.buffer {val}"
                : $"{cleaned} -gc.buffer {val}";
        }

        SteamAndRustService.WriteLaunchOptions(cleaned, cfgPath);
    }

    public object CaptureState()
    {
        string cfg = SteamAndRustService.GetConfigPathToLastUser();
        return new GcBufferSnapshot
        {
            ConfigPath = cfg,
            LaunchOptions = SteamAndRustService.ReadLaunchOptions(cfg)
        };
    }

    public void RestoreState(JsonElement state)
    {
        var snap = TweakBackup.Read<GcBufferSnapshot>(state);
        if (snap == null) return;

        string currentCfg = SteamAndRustService.GetConfigPathToLastUser();
        SteamAndRustService.EnsureSameConfig(snap.ConfigPath, currentCfg);
        SteamAndRustService.WriteLaunchOptions(snap.LaunchOptions ?? "", currentCfg);
    }

    public static int CalculateGoodGcBuffer()
    {
        int ramGb = PagefileOptimization.GetTotalRamGb();
        int physicalCores = GetPhysicalCoreCount();

        int baseVal = ramGb >= 32 ? 4096
            : ramGb >= 24 ? 3072
            : ramGb >= 16 ? 2048
            : ramGb >= 12 ? 1024
            : 512;

        float mult = physicalCores >= 8 ? 1.15f
            : physicalCores > 4 ? 1.0f
            : 0.75f;

        int result = Math.Max(512, (int)Math.Round(baseVal * mult));
        return Math.Min(4096, result);
    }

    private static int GetPhysicalCoreCount()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT NumberOfCores FROM Win32_Processor");
            int total = 0;
            foreach (ManagementObject obj in searcher.Get())
            {
                if (int.TryParse(obj["NumberOfCores"]?.ToString(), out int cores))
                    total += cores;
            }
            if (total > 0) return total;
        }
        catch { }
        return Math.Max(2, Environment.ProcessorCount / 2);
    }
}
