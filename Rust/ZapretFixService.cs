using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using RustOptimizer.Core;

namespace RustOptimizer.Rust;

/// <summary>
/// Port of ZapretFix service: checks if winws.exe (Zapret DPI bypass) is running
/// and stops/deletes the WinDivert kernel driver service if needed so EasyAntiCheat doesn't block Rust launch.
/// </summary>
public static class ZapretFixService
{
    private const string BypassProcessName = "winws";
    private const string DriverServiceName = "windivert";
    private const int SC_MANAGER_CONNECT = 1;
    private const int SERVICE_QUERY_STATUS = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public int dwServiceType;
        public int dwCurrentState;
        public int dwControlsAccepted;
        public int dwWin32ExitCode;
        public int dwServiceSpecificExitCode;
        public int dwCheckPoint;
        public int dwWaitHint;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint OpenSCManager(string machineName, string databaseName, int desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint OpenService(nint scManager, string serviceName, int desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(nint service, out SERVICE_STATUS serviceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(nint handle);

    public static bool IsZapretRunning()
    {
        try
        {
            var procs = Process.GetProcessesByName(BypassProcessName);
            bool running = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
            return running;
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return false;
        }
    }

    public static string GetWinDivertServiceState()
    {
        nint scm = nint.Zero;
        nint svc = nint.Zero;
        try
        {
            scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == nint.Zero)
                return "Неизвестно (нет доступа к SCM)";

            svc = OpenService(scm, DriverServiceName, SERVICE_QUERY_STATUS);
            if (svc == nint.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                return err switch
                {
                    1060 => "Не установлен (ОК)",
                    1072 => "Помечен на удаление (ОК)",
                    _ => $"Код Win32: {err}"
                };
            }

            if (!QueryServiceStatus(svc, out var status))
                return "Ошибка чтения статуса";

            return status.dwCurrentState switch
            {
                1 => "Остановлен",
                2 => "Запускается",
                3 => "Останавливается",
                4 => "Работает (активен)",
                _ => $"Состояние {status.dwCurrentState}"
            };
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return "Ошибка проверки";
        }
        finally
        {
            if (svc != nint.Zero) CloseServiceHandle(svc);
            if (scm != nint.Zero) CloseServiceHandle(scm);
        }
    }

    public static string GetStatusSummary()
    {
        bool zapret = IsZapretRunning();
        string driver = GetWinDivertServiceState();
        return $"Процесс winws.exe (Zapret): {(zapret ? "Запущен" : "Не запущен")} | Драйвер WinDivert: {driver}";
    }

    public static string ApplyFix()
    {
        try
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                int stopCode = RunSc($"stop {DriverServiceName}");
                if (stopCode == 0 || stopCode == 1060 || stopCode == 1062)
                    break;
                Thread.Sleep(300);
            }

            bool deleted = false;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                int delCode = RunSc($"delete {DriverServiceName}");
                if (delCode == 0 || delCode == 1060 || delCode == 1072 || !ServiceExistsInRegistry(DriverServiceName))
                {
                    deleted = true;
                    break;
                }
                Thread.Sleep(300);
            }

            return deleted
                ? "Служба драйвера WinDivert успешно остановлена и выгружена (конфликт с EAC устранён)."
                : "Команды выгрузки WinDivert выполнены, проверьте статус.";
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            return "Ошибка при применении Zapret Fix: " + ex.Message;
        }
    }

    private static bool ServiceExistsInRegistry(string name)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
            return k != null;
        }
        catch
        {
            return false;
        }
    }

    private static int RunSc(string args)
    {
        var psi = new ProcessStartInfo("sc.exe", args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(5000);
        Logger.Log($"ZapretFix: sc {args} -> exit {p.ExitCode} {stdout.Trim()} {stderr.Trim()}");
        return p.ExitCode;
    }
}
