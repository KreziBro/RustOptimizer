using System;
using System.Threading.Tasks;
using System.Windows;
using RustOptimizer.Core;

namespace RustOptimizer;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Logger.Log(ex);
                System.Windows.MessageBox.Show(
                    $"Критическая ошибка запуска:\n{ex.Message}\n\nПодробности записаны в:\n{Logger.LogPath}",
                    "Rust Optimizer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Log(args.Exception);
            System.Windows.MessageBox.Show(
                $"Ошибка приложения:\n{args.Exception.Message}\n\nПодробности записаны в:\n{Logger.LogPath}",
                "Rust Optimizer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Log(args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
    }
}
