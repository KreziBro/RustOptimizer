namespace RustOptimizer.Tweaks;

/// <summary>Windows Defender state used by <see cref="DisableDRTP"/>.</summary>
public class DisableDRTPInfo
{
    public bool IsTamperProtected { get; set; }
    public bool RealTimeProtectionEnabled { get; set; }
}
