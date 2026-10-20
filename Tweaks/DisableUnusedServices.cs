using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using RustOptimizer.Core;

namespace RustOptimizer.Tweaks;

public enum ServiceStartupType
{
    Automatic,
    AutomaticDelayedStart,
    Manual,
    Disabled,
    Boot,
    System,
    Unknown
}

public sealed class ServiceConfigItem
{
    public string Name { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ServiceStartupType StartupType { get; set; }
}

public sealed class ServiceInfoDto
{
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string State { get; set; }
    public string Status { get; set; }
    public ServiceStartupType StartupType { get; set; }
}

/// <summary>
/// Optimizes background Windows services according to Assets/ServicesConfigGood.json.
/// </summary>
public sealed class DisableUnusedServices : ITweak
{
    public string Id => "DisableUnusedServices";
    public string Title => "Оптимизация неиспользуемых служб Windows";
    public string Description => "Переводит второстепенные и телеметрические службы Windows в ручной или отключенный режим.";
    public bool NeedComputerRestart => true;
    public bool NeedSteamRestart => false;

    private static string GoodConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "ServicesConfigGood.json");
    private static string BadConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "ServicesConfigBad.json");

    public TweakStatus GetStatus()
    {
        try
        {
            if (!File.Exists(GoodConfigPath))
                return TweakStatus.Bad;

            var targetConfig = JsonSerializer.Deserialize<List<ServiceConfigItem>>(File.ReadAllText(GoodConfigPath));
            if (targetConfig == null || targetConfig.Count == 0)
                return TweakStatus.Bad;

            var installedServices = GetAllServicesInfo();
            int totalMatched = 0;
            int compliant = 0;

            foreach (var cfg in targetConfig)
            {
                var matches = MatchServices(installedServices, cfg.Name).ToList();
                if (matches.Count == 0)
                    continue;

                totalMatched += matches.Count;
                foreach (var svc in matches)
                {
                    if (svc.StartupType == cfg.StartupType)
                        compliant++;
                }
            }

            if (totalMatched == 0)
                return TweakStatus.Bad;

            double ratio = (double)compliant / totalMatched;
            return ratio >= 0.95 ? TweakStatus.Good : TweakStatus.Bad;
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return TweakStatus.Bad;
        }
    }

    public void Apply(bool good)
    {
        string path = good ? GoodConfigPath : BadConfigPath;
        if (!File.Exists(path))
            throw new FileNotFoundException("Services configuration file not found", path);

        var items = JsonSerializer.Deserialize<List<ServiceConfigItem>>(File.ReadAllText(path));
        if (items != null)
            ApplyConfiguration(items);
    }

    public object CaptureState()
    {
        var installed = GetAllServicesInfo();
        var targets = new List<ServiceConfigItem>();
        if (File.Exists(GoodConfigPath))
        {
            var goodCfg = JsonSerializer.Deserialize<List<ServiceConfigItem>>(File.ReadAllText(GoodConfigPath)) ?? new();
            foreach (var cfg in goodCfg)
            {
                foreach (var svc in MatchServices(installed, cfg.Name))
                {
                    if (svc.StartupType != ServiceStartupType.Unknown &&
                        svc.StartupType != ServiceStartupType.Boot &&
                        svc.StartupType != ServiceStartupType.System)
                    {
                        targets.Add(new ServiceConfigItem { Name = svc.Name, StartupType = svc.StartupType });
                    }
                }
            }
        }
        return targets;
    }

    public void RestoreState(JsonElement state)
    {
        var saved = TweakBackup.Read<List<ServiceConfigItem>>(state);
        if (saved != null && saved.Count > 0)
            ApplyConfiguration(saved);
    }

    private static void ApplyConfiguration(List<ServiceConfigItem> config)
    {
        var installed = GetAllServicesInfo();
        foreach (var item in config)
        {
            bool exists = MatchServices(installed, item.Name).Any();
            if (!exists)
                continue;

            string targetName = item.Name.EndsWith('*')
                ? item.Name.Substring(0, item.Name.Length - 1)
                : item.Name;

            ChangeServiceStartType(targetName, item.StartupType);
        }
    }

    private static IEnumerable<ServiceInfoDto> MatchServices(List<ServiceInfoDto> services, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return Enumerable.Empty<ServiceInfoDto>();

        if (pattern.EndsWith('*'))
        {
            string prefix = pattern.Substring(0, pattern.Length - 1);
            return services.Where(s => s.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
        return services.Where(s => s.Name.Equals(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static List<ServiceInfoDto> GetAllServicesInfo()
    {
        var result = new List<ServiceInfoDto>();
        using var searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, State, Status, StartMode FROM Win32_Service");
        using var collection = searcher.Get();
        foreach (ManagementObject obj in collection)
        {
            string name = obj["Name"]?.ToString() ?? "";
            string startMode = obj["StartMode"]?.ToString() ?? "Unknown";
            result.Add(new ServiceInfoDto
            {
                Name = name,
                DisplayName = obj["DisplayName"]?.ToString() ?? "",
                State = obj["State"]?.ToString() ?? "",
                Status = obj["Status"]?.ToString() ?? "",
                StartupType = ParseStartupType(startMode, name)
            });
        }
        return result;
    }

    private static ServiceStartupType ParseStartupType(string wmiStartMode, string serviceName)
    {
        return wmiStartMode switch
        {
            "Auto" => IsDelayedAutoStart(serviceName) ? ServiceStartupType.AutomaticDelayedStart : ServiceStartupType.Automatic,
            "Manual" => ServiceStartupType.Manual,
            "Disabled" => ServiceStartupType.Disabled,
            "Boot" => ServiceStartupType.Boot,
            "System" => ServiceStartupType.System,
            _ => ServiceStartupType.Unknown
        };
    }

    private static bool IsDelayedAutoStart(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            if (key?.GetValue("DelayedAutoStart") is int val)
                return val == 1;
        }
        catch { }
        return false;
    }

    private static void ChangeServiceStartType(string serviceName, ServiceStartupType newType)
    {
        string scMode = newType switch
        {
            ServiceStartupType.Automatic => "auto",
            ServiceStartupType.AutomaticDelayedStart => "delayed-auto",
            ServiceStartupType.Manual => "demand",
            ServiceStartupType.Disabled => "disabled",
            _ => null
        };
        if (scMode == null)
            return;

        try
        {
            var psi = new ProcessStartInfo("sc.exe", $"config \"{serviceName}\" start= {scMode}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to configure service {serviceName}: {ex.Message}");
        }
    }
}
