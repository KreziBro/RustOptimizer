using System.Text.Json;
using Microsoft.Win32;
using RustOptimizer.Core;

namespace RustOptimizer.Tweaks;

/// <summary>Windows Game Mode (HKCU\Software\Microsoft\GameBar, string values "1"/"0").</summary>
public sealed class EnableGameModeOptimization : ITweak
{
    private const string RegistryPath = @"Software\Microsoft\GameBar";
    private const string ValueName1 = "AllowAutoGameMode";
    private const string ValueName2 = "AutoGameModeEnabled";

    public EnableGameModeOptimization() { }

    public string Id => "EnableGameMode";
    public string Title => "Enable Game Mode";
    public string Description => "Enables Windows Game Mode (AllowAutoGameMode / AutoGameModeEnabled).";
    public bool NeedComputerRestart => false;
    public bool NeedSteamRestart => false;

    public void Apply(bool good)
    {
        Logger.Log($"Requested Game Mode status change to {(good ? "Good" : "Bad")}");
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, true))
        {
            try
            {
                if (key != null)
                {
                    // Only touch the values if at least one of them already exists.
                    object v1 = key.GetValue(ValueName1);
                    object v2 = key.GetValue(ValueName2);
                    if (v1 != null || v2 != null)
                    {
                        string value = good ? "1" : "0";
                        key.SetValue(ValueName1, value, RegistryValueKind.String);
                        key.SetValue(ValueName2, value, RegistryValueKind.String);
                        Logger.Log(good
                            ? "Game Mode registry values were set to enabled"
                            : "Game Mode registry values were set to disabled");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to set Game Mode registry values");
                Logger.Log(ex);
            }
        }
        Logger.Log($"Game Mode effective status is {GetStatus()}");
    }

    public object CaptureState() => new RegistryValueSnapshot[]
    {
        RegistryValueSnapshot.Capture(RegistryHive.CurrentUser, RegistryPath, ValueName1),
        RegistryValueSnapshot.Capture(RegistryHive.CurrentUser, RegistryPath, ValueName2)
    };

    public void RestoreState(JsonElement state)
    {
        foreach (var snapshot in TweakBackup.Read<RegistryValueSnapshot[]>(state))
            snapshot.Restore();
    }

    public TweakStatus GetStatus()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            if (key != null)
            {
                string v1 = key.GetValue(ValueName1)?.ToString();
                string v2 = key.GetValue(ValueName2)?.ToString();
                if (v1 != null || v2 != null)
                {
                    if (string.Equals(v1, "0", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(v2, "0", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Log("Game Mode is disabled based on registry values");
                        return TweakStatus.Bad;
                    }
                    if (string.Equals(v1, "1", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(v2, "1", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Log("Game Mode is enabled based on registry values");
                        return TweakStatus.Good;
                    }
                    Logger.Log("Registry values indicate mixed Game Mode state");
                    return TweakStatus.Middle;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to read Game Mode registry values");
            Logger.Log(ex);
        }
        Logger.Log("Game Mode status fell back to unsupported");
        return TweakStatus.Unsupported;
    }
}
