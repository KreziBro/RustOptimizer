using System.Text.Json;
using Microsoft.Win32;
using Mixaill.HwInfo.D3DKMT;
using RustOptimizer.Core;

namespace RustOptimizer.Tweaks;

/// <summary>Hardware-accelerated GPU scheduling (HAGS): HwSchMode 1 = off (good), 2 = on (stock).</summary>
public sealed class DisableHags : ITweak
{
    private const string RegistryPath = @"System\CurrentControlSet\Control\GraphicsDrivers";
    private const string KeyName = "HwSchMode";
    private const int GoodValue = 1;
    private const int BadValue = 2;

    public DisableHags() { }

    public string Id => "DisableHags";
    public string Title => "Disable HAGS";
    public string Description => "Turns off Hardware-accelerated GPU Scheduling (HwSchMode). Requires a computer restart.";
    public bool NeedComputerRestart => true;
    public bool NeedSteamRestart => false;

    public void Apply(bool good)
    {
        try
        {
            using RegistryKey key = Registry.LocalMachine.OpenSubKey(RegistryPath, true);
            if (key != null)
                key.SetValue(KeyName, good ? GoodValue : BadValue, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to change HAGS registry value");
            Logger.Log(ex);
        }
    }

    public object CaptureState() =>
        RegistryValueSnapshot.Capture(RegistryHive.LocalMachine, RegistryPath, KeyName);

    public void RestoreState(JsonElement state) =>
        TweakBackup.Read<RegistryValueSnapshot>(state).Restore();

    public TweakStatus GetStatus()
    {
        var adapters = new List<KmtAdapter>();
        try
        {
            adapters = new Kmt().GetAdapters();
            // HAGS enabled on any adapter -> stock state (Bad); supported but off -> Good.
            if (adapters.Any(a => a.WddmCapabilities_27.HagsEnabled))
                return TweakStatus.Bad;
            if (adapters.Any(a => a.WddmCapabilities_27.HagsSupported))
                return TweakStatus.Good;
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to read HAGS status");
            Logger.Log(ex);
        }
        finally
        {
            foreach (var a in adapters) a.Dispose();
        }
        return TweakStatus.Unsupported;
    }
}
