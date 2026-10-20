using System;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using RustOptimizer.Core;

namespace RustOptimizer.Tweaks;

/// <summary>
/// Disables Xbox Game Bar / GameDVR overlay and background recording.
/// </summary>
public sealed class DisableXboxGameBarOptimization : ITweak
{
    private const string GameDvrRegPath = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string GameConfigStoreRegPath = @"System\GameConfigStore";

    public string Id => "DisableXboxGameBar";
    public string Title => "Отключение Xbox Game Bar и фоновой записи (GameDVR)";
    public string Description => "Отключает оверлей Xbox Game Bar и фоновый захват видео GameDVR, снижая инпут-лаг и статтеры.";
    public bool NeedComputerRestart => false;
    public bool NeedSteamRestart => false;

    public sealed class XboxGameBarBackup
    {
        public bool PackageWasInstalled { get; set; }
        public RegistryValueSnapshot AppCaptureEnabled { get; set; }
        public RegistryValueSnapshot GameDvrEnabled { get; set; }
    }

    public TweakStatus GetStatus()
    {
        try
        {
            using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var dvrKey = hkcu.OpenSubKey(GameDvrRegPath);
            using var storeKey = hkcu.OpenSubKey(GameConfigStoreRegPath);

            int appCapture = Convert.ToInt32(dvrKey?.GetValue("AppCaptureEnabled", 1) ?? 1);
            int gameDvr = Convert.ToInt32(storeKey?.GetValue("GameDVR_Enabled", 1) ?? 1);

            bool regDisabled = appCapture == 0 && gameDvr == 0;
            bool pkgInstalled = IsPackageInstalled();

            return (!pkgInstalled && regDisabled) ? TweakStatus.Good : TweakStatus.Bad;
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return TweakStatus.Bad;
        }
    }

    public void Apply(bool good)
    {
        using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var dvrKey = hkcu.CreateSubKey(GameDvrRegPath, true);
        using var storeKey = hkcu.CreateSubKey(GameConfigStoreRegPath, true);

        if (good)
        {
            dvrKey?.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);
            storeKey?.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);

            if (IsPackageInstalled())
            {
                PowershellTools.powershellExecute("Get-AppxPackage Microsoft.XboxGamingOverlay | Remove-AppxPackage -ErrorAction SilentlyContinue");
                if (IsPackageInstalled())
                    RunWinget("uninstall 9nzkpstsnw4p --silent --accept-source-agreements");
            }
        }
        else
        {
            dvrKey?.SetValue("AppCaptureEnabled", 1, RegistryValueKind.DWord);
            storeKey?.SetValue("GameDVR_Enabled", 1, RegistryValueKind.DWord);

            if (!IsPackageInstalled())
                RunWinget("install 9NZKPSTSNW4P --source msstore --accept-source-agreements --accept-package-agreements");
        }
    }

    public object CaptureState() => new XboxGameBarBackup
    {
        PackageWasInstalled = IsPackageInstalled(),
        AppCaptureEnabled = RegistryValueSnapshot.Capture(RegistryHive.CurrentUser, GameDvrRegPath, "AppCaptureEnabled"),
        GameDvrEnabled = RegistryValueSnapshot.Capture(RegistryHive.CurrentUser, GameConfigStoreRegPath, "GameDVR_Enabled")
    };

    public void RestoreState(JsonElement state)
    {
        var snap = TweakBackup.Read<XboxGameBarBackup>(state);
        if (snap == null) return;

        snap.AppCaptureEnabled?.Restore();
        snap.GameDvrEnabled?.Restore();

        if (snap.PackageWasInstalled && !IsPackageInstalled())
            RunWinget("install 9NZKPSTSNW4P --source msstore --accept-source-agreements --accept-package-agreements");
    }

    public static bool IsPackageInstalled()
    {
        string output = PowershellTools.powershellExecute("Get-AppxPackage Microsoft.XboxGamingOverlay");
        return !string.IsNullOrWhiteSpace(output);
    }

    private static void RunWinget(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("winget", arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(30000);
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
        }
    }
}
