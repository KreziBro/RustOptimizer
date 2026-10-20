using System.Text.Json;
using Microsoft.Win32;
using RustOptimizer.Core;

namespace RustOptimizer.Tweaks;

/// <summary>Hypervisor-enforced code integrity (Memory Integrity): Enabled 0 = off (good).</summary>
public sealed class DisableHvci : ITweak
{
    private const string Path = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
    private const string ValueName = "Enabled";

    public DisableHvci() { }

    public string Id => "DisableHvci";
    public string Title => "Disable HVCI";
    public string Description => "Turns off Hypervisor-enforced Code Integrity (Memory Integrity). Requires a computer restart.";
    public bool NeedComputerRestart => true;
    public bool NeedSteamRestart => false;

    public void Apply(bool good)
    {
        try
        {
            using RegistryKey key = Registry.LocalMachine.CreateSubKey(Path, true);
            if (key == null)
                Logger.Log("HVCI registry key could not be created");
            else
                key.SetValue(ValueName, good ? 0 : 1, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to change HVCI registry value");
            Logger.Log(ex);
        }
    }

    public object CaptureState() =>
        RegistryValueSnapshot.Capture(RegistryHive.LocalMachine, Path, ValueName);

    public void RestoreState(JsonElement state) =>
        TweakBackup.Read<RegistryValueSnapshot>(state).Restore();

    public TweakStatus GetStatus()
    {
        using RegistryKey key = Registry.LocalMachine.OpenSubKey(Path, false);
        if (key == null)
        {
            Logger.Log("HVCI registry key was not found");
            return TweakStatus.Middle;
        }
        object raw = key.GetValue(ValueName);
        int num = raw != null ? Convert.ToInt32(raw) : 0;
        Logger.Log($"HVCI enabled raw value: {num}");
        return num > 0 ? TweakStatus.Bad : TweakStatus.Good;
    }
}
