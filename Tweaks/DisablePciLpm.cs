using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using RustOptimizer.Core;

namespace RustOptimizer.Tweaks;

public enum ASPMValue
{
    Off = 0,
    Moderate = 1,
    Maximum = 2
}

/// <summary>
/// Disables PCI Express Link State Power Management (ASPM) on AC and DC power.
/// </summary>
public sealed class DisablePciLpm : ITweak
{
    private static readonly Guid PciExpressSubgroupGuid = Guid.Parse("501a4d13-42af-4429-9fd1-a8218c268e20");
    private static readonly Guid AspmSettingGuid = Guid.Parse("ee12f906-d277-404b-b6da-e5fa1a576df5");

    public string Id => "DisablePcieLpm";
    public string Title => "Отключение PCIe Link State Power Management (ASPM)";
    public string Description => "Отключает энергосбережение шины PCI Express (ASPM), снижая задержки видеокарты и NVMe.";
    public bool NeedComputerRestart => false;
    public bool NeedSteamRestart => false;

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(nint UserRootPowerKey, out nint ActivePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(nint RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, out uint AcValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(nint RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, out uint DcValueIndex);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint hMem);

    public sealed class PciLpmState
    {
        public ASPMValue Ac { get; set; }
        public ASPMValue Dc { get; set; }
    }

    public TweakStatus GetStatus()
    {
        try
        {
            var (ac, dc) = GetAspmStatus();
            return (ac == ASPMValue.Off && dc == ASPMValue.Off) ? TweakStatus.Good : TweakStatus.Bad;
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return TweakStatus.Bad;
        }
    }

    public void Apply(bool good)
    {
        ASPMValue target = good ? ASPMValue.Off : ASPMValue.Moderate;
        SetAspm(target, target);
    }

    public object CaptureState()
    {
        var (ac, dc) = GetAspmStatus();
        return new PciLpmState { Ac = ac, Dc = dc };
    }

    public void RestoreState(JsonElement state)
    {
        var snap = TweakBackup.Read<PciLpmState>(state);
        if (snap != null)
            SetAspm(snap.Ac, snap.Dc);
    }

    public static (ASPMValue ac, ASPMValue dc) GetAspmStatus()
    {
        uint err = PowerGetActiveScheme(nint.Zero, out nint activeGuidPtr);
        if (err != 0 || activeGuidPtr == nint.Zero)
            throw new InvalidOperationException($"PowerGetActiveScheme failed with error {err}");

        try
        {
            Guid schemeGuid = Marshal.PtrToStructure<Guid>(activeGuidPtr);
            Guid subgroup = PciExpressSubgroupGuid;
            Guid setting = AspmSettingGuid;

            uint errAc = PowerReadACValueIndex(nint.Zero, ref schemeGuid, ref subgroup, ref setting, out uint acVal);
            uint errDc = PowerReadDCValueIndex(nint.Zero, ref schemeGuid, ref subgroup, ref setting, out uint dcVal);
            if (errAc != 0 || errDc != 0)
                throw new InvalidOperationException($"PowerReadValueIndex failed: AC={errAc}, DC={errDc}");

            return ((ASPMValue)acVal, (ASPMValue)dcVal);
        }
        finally
        {
            LocalFree(activeGuidPtr);
        }
    }

    private static void SetAspm(ASPMValue ac, ASPMValue dc)
    {
        PowershellTools.powershellExecute($"powercfg /setacvalueindex SCHEME_CURRENT SUB_PCIEXPRESS ASPM {(int)ac}");
        PowershellTools.powershellExecute($"powercfg /setdcvalueindex SCHEME_CURRENT SUB_PCIEXPRESS ASPM {(int)dc}");
        PowershellTools.powershellExecute("powercfg /setactive SCHEME_CURRENT");
    }
}
