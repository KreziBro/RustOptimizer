using System.Text.Json;
using RustOptimizer.Core;

namespace RustOptimizer.Tweaks;

/// <summary>Disables Windows Defender real-time protection (impossible while Tamper Protection is on).</summary>
public sealed class DisableDRTP : ITweak
{
    public class RealtimeProtectStatusDto
    {
        public bool RealTimeProtectionEnabled { get; set; }
    }

    private class TamperStatusDto
    {
        public bool IsTamperProtected { get; set; }
    }

    public DisableDRTP() { }

    public string Id => "DisableDRTP";
    public string Title => "Disable Defender real-time protection";
    public string Description => "Turns off Windows Defender real-time monitoring (Set-MpPreference). Not possible with Tamper Protection enabled.";
    public bool NeedComputerRestart => true;
    public bool NeedSteamRestart => false;

    public void Apply(bool good)
    {
        try
        {
            DisableDRTPInfo info = GetDisableDRTPInfo();
            if (info != null && !info.IsTamperProtected)
            {
                PowershellTools.powershellExecute(good
                    ? "Set-MpPreference -DisableRealtimeMonitoring $true"
                    : "Set-MpPreference -DisableRealtimeMonitoring $false");
            }
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
        }
    }

    public object CaptureState()
    {
        if (!PowershellTools.HaveDefender)
            return null;
        return GetDisableDRTPInfo() ?? throw new Exception("Could not read Defender real-time protection state.");
    }

    public void RestoreState(JsonElement state)
    {
        if (state.ValueKind == JsonValueKind.Null) return; // no Defender when captured
        DisableDRTPInfo saved = TweakBackup.Read<DisableDRTPInfo>(state);
        DisableDRTPInfo current = GetDisableDRTPInfo() ?? throw new Exception("Could not read Defender real-time protection state.");
        if (current.RealTimeProtectionEnabled == saved.RealTimeProtectionEnabled)
            return;
        if (current.IsTamperProtected)
            throw new Exception("Tamper Protection is enabled, so Defender real-time protection cannot be restored. Disable it in Windows Security and retry the rollback.");

        PowershellTools.powershellExecute("Set-MpPreference -DisableRealtimeMonitoring $" + (saved.RealTimeProtectionEnabled ? "false" : "true"));
        DisableDRTPInfo after = GetDisableDRTPInfo();
        if (after?.RealTimeProtectionEnabled == saved.RealTimeProtectionEnabled)
            return;
        throw new Exception($"Could not restore real-time protection: expected {saved.RealTimeProtectionEnabled}, got {(after == null ? "unreadable" : after.RealTimeProtectionEnabled.ToString())}.");
    }

    public TweakStatus GetStatus()
    {
        DisableDRTPInfo info = GetDisableDRTPInfo();
        if (info == null || !info.IsTamperProtected)
        {
            if (info != null && info.RealTimeProtectionEnabled) return TweakStatus.Bad;
            if (info != null && !info.RealTimeProtectionEnabled) return TweakStatus.Good;
        }
        return TweakStatus.Unsupported;
    }

    public static DisableDRTPInfo GetDisableDRTPInfo()
    {
        try
        {
            string tamperJson = PowershellTools.powershellExecute("Get-MpComputerStatus | Select-Object IsTamperProtected | ConvertTo-Json");
            Logger.Log("RAW JSON: " + tamperJson);
            var tamper = JsonSerializer.Deserialize<TamperStatusDto>(tamperJson);

            string realtimeJson = PowershellTools.powershellExecute("Get-MpComputerStatus | Select-Object RealTimeProtectionEnabled | ConvertTo-Json");
            Logger.Log("RAW JSON: " + realtimeJson);
            var realtime = JsonSerializer.Deserialize<RealtimeProtectStatusDto>(realtimeJson);

            return new DisableDRTPInfo
            {
                IsTamperProtected = tamper.IsTamperProtected,
                RealTimeProtectionEnabled = realtime.RealTimeProtectionEnabled
            };
        }
        catch (Exception ex)
        {
            Logger.Log(ex.Message);
            Logger.Log(ex.StackTrace ?? "");
        }
        return null;
    }
}
