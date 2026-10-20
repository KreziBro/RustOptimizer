using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using RustOptimizer.Bridge;
using RustOptimizer.Core;

namespace RustOptimizer;

public partial class MainWindow : Window
{
    private string _currentPath = "/";

    private static readonly Dictionary<string, (string ru, string en)> SectionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/"] = ("Твики", "Tweaks"),
        ["/binds"] = ("Бинды", "Binds"),
        ["/optimisation"] = ("Оптимизация", "Optimization"),
        ["/graphics"] = ("Графика", "Graphics"),
        ["/instruments"] = ("Инструменты", "Tools"),
        ["/camera_codes"] = ("Камеры CCTV", "Camera Codes"),
        ["/settings"] = ("Настройки", "Settings"),
        ["/statistic"] = ("Статистика", "Statistics")
    };

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += MainWindow_StateChanged;
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            MainWindowBorder.CornerRadius = new CornerRadius(0);
            MainWindowBorder.BorderThickness = new Thickness(0);
        }
        else
        {
            MainWindowBorder.CornerRadius = new CornerRadius(20);
            MainWindowBorder.BorderThickness = new Thickness(1);
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            string userDataFolder = Path.Combine(AppPaths.Root, "WebView2Data");
            Directory.CreateDirectory(userDataFolder);

            webView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 9, 9, 13);
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await webView.EnsureCoreWebView2Async(env);

            var settings = webView.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;

            string contentDir = Path.Combine(AppContext.BaseDirectory, "content");
            if (!Directory.Exists(contentDir))
            {
                string devContent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "content"));
                if (Directory.Exists(devContent))
                    contentDir = devContent;
            }

            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.local",
                contentDir,
                CoreWebView2HostResourceAccessKind.Allow);

            webView.CoreWebView2.AddHostObjectToScript("csAPI", new JsBridge(this));
            webView.CoreWebView2.AddHostObjectToScript("optimizations", new OptimizationsBridge());

            webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            webView.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;

            RefreshWindowTitle();
            webView.Source = new Uri("https://app.local/index.html");
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            System.Windows.MessageBox.Show(
                $"Ошибка инициализации WebView2:\n{ex.Message}",
                "Rust Optimizer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CoreWebView2_SourceChanged(object sender, CoreWebView2SourceChangedEventArgs e)
    {
        try
        {
            if (Uri.TryCreate(webView.Source?.ToString(), UriKind.Absolute, out Uri uri))
            {
                _currentPath = string.IsNullOrWhiteSpace(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
                RefreshWindowTitle();
            }
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
        }
    }

    private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string json = e.WebMessageAsJson;
            if (string.IsNullOrWhiteSpace(json)) return;

            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                using var innerDoc = JsonDocument.Parse(root.GetString() ?? "{}");
                HandleMessageElement(innerDoc.RootElement);
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                HandleMessageElement(root);
            }
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
        }
    }

    private void HandleMessageElement(JsonElement msg)
    {
        if (!msg.TryGetProperty("action", out var actionProp)) return;
        string action = actionProp.GetString();

        if (action == "navigation-state" && msg.TryGetProperty("payload", out var payload))
        {
            bool hasPrev = payload.TryGetProperty("hasPrevious", out var hp) && hp.ValueKind == JsonValueKind.True;
            bool hasNext = payload.TryGetProperty("hasNext", out var hn) && hn.ValueKind == JsonValueKind.True;
            PreviousPageButton.IsEnabled = hasPrev;
            NextPageButton.IsEnabled = hasNext;

            if (payload.TryGetProperty("path", out var pathProp) && pathProp.ValueKind == JsonValueKind.String)
            {
                _currentPath = pathProp.GetString() ?? "/";
                RefreshWindowTitle();
            }
        }
        else if (action == "use_instrument" && msg.TryGetProperty("payload", out var instPayload))
        {
            if (instPayload.TryGetProperty("link", out var linkProp))
            {
                string link = linkProp.GetString();
                if (!string.IsNullOrWhiteSpace(link))
                {
                    try { Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); }
                    catch (Exception ex) { Logger.Log(ex); }
                }
            }
        }
    }

    public void RefreshWindowTitle()
    {
        try
        {
            bool isEn = false;
            string langPath = Path.Combine(AppPaths.Root, "lang.json");
            if (File.Exists(langPath))
            {
                string txt = File.ReadAllText(langPath, Encoding.UTF8);
                isEn = txt.Contains("\"en\"", StringComparison.OrdinalIgnoreCase);
            }

            string key = _currentPath ?? "/";
            if (!SectionNames.TryGetValue(key, out var pair))
                pair = SectionNames["/"];

            SectionTitleText.Text = isEn ? pair.en : pair.ru;
        }
        catch
        {
            SectionTitleText.Text = "Твики";
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeButton_Click(sender, e);
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private async void PreviousPageButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (webView?.CoreWebView2 != null)
                await webView.CoreWebView2.ExecuteScriptAsync("window.previusPage && window.previusPage()");
        }
        catch (Exception ex) { Logger.Log(ex); }
    }

    private async void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (webView?.CoreWebView2 != null)
                await webView.CoreWebView2.ExecuteScriptAsync("window.nextPage && window.nextPage()");
        }
        catch (Exception ex) { Logger.Log(ex); }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
