using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using RustOptimizer.Core;
using RustOptimizer.Rust;

namespace RustOptimizer.Tweaks;

/// <summary>
/// Removes '-window-mode exclusive' from Rust Steam Launch Options so DXGI Flip Model works properly without freezes on Alt+Tab.
/// </summary>
public sealed class DisableExclusiveFullscreen : ITweak
{
    private static readonly Regex ExclusiveRegex = new(@"-window-mode\s+(?:\\?""exclusive\\?""|exclusive)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Id => "DisableExclusiveFullscreen";
    public string Title => "Отключение устаревшего эксклюзивного полноэкранного режима";
    public string Description => "Удаляет параметр -window-mode exclusive из параметров запуска Rust (в DX11 Flip Model он вызывает фризы при Alt+Tab).";
    public bool NeedComputerRestart => false;
    public bool NeedSteamRestart => true;

    public sealed class ExclusiveFullscreenSnapshot
    {
        public string ConfigPath { get; set; }
        public string LaunchOptions { get; set; }
    }

    public TweakStatus GetStatus()
    {
        try
        {
            string options = SteamAndRustService.ReadLaunchOptions() ?? "";
            return ExclusiveRegex.IsMatch(options) ? TweakStatus.Bad : TweakStatus.Good;
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return TweakStatus.Good;
        }
    }

    public void Apply(bool good)
    {
        string cfg = SteamAndRustService.GetConfigPathToLastUser();
        string current = SteamAndRustService.ReadLaunchOptions(cfg) ?? "";
        string cleaned = ExclusiveRegex.Replace(current, "").Trim();
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ");

        if (!good)
        {
            cleaned = string.IsNullOrWhiteSpace(cleaned)
                ? "-window-mode exclusive"
                : $"{cleaned} -window-mode exclusive";
        }

        SteamAndRustService.WriteLaunchOptions(cleaned, cfg);
    }

    public object CaptureState()
    {
        string cfg = SteamAndRustService.GetConfigPathToLastUser();
        return new ExclusiveFullscreenSnapshot
        {
            ConfigPath = cfg,
            LaunchOptions = SteamAndRustService.ReadLaunchOptions(cfg)
        };
    }

    public void RestoreState(JsonElement state)
    {
        var snap = TweakBackup.Read<ExclusiveFullscreenSnapshot>(state);
        if (snap == null) return;

        string currentCfg = SteamAndRustService.GetConfigPathToLastUser();
        SteamAndRustService.EnsureSameConfig(snap.ConfigPath, currentCfg);
        SteamAndRustService.WriteLaunchOptions(snap.LaunchOptions ?? "", currentCfg);
    }
}
