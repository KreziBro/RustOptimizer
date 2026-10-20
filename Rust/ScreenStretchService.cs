using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using RustOptimizer.Core;

namespace RustOptimizer.Rust;

public sealed record ResolutionOption(int Width, int Height)
{
    public string Label => $"{Width}x{Height}";
    public override string ToString() => Label;
}

public sealed class ScreenStretchStoredSettings
{
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }
    public int TargetWidth { get; set; }
    public int TargetHeight { get; set; }
    public int OriginalWidth { get; set; }
    public int OriginalHeight { get; set; }
    public int OriginalFrequency { get; set; }
    public bool OwnsLaunchParams { get; set; }
    public string PreviousResolutionArg { get; set; }
}

/// <summary>
/// Port of ScreenStretch service: switches monitor resolution via ChangeDisplaySettings
/// and updates '-graphics.resolution <index>' in Rust Steam LaunchOptions.
/// </summary>
public static class ScreenStretchService
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int CDS_UPDATEREGISTRY = 1;
    private const int CDS_TEST = 2;
    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const int DISP_CHANGE_RESTART = 1;
    private const int DM_PELSWIDTH = 0x80000;
    private const int DM_PELSHEIGHT = 0x100000;
    private const int DM_DISPLAYFREQUENCY = 0x400000;

    private static string SettingsPath => Path.Combine(AppPaths.Root, "screen-stretch.json");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);

    public static (int width, int height, int frequency) GetCurrentDisplayMode()
    {
        var dm = NewDevMode();
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
            return (1920, 1080, 60);
        return (dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
    }

    public static List<ResolutionOption> GetSupportedResolutions()
    {
        var seen = new HashSet<(int, int)>();
        var list = new List<ResolutionOption>();
        var dm = NewDevMode();
        for (int i = 0; EnumDisplaySettings(null, i, ref dm); i++)
        {
            if (dm.dmPelsWidth >= 800 && dm.dmPelsHeight >= 600)
                list.Add(new ResolutionOption(dm.dmPelsWidth, dm.dmPelsHeight));
        }

        return list
            .OrderByDescending(r => r.Width)
            .ThenByDescending(r => r.Height)
            .Where(r => seen.Add((r.Width, r.Height)))
            .ToList();
    }

    public static ScreenStretchStoredSettings LoadSettings()
    {
        ScreenStretchStoredSettings s = null;
        try
        {
            if (File.Exists(SettingsPath))
                s = JsonSerializer.Deserialize<ScreenStretchStoredSettings>(File.ReadAllText(SettingsPath));
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
        }

        s ??= new ScreenStretchStoredSettings();
        var (curW, curH, _) = GetCurrentDisplayMode();
        if (s.SourceWidth <= 0 || s.SourceHeight <= 0)
        {
            s.SourceWidth = curW;
            s.SourceHeight = curH;
        }
        if (s.TargetWidth <= 0 || s.TargetHeight <= 0)
        {
            var resolutions = GetSupportedResolutions();
            var stretchCandidate = resolutions.FirstOrDefault(r => r.Width == 1440 && r.Height == 1080)
                ?? resolutions.FirstOrDefault(r => r.Width < curW)
                ?? new ResolutionOption(curW, curH);
            s.TargetWidth = stretchCandidate.Width;
            s.TargetHeight = stretchCandidate.Height;
        }
        return s;
    }

    public static void SaveSettings(ScreenStretchStoredSettings settings)
    {
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool IsStretchActive()
    {
        var s = LoadSettings();
        var (curW, curH, _) = GetCurrentDisplayMode();
        return s.TargetWidth > 0 && s.TargetHeight > 0 &&
               (s.TargetWidth != s.SourceWidth || s.TargetHeight != s.SourceHeight) &&
               curW == s.TargetWidth && curH == s.TargetHeight;
    }

    public static string EnableStretch(int sourceW, int sourceH, int targetW, int targetH)
    {
        var s = LoadSettings();
        s.SourceWidth = sourceW;
        s.SourceHeight = sourceH;
        s.TargetWidth = targetW;
        s.TargetHeight = targetH;

        var resolutions = GetSupportedResolutions();
        int idx = resolutions.FindIndex(r => r.Width == targetW && r.Height == targetH);
        if (idx < 0)
            return $"Разрешение {targetW}x{targetH} не найдено в списке режимов монитора.";

        if (!CanApplyMode(targetW, targetH))
            return $"Монитор отклонил разрешение {targetW}x{targetH}.";

        var (curW, curH, curFreq) = GetCurrentDisplayMode();
        if (s.OriginalWidth <= 0 || s.OriginalHeight <= 0)
        {
            s.OriginalWidth = curW;
            s.OriginalHeight = curH;
            s.OriginalFrequency = curFreq;
        }

        string status = ApplyDisplayMode(targetW, targetH, 0);
        if (status != "ALLGOOD" && status != "RESTART_REQUIRED")
            return $"Не удалось переключить разрешение экрана: {status}";

        try
        {
            string currentLo = SteamAndRustService.ReadLaunchOptions();
            var kvs = RustGameTweaksAndGraphics.ParseLaunchParamsToKeyValues(currentLo);
            if (!s.OwnsLaunchParams)
            {
                var prev = kvs.FirstOrDefault(x => string.Equals(x.Key, "graphics.resolution", StringComparison.OrdinalIgnoreCase));
                s.PreviousResolutionArg = string.IsNullOrEmpty(prev.Key) ? null : prev.Value;
                s.OwnsLaunchParams = true;
            }
            kvs.RemoveAll(x => string.Equals(x.Key, "graphics.resolution", StringComparison.OrdinalIgnoreCase));
            kvs.Add(("graphics.resolution", idx.ToString()));
            SteamAndRustService.WriteLaunchOptions(RustGameTweaksAndGraphics.BuildLaunchParamsFromKeyValues(kvs));
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
        }

        SaveSettings(s);
        return $"Растяг экрана активирован: {targetW}x{targetH} (индекс разрешения в Rust: {idx}).";
    }

    public static string DisableStretch()
    {
        var s = LoadSettings();
        int restoreW = s.SourceWidth > 0 ? s.SourceWidth : s.OriginalWidth;
        int restoreH = s.SourceHeight > 0 ? s.SourceHeight : s.OriginalHeight;

        if (restoreW > 0 && restoreH > 0 && CanApplyMode(restoreW, restoreH))
            ApplyDisplayMode(restoreW, restoreH, s.OriginalFrequency);

        if (s.OwnsLaunchParams)
        {
            try
            {
                string currentLo = SteamAndRustService.ReadLaunchOptions();
                var kvs = RustGameTweaksAndGraphics.ParseLaunchParamsToKeyValues(currentLo);
                kvs.RemoveAll(x => string.Equals(x.Key, "graphics.resolution", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(s.PreviousResolutionArg))
                    kvs.Add(("graphics.resolution", s.PreviousResolutionArg));
                SteamAndRustService.WriteLaunchOptions(RustGameTweaksAndGraphics.BuildLaunchParamsFromKeyValues(kvs));
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        }

        s.OriginalWidth = 0;
        s.OriginalHeight = 0;
        s.OriginalFrequency = 0;
        s.OwnsLaunchParams = false;
        s.PreviousResolutionArg = null;
        SaveSettings(s);

        return $"Возвращено исходное разрешение {restoreW}x{restoreH}.";
    }

    private static bool CanApplyMode(int width, int height)
    {
        var dm = NewDevMode();
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
            return false;
        dm.dmPelsWidth = width;
        dm.dmPelsHeight = height;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;
        int res = ChangeDisplaySettings(ref dm, CDS_TEST);
        return res == DISP_CHANGE_SUCCESSFUL || res == DISP_CHANGE_RESTART;
    }

    private static string ApplyDisplayMode(int width, int height, int frequency)
    {
        var dm = NewDevMode();
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
            return "FAILED";

        dm.dmPelsWidth = width;
        dm.dmPelsHeight = height;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;
        if (frequency > 0)
        {
            dm.dmDisplayFrequency = frequency;
            dm.dmFields |= DM_DISPLAYFREQUENCY;
        }

        int test = ChangeDisplaySettings(ref dm, CDS_TEST);
        if (test != DISP_CHANGE_SUCCESSFUL && test != DISP_CHANGE_RESTART && frequency > 0)
        {
            dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;
            test = ChangeDisplaySettings(ref dm, CDS_TEST);
        }
        if (test != DISP_CHANGE_SUCCESSFUL && test != DISP_CHANGE_RESTART)
            return "UNSUPPORTED";

        int code = ChangeDisplaySettings(ref dm, CDS_UPDATEREGISTRY);
        return code switch
        {
            DISP_CHANGE_SUCCESSFUL => "ALLGOOD",
            DISP_CHANGE_RESTART => "RESTART_REQUIRED",
            _ => "FAILED"
        };
    }

    private static DEVMODE NewDevMode() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
}
