using System;
using System.Text.Json;
using Microsoft.Win32;
using RustOptimizer.Core;

namespace RustOptimizer.Tweaks;

/// <summary>
/// Manages the active Windows Power Plan (Balanced scheme for proper AMD 3D V-Cache / Intel scheduling).
/// </summary>
public sealed class WindowsPowerPlan : ITweak
{
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string PowerSchemesRegPath = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";

    public string Id => "WindowsPowerPlan";
    public string Title => "Схема электропитания Windows (Сбалансированная)";
    public string Description => "Устанавливает сбалансированную схему питания для корректной парковки ядер (особенно на Ryzen X3D и гибридных Intel).";
    public bool NeedComputerRestart => false;
    public bool NeedSteamRestart => false;

    public TweakStatus GetStatus()
    {
        try
        {
            string active = GetActivePowerSchemeGuid();
            return string.Equals(active, BalancedGuid, StringComparison.OrdinalIgnoreCase)
                ? TweakStatus.Good
                : TweakStatus.Bad;
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return TweakStatus.Bad;
        }
    }

    public void Apply(bool good)
    {
        if (good)
            PowershellTools.powershellExecute("powercfg -setactive SCHEME_BALANCED");
        else
            PowershellTools.powershellExecute("powercfg -setactive SCHEME_MAX");
    }

    public object CaptureState() => GetActivePowerSchemeGuid() ?? BalancedGuid;

    public void RestoreState(JsonElement state)
    {
        string guid = state.GetString();
        if (!string.IsNullOrWhiteSpace(guid) && Guid.TryParse(guid, out _))
            PowershellTools.powershellExecute($"powercfg /setactive {guid}");
    }

    private static string GetActivePowerSchemeGuid()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hklm.OpenSubKey(PowerSchemesRegPath);
        string val = key?.GetValue("ActivePowerScheme") as string;
        if (!string.IsNullOrWhiteSpace(val))
            return val.Trim();

        return PowershellTools.powershellExecute("(Get-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Power\\User\\PowerSchemes').ActivePowerScheme").Trim();
    }
}
