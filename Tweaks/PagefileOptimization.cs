using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.Json;
using RustOptimizer.Core;

namespace RustOptimizer.Tweaks;

public sealed class PagefileDto
{
    public bool IsAutoManaged { get; set; }
    public uint InitialSizeMb { get; set; }
    public uint MaximumSizeMb { get; set; }
    public uint CurrentAllocatedMb { get; set; }
}

/// <summary>
/// Configures the Windows pagefile size on C:\ based on installed RAM and GPU VRAM.
/// </summary>
public sealed class PagefileOptimization : ITweak
{
    public string Id => "AutoPagefile";
    public string Title => "Оптимизация файла подкачки (Pagefile)";
    public string Description => "Рассчитывает и фиксирует оптимальный размер файла подкачки для предотвращения вылетов Rust из-за нехватки памяти.";
    public bool NeedComputerRestart => true;
    public bool NeedSteamRestart => false;

    public TweakStatus GetStatus()
    {
        try
        {
            var info = GetPagefileInfo();
            int desiredMb = CalculateDesiredSizeMb();
            uint effectiveMb = info.IsAutoManaged ? info.CurrentAllocatedMb : Math.Max(info.InitialSizeMb, info.MaximumSizeMb);
            return effectiveMb >= desiredMb ? TweakStatus.Good : TweakStatus.Bad;
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
        {
            var info = GetPagefileInfo();
            int desiredMb = CalculateDesiredSizeMb();
            uint currentMb = info.IsAutoManaged ? info.CurrentAllocatedMb : info.MaximumSizeMb;
            if (currentMb >= desiredMb)
                return;

            var drive = new DriveInfo("C");
            long neededExtraBytes = (long)(desiredMb - currentMb) * 1024L * 1024L;
            long remainingFree = drive.AvailableFreeSpace - neededExtraBytes;
            if ((double)remainingFree / drive.TotalSize < 0.10)
                throw new InvalidOperationException("На диске C: останется менее 10% свободного места после увеличения файла подкачки.");

            SetPagefileSize((uint)desiredMb, (uint)desiredMb);
        }
        else
        {
            EnableAutoPagefile();
        }
    }

    public object CaptureState() => GetPagefileInfo();

    public void RestoreState(JsonElement state)
    {
        var snap = TweakBackup.Read<PagefileDto>(state);
        if (snap == null) return;

        if (snap.IsAutoManaged)
            EnableAutoPagefile();
        else if (snap.MaximumSizeMb > 0)
            SetPagefileSize(snap.InitialSizeMb, snap.MaximumSizeMb);
    }

    public static int CalculateDesiredSizeMb()
    {
        int ramGb = GetTotalRamGb();
        int vramGb = GetTotalVramGb();
        int desiredGb = Math.Max(8, 40 - (ramGb + vramGb));
        return desiredGb * 1024;
    }

    public static int GetTotalRamGb()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (ulong.TryParse(obj["TotalVisibleMemorySize"]?.ToString(), out ulong kb))
                    return (int)Math.Round(kb / 1024.0 / 1024.0);
            }
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
        }
        return 16;
    }

    public static int GetTotalVramGb()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT AdapterRAM FROM Win32_VideoController");
            uint maxBytes = 0;
            foreach (ManagementObject obj in searcher.Get())
            {
                if (uint.TryParse(obj["AdapterRAM"]?.ToString(), out uint bytes) && bytes > maxBytes)
                    maxBytes = bytes;
            }
            if (maxBytes > 0)
                return Math.Max(2, (int)Math.Round(maxBytes / 1024.0 / 1024.0 / 1024.0));
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
        }
        return 8;
    }

    public static PagefileDto GetPagefileInfo()
    {
        var dto = new PagefileDto();
        using (var csSearcher = new ManagementObjectSearcher("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem"))
        {
            foreach (ManagementObject obj in csSearcher.Get())
            {
                if (obj["AutomaticManagedPagefile"] is bool auto)
                    dto.IsAutoManaged = auto;
            }
        }

        using (var pfSearcher = new ManagementObjectSearcher("SELECT Name, InitialSize, MaximumSize FROM Win32_PageFileSetting"))
        {
            foreach (ManagementObject obj in pfSearcher.Get())
            {
                string name = obj["Name"]?.ToString() ?? "";
                if (name.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
                {
                    dto.InitialSizeMb = Convert.ToUInt32(obj["InitialSize"] ?? 0);
                    dto.MaximumSizeMb = Convert.ToUInt32(obj["MaximumSize"] ?? 0);
                }
            }
        }

        using (var usageSearcher = new ManagementObjectSearcher("SELECT Name, AllocatedBaseSize FROM Win32_PageFileUsage"))
        {
            foreach (ManagementObject obj in usageSearcher.Get())
            {
                string name = obj["Name"]?.ToString() ?? "";
                if (name.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
                    dto.CurrentAllocatedMb = Convert.ToUInt32(obj["AllocatedBaseSize"] ?? 0);
            }
        }

        return dto;
    }

    private static void SetPagefileSize(uint initialMb, uint maxMb)
    {
        using (var csSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem"))
        {
            foreach (ManagementObject cs in csSearcher.Get())
            {
                cs["AutomaticManagedPagefile"] = false;
                cs.Put();
            }
        }

        bool found = false;
        using (var pfSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PageFileSetting WHERE Name='C:\\\\pagefile.sys'"))
        {
            foreach (ManagementObject pf in pfSearcher.Get())
            {
                pf["InitialSize"] = initialMb;
                pf["MaximumSize"] = maxMb;
                pf.Put();
                found = true;
            }
        }

        if (!found)
        {
            using var pfClass = new ManagementClass("Win32_PageFileSetting");
            using var newPf = pfClass.CreateInstance();
            if (newPf != null)
            {
                newPf["Name"] = @"C:\pagefile.sys";
                newPf["InitialSize"] = initialMb;
                newPf["MaximumSize"] = maxMb;
                newPf.Put();
            }
        }
    }

    private static void EnableAutoPagefile()
    {
        using var csSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
        foreach (ManagementObject cs in csSearcher.Get())
        {
            cs["AutomaticManagedPagefile"] = true;
            cs.Put();
        }
    }
}
