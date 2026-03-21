using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using VelocityCosmic.Controls;

#nullable enable
namespace VelocityCosmic;

public partial class MainWindow : Window
{
    private int TabCounter;
    private bool _isRefreshing;
    private ListCollectionView? _explorerView;
    private MainWindow.AppSettings _appSettings;
    private bool _isInitializing;
    private TabItem? _tabToRename;
    private Border? _currentVisiblePage;
    private readonly HashSet<TabItem> _openedTabs = new HashSet<TabItem>();
    private MainWindow.SettingItem? _previousSelectedItem;
    private string? _logFilePath;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int GWL_STYLE = -16;
    private const int WS_SYSMENU = 524288;
    private const int WS_CAPTION = 12582912;
    private const int CS_DROPSHADOW = 131072;
    private const int GCL_STYLE = -26;
    private const int WS_THICKFRAME = 262144;
    private const int WS_MINIMIZEBOX = 131072;
    private const int WS_MAXIMIZEBOX = 65536;
    private const int DWMWA_NCRENDERING_POLICY = 2;
    private const int DWMNCRP_ENABLED = 2;
    private const int WM_GETMINMAXINFO = 36;
    private const int WM_DPICHANGED = 736;
    private const int MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOZORDER = 4;
    private const uint SWP_NOACTIVATE = 16;
    private const uint WDA_NONE = 0;
    private const uint WDA_EXCLUDEFROMCAPTURE = 17;

    private readonly HashSet<int> _attemptedInjections = new HashSet<int>();
    private DispatcherTimer _autoInjectTimer;

    public MainWindow()
    {
        InitializeComponent();

        _appSettings = SettingsManager.Load();

        Cosmic.OnClientConnected += pid => WriteToTerminal($"Client connected: PID {pid}");
        Cosmic.OnClientDisconnected += pid => { WriteToTerminal($"Client disconnected: PID {pid}"); _attemptedInjections.Remove(pid); };
        Cosmic.OnOutput += (pid, status, msg) =>
        {
            string prefix = status switch { 1 => "[WARN]", 2 => "[ERROR]", 3 => "[INFO]", _ => "[PRINT]" };
            WriteToTerminal($"[Cosmic/{pid}] {prefix} {msg}");
        };

        if (ToggleAutoInject != null) { ToggleAutoInject.Checked += Toggle_CheckedChanged; ToggleAutoInject.Unchecked += Toggle_CheckedChanged; }
        if (ToggleSaveScripts != null) { ToggleSaveScripts.Checked += Toggle_CheckedChanged; ToggleSaveScripts.Unchecked += Toggle_CheckedChanged; }
        if (ToggleAutoReport != null) { ToggleAutoReport.Checked += Toggle_CheckedChanged; ToggleAutoReport.Unchecked += Toggle_CheckedChanged; }
        if (ToggleTopMost != null) { ToggleTopMost.Checked += Toggle_CheckedChanged; ToggleTopMost.Unchecked += Toggle_CheckedChanged; }
        if (ToggleHideVel != null) { ToggleHideVel.Checked += Toggle_CheckedChanged; ToggleHideVel.Unchecked += Toggle_CheckedChanged; }
        if (ToggleEnablePreserve != null) { ToggleEnablePreserve.Checked += Toggle_CheckedChanged; ToggleEnablePreserve.Unchecked += Toggle_CheckedChanged; }
        if (DiscordRPCToggle != null) { DiscordRPCToggle.Checked += Toggle_CheckedChanged; DiscordRPCToggle.Unchecked += Toggle_CheckedChanged; }
        if (TogleExtLog != null) { TogleExtLog.Checked += Toggle_CheckedChanged; TogleExtLog.Unchecked += Toggle_CheckedChanged; }
        if (ToggleEnableBackground != null) { ToggleEnableBackground.Checked += ToggleEnableBackground_Checked; ToggleEnableBackground.Unchecked += ToggleEnableBackground_Unchecked; }
        if (ToggleSaveWindowSize != null) { ToggleSaveWindowSize.Checked += ToggleSaveWindowSize_CheckedChanged; ToggleSaveWindowSize.Unchecked += ToggleSaveWindowSize_CheckedChanged; }
        if (ToggleStartMinimized != null) { ToggleStartMinimized.Checked += Toggle_CheckedChanged; ToggleStartMinimized.Unchecked += Toggle_CheckedChanged; }

        this.Loaded += MainWindow_Loaded;
        this.Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SetToggleStatesFromSettings();

        _autoInjectTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _autoInjectTimer.Tick += async (s, ev) =>
        {
            if (!_appSettings.AutoInject || !Cosmic.IsRunning) return;
            var procs = Cosmic.GetRobloxProcesses();
            var clients = Cosmic.GetClients();
            foreach (var pid in procs)
            {
                if (!_attemptedInjections.Contains(pid) && !clients.Contains(pid))
                {
                    _attemptedInjections.Add(pid);
                    WriteToTerminal($"Auto-Injecting PID: {pid}...");
                    var result = await Task.Run(() => Cosmic.Attach(pid));
                    WriteToTerminal($"Auto-Inject PID {pid}: {Cosmic.GetAttachStatusMessage(result)}");
                }
            }
        };
        _autoInjectTimer.Start();

        if (_appSettings.EnableBackground)
            await LoadBackgroundImageAsync(_appSettings.BgImgUrl);

        Topmost = _appSettings.TopMost;

        if (_appSettings.SaveWindowSize && _appSettings.WindowWidth > 0 && _appSettings.WindowHeight > 0)
        {
            Width = _appSettings.WindowWidth;
            Height = _appSettings.WindowHeight;
        }

        if (_appSettings.SaveOutputLocally)
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "Logs"));

        if (_appSettings.StartMinimized)
            WindowState = WindowState.Minimized;

        LoadFiles(Path.Combine(Directory.GetCurrentDirectory(), "Scripts"));
        LoadSettings();

        WriteToTerminal($"Welcome to Velocity (Cosmic) by XomYoshi, {Environment.UserName}");
        WriteToTerminal("Supported version: version-ae421f0582e54718");

        WriteToTerminal("Initializing...");
        await Cosmic.Initialize();
        WriteToTerminal("Downloading 'Cosmic-Module' & 'Cosmic-Injector'...");
        await Cosmic.WaitForReady();
        WriteToTerminal("Done!");

        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (_appSettings.ExtendedLogging)
            WriteToTerminal($"Modifying HWND Affinity -> {handle}");
        SetExcludeFromCapture(handle, _appSettings.HideVelocity);

        await Task.Delay(1000);
        if (_appSettings.IsNew)
        {
            WelcomeMessage.Visibility = Visibility.Visible;
            WelcomeBackDrop.Opacity = 0.0;
            WelcomeBorderMessageHost.Opacity = 0.0;
            ((Storyboard)FindResource("OpenWelcomeStoryboard")).Begin();
        }
        else
            WelcomeMessage.Visibility = Visibility.Collapsed;
    }

    private async Task LoadBackgroundImageAsync(string imageUrl)
    {
        if (!_appSettings.EnableBackground)
        {
            BackgroundImage.Visibility = Visibility.Collapsed;
            BackgroundImage.Source = null;
        }
        else if (string.IsNullOrWhiteSpace(imageUrl))
        {
            var anim = new DoubleAnimation(BackgroundImage.Opacity, 0.0, (Duration)TimeSpan.FromMilliseconds(300));
            anim.Completed += (s, e) => { BackgroundImage.Visibility = Visibility.Collapsed; BackgroundImage.Source = null; };
            BackgroundImage.BeginAnimation(UIElement.OpacityProperty, anim);
        }
        else
        {
            try
            {
                using var client = new HttpClient();
                var resp = await client.GetAsync(imageUrl);
                resp.EnsureSuccessStatusCode();
                using var stream = await resp.Content.ReadAsStreamAsync();
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = stream;
                bmp.EndInit();
                bmp.Freeze();
                BackgroundImage.Source = bmp;
                BackgroundImage.Visibility = Visibility.Visible;
                BackgroundImage.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 0.2, (Duration)TimeSpan.FromMilliseconds(300)));
            }
            catch { BackgroundImage.Visibility = Visibility.Collapsed; BackgroundImage.Source = null; }
        }
    }

    private async void ToggleEnableBackground_Checked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _appSettings.EnableBackground = true;
        SettingsManager.Save(_appSettings);
        BackgroundUrlContainer.Visibility = Visibility.Visible;
        await LoadBackgroundImageAsync(_appSettings.BgImgUrl);
        var a1 = new DoubleAnimation(0.0, 1.0, (Duration)TimeSpan.FromMilliseconds(150)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        var a2 = new ThicknessAnimation(new Thickness(10, -10, 10, 0), new Thickness(10, 10, 10, 0), (Duration)TimeSpan.FromMilliseconds(150)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        var a3 = new DoubleAnimation(0.0, 41.0, (Duration)TimeSpan.FromMilliseconds(300)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
        BackgroundUrlContainer.BeginAnimation(UIElement.OpacityProperty, a1);
        BackgroundUrlContainer.BeginAnimation(FrameworkElement.MarginProperty, a2);
        BackgroundUrlContainer.BeginAnimation(FrameworkElement.HeightProperty, a3);
    }

    private void ToggleEnableBackground_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _appSettings.EnableBackground = false;
        SettingsManager.Save(_appSettings);
        var a1 = new DoubleAnimation(1.0, 0.0, (Duration)TimeSpan.FromMilliseconds(150)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        var a2 = new ThicknessAnimation(new Thickness(10, 10, 10, 0), new Thickness(10, -10, 10, 0), (Duration)TimeSpan.FromMilliseconds(150)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        var a3 = new DoubleAnimation(41.0, 0.0, (Duration)TimeSpan.FromMilliseconds(300)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
        a3.Completed += (s, _) => { BackgroundUrlContainer.Visibility = Visibility.Collapsed; BackgroundImage.Source = null; BackgroundImage.Visibility = Visibility.Collapsed; };
        BackgroundUrlContainer.BeginAnimation(UIElement.OpacityProperty, a1);
        BackgroundUrlContainer.BeginAnimation(FrameworkElement.MarginProperty, a2);
        BackgroundUrlContainer.BeginAnimation(FrameworkElement.HeightProperty, a3);
    }

    private async void BackgroundUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        _appSettings.BgImgUrl = BackgroundUrlTextBox.Text;
        SettingsManager.Save(_appSettings);
        await LoadBackgroundImageAsync(_appSettings.BgImgUrl);
    }

    private void ToggleSaveWindowSize_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _appSettings.SaveWindowSize = ToggleSaveWindowSize.IsChecked.GetValueOrDefault();
        SettingsManager.Save(_appSettings);
    }

    private async void SetToggleStatesFromSettings()
    {
        if (!IsLoaded) return;
        _isInitializing = true;
        ToggleAutoInject.IsChecked = _appSettings.AutoInject;
        ToggleSaveScripts.IsChecked = _appSettings.SaveScripts;
        ToggleAutoReport.IsChecked = _appSettings.UnlockFPS;
        ToggleTopMost.IsChecked = _appSettings.TopMost;
        ToggleHideVel.IsChecked = _appSettings.HideVelocity;
        DiscordRPCToggle.IsChecked = _appSettings.SaveOutputLocally;
        TogleExtLog.IsChecked = _appSettings.ExtendedLogging;
        ToggleStartMinimized.IsChecked = _appSettings.StartMinimized;
        ToggleSaveWindowSize.IsChecked = _appSettings.SaveWindowSize;
        ToggleEnablePreserve.IsChecked = _appSettings.PreserveUiElementSize;
        ToggleEnableBackground.IsChecked = _appSettings.EnableBackground;
        BackgroundUrlTextBox.Text = _appSettings.BgImgUrl ?? "";
        BackgroundUrlContainer.Visibility = _appSettings.EnableBackground ? Visibility.Visible : Visibility.Collapsed;
        BackgroundUrlContainer.Opacity = _appSettings.EnableBackground ? 1 : 0;
        _isInitializing = false;
        await LoadTabsAsync();
    }

    private void Toggle_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isInitializing) return;
        _appSettings.AutoInject = ToggleAutoInject.IsChecked.GetValueOrDefault();
        _appSettings.SaveScripts = ToggleSaveScripts.IsChecked.GetValueOrDefault();
        _appSettings.UnlockFPS = ToggleAutoReport.IsChecked.GetValueOrDefault();
        _appSettings.TopMost = ToggleTopMost.IsChecked.GetValueOrDefault();
        Topmost = _appSettings.TopMost;
        _appSettings.PreserveUiElementSize = ToggleEnablePreserve.IsChecked.GetValueOrDefault();
        bool hideVel = ToggleHideVel.IsChecked.GetValueOrDefault();
        if (_appSettings.HideVelocity != hideVel) { _appSettings.HideVelocity = hideVel; SetExcludeFromCapture(new WindowInteropHelper(this).Handle, hideVel); }
        _appSettings.SaveOutputLocally = DiscordRPCToggle.IsChecked.GetValueOrDefault();
        _appSettings.ExtendedLogging = TogleExtLog.IsChecked.GetValueOrDefault();
        _appSettings.StartMinimized = ToggleStartMinimized.IsChecked.GetValueOrDefault();
        SettingsManager.Save(_appSettings);

        if (Cosmic.IsRunning)
            Cosmic.SetUnlockFps(_appSettings.UnlockFPS);
    }

    private async Task KillDecompilerIfRunning()
    {
        foreach (Process process in Process.GetProcessesByName("Decompiler"))
        {
            try { process.Kill(true); process.WaitForExit(2000); }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            { MessageBox.Show("Can't access 'Decompiler.exe'.\n\nError -> " + ex.Message, "Access Denied", MessageBoxButton.OK, MessageBoxImage.Hand); break; }
            catch (Exception ex)
            { MessageBox.Show("Failed to terminate 'Decompiler.exe'.\n\nError -> " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand); break; }
        }
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_appSettings.SaveWindowSize && WindowState == WindowState.Normal)
        { _appSettings.WindowWidth = Width; _appSettings.WindowHeight = Height; }
        if (_appSettings.PreserveUiElementSize)
        { _appSettings.SavedExplorerWidth = ExplorerBorder.ActualWidth; _appSettings.SavedTerminalHeight = BottomHostElement.ActualHeight; }
        SettingsManager.Save(_appSettings);
        Cosmic.Shutdown();
        WriteToTerminal("Shutdown...");
        await KillDecompilerIfRunning();
    }

    private void ChromeDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        try { await SaveTabsAsync(); } catch { }
        Application.Current.Shutdown();
    }
    
    // Inject
    private async void InjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Cosmic.IsRunning) { WriteToTerminal("Not initialized yet"); return; }
        var procs = Cosmic.GetRobloxProcesses();
        if (procs.Count == 0) { WriteToTerminal("No Roblox process found"); return; }
        WriteToTerminal($"Injecting into: '{procs.Count}' process(es)...");
        var results = await Task.Run(() => Cosmic.Attach());
        foreach (var kv in results)
        {
            _attemptedInjections.Add(kv.Key);
            WriteToTerminal($"PID {kv.Key}: {Cosmic.GetAttachStatusMessage(kv.Value)}");
        }
    }

    // Execute
    private async void ExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Cosmic.IsRunning) { WriteToTerminal("Not initialized"); return; }
        if (Cosmic.ClientCount == 0) { WriteToTerminal("Not Injected!"); return; }
        if (!(TabCtrl.SelectedItem is TabItem tab) || !(tab.Content is Grid content))
        { WriteToTerminal("No tab selected"); return; }
        var editor = content.Children.OfType<WebViewAPI>().FirstOrDefault();
        if (editor == null) { WriteToTerminal("WebView not found"); return; }
        string script = await editor.GetText();
        if (string.IsNullOrWhiteSpace(script)) { WriteToTerminal("No script to Execute"); return; }
        try { await Cosmic.ExecuteAsync(script); WriteToTerminal("Script Executed"); }
        catch (Exception ex) { WriteToTerminal($"Execute Failed: {ex.Message}"); }
    }

    // Clear

    private async void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (!(TabCtrl.SelectedItem is TabItem tab) || !(tab.Content is Grid content)) return;
        var editor = content.Children.OfType<WebViewAPI>().FirstOrDefault();
        if (editor == null) return;
        try { await editor.SetText(""); } catch (Exception ex) { Console.WriteLine("[Editor] Clear failed: " + ex.Message); }
    }

    // Save
    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!(TabCtrl.SelectedItem is TabItem activeTab) || !(activeTab.Content is Grid content)) return;
        var editor = content.Children.OfType<WebViewAPI>().FirstOrDefault();
        if (editor == null) return;
        string text = await editor.GetText();
        if (string.IsNullOrWhiteSpace(text)) return;
        var dlg = new SaveFileDialog { Title = "Save your script", Filter = "All Files (*.*)|*.*|Lua Script (*.lua)|*.lua|Luau Script (*.luau)|*.luau|Text File (*.txt)|*.txt", FilterIndex = 2, FileName = activeTab.Header?.ToString() ?? "NewScript" };
        if (!dlg.ShowDialog().GetValueOrDefault()) return;
        try { await File.WriteAllTextAsync(dlg.FileName, text); } catch { }
    }

    // Open
    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Open a script file", Filter = "All Files (*.*)|*.*|Lua Script (*.lua)|*.lua|Luau Script (*.luau)|*.luau|Text File (*.txt)|*.txt", Multiselect = false };
        if (!dlg.ShowDialog().GetValueOrDefault()) return;
        string fileName = Path.GetFileName(dlg.FileName);
        string fileContent;
        try { fileContent = await File.ReadAllTextAsync(dlg.FileName); } catch { return; }
        if (TabCtrl.Items.Count == 0) { CreateEditorInstance(fileName, fileContent); return; }
        if (!(TabCtrl.SelectedItem is TabItem activeTab) || !(activeTab.Content is Grid content)) { CreateEditorInstance(fileName, fileContent); return; }
        var editor = content.Children.OfType<WebViewAPI>().FirstOrDefault();
        if (editor == null) { CreateEditorInstance(fileName, fileContent); return; }
        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        var tcs = new TaskCompletionSource<bool>();
        fadeOut.Completed += async (s2, e2) =>
        {
            try { await editor.SetText(fileContent); activeTab.Header = fileName; } catch (Exception ex) { Console.WriteLine("[Editor] Open failed: " + ex.Message); }
            ((UIElement)editor).BeginAnimation(UIElement.OpacityProperty, fadeIn);
            tcs.SetResult(true);
        };
        ((UIElement)editor).BeginAnimation(UIElement.OpacityProperty, fadeOut);
        await tcs.Task;
    }

    // Terminal

    private void CopyTerminalOutput_Click(object sender, RoutedEventArgs e)
    {
        if (TerminalBox.Document == null) return;
        string text = new TextRange(TerminalBox.Document.ContentStart, TerminalBox.Document.ContentEnd).Text;
        if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
        WriteToTerminal("Copied to clipboard!");
    }

    private void SaveTerminalOutput_Click(object sender, RoutedEventArgs e)
    {
        if (TerminalBox.Document == null) return;
        string text = new TextRange(TerminalBox.Document.ContentStart, TerminalBox.Document.ContentEnd).Text;
        var dlg = new SaveFileDialog { Filter = "Text file (*.txt)|*.txt|All files (*.*)|*.*", DefaultExt = ".txt", FileName = "TerminalOutput.txt" };
        if (!dlg.ShowDialog().GetValueOrDefault()) return;
        try { File.WriteAllText(dlg.FileName, text); }
        catch (Exception ex) { MessageBox.Show("Failed to save:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand); }
    }

    private void ClearTerminal_Click(object sender, RoutedEventArgs e) => TerminalBox.Document.Blocks.Clear();

    public void WriteToTerminal(string message)
    {
        TerminalBox.Dispatcher.Invoke(() =>
        {
            if (TerminalBox.Document == null)
                TerminalBox.Document = new FlowDocument { PagePadding = new Thickness(0), ColumnWidth = double.PositiveInfinity, TextAlignment = TextAlignment.Left, LineHeight = 18 };
            var doc = TerminalBox.Document;
            Paragraph para;
            if (doc.Blocks.FirstBlock is Paragraph p) para = p;
            else { para = new Paragraph { Margin = new Thickness(0) }; doc.Blocks.Add(para); }
            string ts = DateTime.Now.ToString("HH:mm:ss");
            para.Inlines.Add(new Run($"[{ts}] - ") { Foreground = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255)) });
            para.Inlines.Add(new Run(message) { Foreground = new SolidColorBrush(Color.FromArgb(204, 255, 255, 255)) });
            para.Inlines.Add(new LineBreak());
            TerminalBox.ScrollToEnd();
            if (!_appSettings.SaveOutputLocally || string.IsNullOrEmpty(_logFilePath)) return;
            try { File.AppendAllText(_logFilePath, $"[{ts}] - {message}{Environment.NewLine}"); }
            catch (Exception ex) { WriteToTerminal("Failed to save log: " + ex.Message); }
        });
    }

    public void WriteToTerminal(string message, string? linkText, string? linkUrl)
    {
        TerminalBox.Dispatcher.Invoke(() =>
        {
            if (TerminalBox.Document == null)
                TerminalBox.Document = new FlowDocument { PagePadding = new Thickness(0), ColumnWidth = double.PositiveInfinity, TextAlignment = TextAlignment.Left, LineHeight = 18 };
            var doc = TerminalBox.Document;
            Paragraph para;
            if (doc.Blocks.FirstBlock is Paragraph p) para = p;
            else { para = new Paragraph { Margin = new Thickness(0) }; doc.Blocks.Add(para); }
            para.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss}] - ") { Foreground = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255)) });
            para.Inlines.Add(new Run(message + " ") { Foreground = new SolidColorBrush(Color.FromArgb(204, 255, 255, 255)) });
            if (!string.IsNullOrEmpty(linkText) && !string.IsNullOrEmpty(linkUrl))
            {
                var link = new Hyperlink(new Run(linkText)) { Foreground = Brushes.DodgerBlue, TextDecorations = TextDecorations.Underline, NavigateUri = new Uri(linkUrl), ToolTip = "Download from rdd.weao.gg", Cursor = Cursors.Hand };
                link.RequestNavigate += (_, ev) => { try { Process.Start(new ProcessStartInfo(ev.Uri.AbsoluteUri) { UseShellExecute = true }); } catch (Exception ex) { WriteToTerminal("Failed to open link: " + ex.Message); } };
                para.Inlines.Add(link);
            }
            para.Inlines.Add(new LineBreak());
            TerminalBox.ScrollToEnd();
        });
    }

    // Settings UI

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        Storyboard closeSb = (Storyboard)TryFindResource("CloseWelcomeStoryboard");
        if (closeSb == null) { MessageBox.Show("Failed to find resource 'CloseWelcomeStoryboard'."); return; }
        EventHandler handler = null;
        handler = (s, args) => { WelcomeMessage.Visibility = Visibility.Collapsed; _appSettings.IsNew = false; SettingsManager.Save(_appSettings); closeSb.Completed -= handler; };
        closeSb.Completed += handler;
        closeSb.Begin();
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        Storyboard sb = (Storyboard)FindResource("CloseSettingsStoryboard");
        sb.Completed += (_1, _2) => { SettingsHost.Visibility = Visibility.Collapsed; EditorControl.Visibility = Visibility.Visible; SettingsHostBorder.Opacity = 1; SettingsBackDrop.Opacity = 0.6; SettingsScale.ScaleX = 1; SettingsScale.ScaleY = 1; };
        sb.Begin();
    }

    private void OpenSettings()
    {
        SettingsHost.Visibility = Visibility.Visible;
        ((Storyboard)FindResource("OpenSettingsStoryboard")).Begin();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsHost.Visibility == Visibility.Visible) return;
        EditorControl.Visibility = Visibility.Hidden;
        SettingsHostBorder.Opacity = 0; SettingsBackDrop.Opacity = 0;
        SettingsScale.ScaleX = 0.94; SettingsScale.ScaleY = 0.94;
        OpenSettings();
    }

    private void LoadSettings()
    {
        var items = new List<SettingItem>();
        var groups = new Dictionary<string, string[]>
        {
            { "GENERAL", new[] { "General", "Interface" } },
            { "DEBUG",   new[] { "Logging", "Help & Support" } }
        };
        var icons = new Dictionary<string, string>
        {
            { "General",        "Resource\\Settings\\Settings_Module.png" },
            { "Interface",      "Resource\\Settings\\Settings_Window.png" },
            { "Logging",        "Resource\\Settings\\Settings_Logging.png" },
            { "Help & Support", "Resource\\Settings\\Settings_Support.png" }
        };
        foreach (var kv in groups)
            foreach (var name in kv.Value)
                items.Add(new SettingItem(name, kv.Key, icons.GetValueOrDefault(name, icons["Logging"])));
        var view = new ListCollectionView(items);
        view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        SettingsListBox.ItemsSource = view;
        SettingsListBox.SelectedItem = items.FirstOrDefault();
    }

    private void SettingsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!(SettingsListBox.SelectedItem is SettingItem selectedItem)) return;
        if (string.Equals(selectedItem.Name, "Help & Support"))
        {
            if (MessageBox.Show("Would you like to join the Discord server for support?", "Join Discord", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                try { Process.Start(new ProcessStartInfo("https://discord.gg/getcosmic") { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show("Failed to open link: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand); }
            if (_previousSelectedItem != null) SettingsListBox.SelectedItem = _previousSelectedItem;
            else SettingsListBox.SelectedIndex = -1;
            return;
        }
        switch (selectedItem.Name)
        {
            case "General": ShowSettingsPage(GeneralBorder); break;
            case "Interface": ShowSettingsPage(InterfaceBorder); break;
            case "Logging": ShowSettingsPage(DebugBorderSettings); break;
            default:
                if (_currentVisiblePage != null) { AnimatePageOut(_currentVisiblePage); _currentVisiblePage.Visibility = Visibility.Collapsed; _currentVisiblePage = null; }
                break;
        }
        _previousSelectedItem = selectedItem;
    }

    private void ShowSettingsPage(Border page)
    {
        if (_currentVisiblePage == page) return;
        if (_currentVisiblePage != null) AnimatePageOut(_currentVisiblePage);
        AnimatePageIn(page);
        _currentVisiblePage = page;
    }

    // Animations

    private void AnimatePageIn(UIElement el, int ms = 350)
    {
        if (el == null) return;
        el.Visibility = Visibility.Visible;
        if (!(el.RenderTransform is TranslateTransform tt)) el.RenderTransform = tt = new TranslateTransform();
        tt.Y = 20; el.Opacity = 0;
        el.Dispatcher.BeginInvoke(() => { AnimateSlide(el, 20, 0, ms); AnimateOpacity(el, 1, ms + 50); }, DispatcherPriority.Loaded);
    }

    private Task Fade(UIElement el, double to, double dur = 0.25)
    {
        var tcs = new TaskCompletionSource<bool>();
        var a = new DoubleAnimation { To = to, Duration = TimeSpan.FromSeconds(dur), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        a.Completed += (_, __) => tcs.SetResult(true);
        el.BeginAnimation(UIElement.OpacityProperty, a);
        return tcs.Task;
    }

    private void AnimateRenamePopIn(UIElement el, int ms = 350)
    {
        if (el == null) return;
        el.Visibility = Visibility.Visible;
        if (!(el.RenderTransform is TranslateTransform tt)) el.RenderTransform = tt = new TranslateTransform();
        tt.Y = 20; el.Opacity = 0;
        el.Dispatcher.BeginInvoke(() =>
        {
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }, DispatcherPriority.Loaded);
    }

    private void AnimateRenamePopOut(UIElement el, int ms = 250)
    {
        if (el == null) return;
        if (!(el.RenderTransform is TranslateTransform tt)) el.RenderTransform = tt = new TranslateTransform();
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -30, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        var fade = new DoubleAnimation(el.Opacity, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (s, e) => el.Visibility = Visibility.Collapsed;
        el.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void AnimateOpacity(UIElement el, double to, int ms = 300)
        => el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(ms), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } });

    private void AnimatePageOut(UIElement el, int ms = 250)
    {
        if (el == null) return;
        if (!(el.RenderTransform is TranslateTransform)) el.RenderTransform = new TranslateTransform();
        el.Dispatcher.BeginInvoke(() =>
        {
            var a = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
            a.Completed += (s, e) => el.Visibility = Visibility.Collapsed;
            el.BeginAnimation(UIElement.OpacityProperty, a);
            AnimateSlide(el, 0, -20, ms);
        }, DispatcherPriority.Loaded);
    }

    private void AnimateSlide(UIElement el, double fromY, double toY, int ms = 350)
    {
        if (!(el.RenderTransform is TranslateTransform tt)) el.RenderTransform = tt = new TranslateTransform();
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, toY, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    // Explorer

    private void SpinReloadIcon()
    {
        if (!(ReloadImage.RenderTransform is RotateTransform rt)) return;
        rt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(1000)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
    }

    private void ReloadExplroer_Click(object sender, RoutedEventArgs e) { SpinReloadIcon(); LoadFiles(Path.Combine(Directory.GetCurrentDirectory(), "Scripts")); }

    private void LoadFiles(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;
        _isRefreshing = true;
        var items = new List<FileItem>();
        string favDir = Path.Combine(folderPath, "Favourites");
        if (Directory.Exists(favDir))
            items.AddRange(Directory.GetFiles(favDir).OrderBy(f => f).Select(f => new FileItem { Name = Path.GetFileName(f), Icon = "/Resource/Exp/Star_Gold.png", IsFavourite = true, Category = "FAVOURITES", FullPath = f }));
        foreach (string dir in Directory.GetDirectories(folderPath))
        {
            if (Path.GetFileName(dir).Equals("Favourites", StringComparison.OrdinalIgnoreCase)) continue;
            items.AddRange(Directory.GetFiles(dir).OrderBy(f => f).Select(f => new FileItem { Name = Path.GetFileName(f), Icon = GetIconForFile(f), Category = Path.GetFileName(dir).ToUpperInvariant(), FullPath = f }));
        }
        items.AddRange(Directory.GetFiles(folderPath).Where(f => !f.Contains("Favourites")).OrderBy(f => f).Select(f => new FileItem { Name = Path.GetFileName(f), Icon = GetIconForFile(f), Category = "FILES", FullPath = f }));
        _explorerView = new ListCollectionView(items);
        _explorerView.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        ExplorerListBox.ItemsSource = _explorerView;
        ExplorerListBox.SelectedIndex = -1;
        _isRefreshing = false;
    }

    private string GetIconForFile(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLower();
        return ext == ".txt" ? "/Resource/Exp/TXT-File.png" : (ext is ".lua" or ".luau") ? "/Resource/Exp/Script-File_Alt.png" : "/Resource/Exp/File-Default.png";
    }

    private async void ExplorerListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing || Mouse.RightButton == MouseButtonState.Pressed || !ExplorerListBox.IsLoaded) return;
        if (!(ExplorerListBox.SelectedItem is FileItem f) || string.IsNullOrEmpty(f.FullPath)) return;
        if (!File.Exists(f.FullPath)) { ExplorerListBox.SelectedIndex = -1; return; }
        try
        {
            string content = (await File.ReadAllTextAsync(f.FullPath)).Replace("\\t", "\t").Replace("\\n", "\n");
            CreateEditorInstance(f.Name, content, f.Icon ?? "/Assets/Explorer/File-Default.png");
        }
        catch { }
        ExplorerListBox.SelectedIndex = -1;
    }

    private void ExplorerListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = VisualUpwardSearch<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item == null) return;
        item.IsSelected = true; item.Focus();
    }

    private static T? VisualUpwardSearch<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source != null && source is not T) source = VisualTreeHelper.GetParent(source);
        return source as T;
    }

    private bool ExplorerFilter(object obj)
        => obj is FileItem fi && (string.IsNullOrWhiteSpace(SearchTextBoxExp.Text) || fi.Name?.IndexOf(SearchTextBoxExp.Text, StringComparison.OrdinalIgnoreCase) >= 0);

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_explorerView == null) return;
        _explorerView.Filter = ExplorerFilter;
        _explorerView.Refresh();
    }

    public void CreateEditorInstance(string title, string content = "", string? iconPath = null)
    {
        var monaco = new WebViewAPI { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        ((UIElement)monaco).Visibility = Visibility.Hidden;
        string indexPath = Path.Combine(Directory.GetCurrentDirectory(), "Bin", "MonacoEditor", "index.html");
        if (!File.Exists(indexPath)) { MessageBox.Show("Monaco HTML not found at: " + indexPath, "Error", MessageBoxButton.OK, MessageBoxImage.Hand); return; }
        monaco.Source = new Uri(indexPath);
        if (((FrameworkElement)monaco).Parent is Panel pp) pp.Children.Remove(monaco);
        else if (((FrameworkElement)monaco).Parent is ContentControl cc) cc.Content = null;
        var contentGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Opacity = 0 };
        contentGrid.Children.Add(monaco);
        var tab = new TabItem
        {
            Header = title,
            Content = contentGrid,
            Style = TryFindResource("Tab") as Style,
            Tag = string.IsNullOrEmpty(iconPath) ? "/Resource/tabs/icons/script-file_alt.png" : iconPath,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        TabCtrl.Items.Add(tab);
        TabCtrl.SelectedItem = tab;
        if (TabCtrl.Items.Count == 1) Fade(EmptyStatePanel, 0.0);

        tab.Loaded += async (s, e) =>
        {
            var closeBtn = FindCloseButtonInTemplate(tab);
            if (closeBtn != null)
                closeBtn.Click += async (s2, e2) =>
                {
                    Task t1 = Task.CompletedTask, t2 = Task.CompletedTask;
                    if (tab.Template.FindName("RootGrid", tab) is Grid rg)
                    {
                        var tcs = new TaskCompletionSource<bool>();
                        var so = new DoubleAnimation { To = -50, Duration = TimeSpan.FromSeconds(0.25), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                        var fo = new DoubleAnimation { To = 0, Duration = TimeSpan.FromSeconds(0.25), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                        fo.Completed += (a, b) => tcs.SetResult(true);
                        rg.RenderTransform = new TranslateTransform();
                        rg.RenderTransform.BeginAnimation(TranslateTransform.XProperty, so);
                        rg.BeginAnimation(UIElement.OpacityProperty, fo);
                        t1 = tcs.Task;
                    }
                    if (tab.Content is Grid cg)
                    {
                        var tcs = new TaskCompletionSource<bool>();
                        var fo = new DoubleAnimation { To = 0, Duration = TimeSpan.FromSeconds(0.25), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                        fo.Completed += (a, b) => tcs.SetResult(true);
                        cg.BeginAnimation(UIElement.OpacityProperty, fo);
                        t2 = tcs.Task;
                    }
                    await Task.WhenAll(t1, t2);
                    if (tab.Content is Grid cgf) { var ed = cgf.Children.OfType<WebViewAPI>().FirstOrDefault(); if (ed != null) { await ed.SetText(""); ((UIElement)ed).Visibility = Visibility.Hidden; } }
                    TabCtrl.Items.Remove(tab);
                    if (TabCtrl.Items.Count == 0) await Fade(EmptyStatePanel, 1.0);
                };

            if (tab.Template.FindName("TabContextMenu", tab) is ContextMenu cm)
            {
                tab.ContextMenu = cm;
                cm.Opened += (sender2, args2) => RegisterContextMenuItems(cm, tab);
            }

            var readyTcs = new TaskCompletionSource<bool>();
            EventHandler readyHandler = null;
            readyHandler = (s2, a2) => { _openedTabs.Add(tab); monaco.EditorReady -= readyHandler; readyTcs.SetResult(true); };
            monaco.EditorReady += readyHandler;
            await readyTcs.Task;
            await monaco.SetText(content);
            ((UIElement)monaco).Visibility = Visibility.Visible;
            contentGrid.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        };

        tab.PreviewMouseRightButtonDown += (s, e) =>
        {
            e.Handled = true;
            if (tab.ContextMenu == null) return;
            tab.ContextMenu.PlacementTarget = tab;
            tab.ContextMenu.Placement = PlacementMode.MousePoint;
            tab.ContextMenu.IsOpen = true;
        };

        static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t && t.Name == name) return t;
                var found = FindVisualChild<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }

        void RegisterContextMenuItems(ContextMenu cm, TabItem t)
        {
            AttachHandler(FindVisualChild<MenuItem>(cm, "DuplicateMenuItem"));
            AttachHandler(FindVisualChild<MenuItem>(cm, "RenameMenuItem"));
            AttachHandler(FindVisualChild<MenuItem>(cm, "ExecuteMenuItem"));
            AttachHandler(FindVisualChild<MenuItem>(cm, "CloseOtherMenuItem"));
        }

        void AttachHandler(MenuItem? item) { if (item == null) return; item.Click -= MenuItemClickHandler; item.Click += MenuItemClickHandler; }

        async void MenuItemClickHandler(object sender2, RoutedEventArgs args2)
        {
            if (!(sender2 is MenuItem mi) || !(mi.Parent is ContextMenu parent)) return;
            if (!(parent.PlacementTarget is TabItem t) || !(mi.Tag is string tag)) return;
            switch (tag)
            {
                case "CopyyTab":
                    string copied = t.Content is Grid cg && cg.Children[0] is WebViewAPI w ? await w.GetText() : "";
                    CreateEditorInstance("Copy of " + t.Header, copied, t.Tag?.ToString());
                    break;
                case "CloseAllButThis":
                    if (MessageBox.Show($"Close all tabs except '{t.Header}'?", "Close Other Tabs", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes) return;
                    foreach (var item in TabCtrl.Items.Cast<TabItem>().Where(x => x != t).ToList()) TabCtrl.Items.Remove(item);
                    break;
                case "RenameTab":
                    ShowRenamePopup(t);
                    break;
                case "ExecuteTab":
                    if (!Cosmic.IsRunning || Cosmic.ClientCount == 0) { WriteToTerminal("Not Injected!"); return; }
                    if (!(t.Content is Grid cg2)) return;
                    var ed = cg2.Children.OfType<WebViewAPI>().FirstOrDefault();
                    if (ed == null) return;
                    string script = await ed.GetText();
                    if (!string.IsNullOrWhiteSpace(script))
                        try { await Cosmic.ExecuteAsync(script); WriteToTerminal("Script Executed"); }
                        catch (Exception ex) { WriteToTerminal($"Execute failed: {ex.Message}"); }
                    break;
            }
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) { if (_tabToRename != null) _tabToRename.Header = RenameTabTextBox.Text; HideRenamePopup(); }
    private void CancelButton_Click(object sender, RoutedEventArgs e) => HideRenamePopup();

    private void ShowRenamePopup(TabItem tab)
    {
        _tabToRename = tab;
        RenameTabTextBox.Text = tab.Header?.ToString() ?? "";
        RenamePopup.Visibility = Visibility.Visible;
        AnimateRenamePopIn(CoreBorderRename);
        AnimateOpacity(ShadowBlockRenameTab, 0.5);
    }

    private async void HideRenamePopup()
    {
        AnimateRenamePopOut(CoreBorderRename);
        AnimateOpacity(ShadowBlockRenameTab, 0.0);
        _tabToRename = null;
        await Task.Delay(400);
        RenamePopup.Visibility = Visibility.Collapsed;
    }

    private Button? FindCloseButtonInTemplate(TabItem t) => t.Template.FindName("CloseButton", t) as Button;

    private async void RemoveTab(TabItem tabItem)
    {
        if (tabItem?.Content is Grid content) { var ed = content.Children.OfType<WebViewAPI>().FirstOrDefault(); if (ed != null) { await ed.SetText(""); ((UIElement)ed).Visibility = Visibility.Hidden; } }
        TabCtrl.Items.Remove(tabItem);
    }

    private async Task SaveTabsAsync()
    {
        if (!_appSettings.SaveScripts) return;
        var existing = (await TabSessionManager.LoadAsync()).Where(t => !string.IsNullOrEmpty(t.Title)).ToDictionary(t => t.Title!);
        var toSave = new List<SavedTab>();
        foreach (TabItem tab in TabCtrl.Items)
        {
            string title = tab.Header?.ToString() ?? "Untitled";
            if (_openedTabs.Contains(tab))
            {
                string text = "";
                if (tab.Content is Grid cg) { var w = cg.Children.OfType<WebViewAPI>().FirstOrDefault(); if (w != null) text = await w.GetText(); }
                toSave.Add(new SavedTab { Title = title, IconPath = tab.Tag?.ToString() ?? "", Content = text });
            }
            else
                toSave.Add(existing.TryGetValue(title, out var st) ? st : new SavedTab { Title = title, IconPath = tab.Tag?.ToString() ?? "", Content = "" });
        }
        await TabSessionManager.SaveAsync(toSave);
    }

    private async Task LoadTabsAsync()
    {
        if (!_appSettings.SaveScripts) return;
        WriteToTerminal("Loaded -> /Tab.json");
        foreach (var tab in await TabSessionManager.LoadAsync())
            CreateEditorInstance(tab.Title ?? "Untitled", tab.Content ?? "", tab.IconPath);
    }

    // ── Window chrome / DWM ──────────────────────────────────────────────────

    protected virtual void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr h = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(h)?.AddHook(WndProc);
        EnableDwmNonClientRendering(h);
        SetWindowLong(h, -16, GetWindowLong(h, -16) | 13565952);
        EnableDropShadow(h);
    }

    protected virtual void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        IntPtr h = new WindowInteropHelper(this).Handle;
        if (IsWindows11OrGreater()) { SetWindowCornerPreference(h); EnableDropShadow(h); }
        else EnableDropShadow(h);
    }

    protected virtual void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        IntPtr h = new WindowInteropHelper(this).Handle;
        if (IsWindows11OrGreater()) SetWindowCornerPreference(h);
        Task.Delay(50).ContinueWith(_ => Dispatcher.Invoke(() => EnableDropShadow(h)));
    }

    [DllImport("user32.dll")] internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private static bool IsWindows11OrGreater() { var v = Environment.OSVersion.Version; return v.Major >= 10 && v.Build >= 22000; }
    private void SetWindowCornerPreference(IntPtr hwnd) { if (!IsWindows11OrGreater()) return; int v = 2; DwmSetWindowAttribute(hwnd, 33, ref v, 4); }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case 36: WmGetMinMaxInfo(hwnd, lParam); handled = true; break;
            case 736: var r = Marshal.PtrToStructure<RECT>(lParam); SetWindowPos(hwnd, IntPtr.Zero, r.left, r.top, r.right - r.left, r.bottom - r.top, 20); handled = true; break;
        }
        return IntPtr.Zero;
    }

    private void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        IntPtr mon = MonitorFromWindow(hwnd, 2);
        if (mon != IntPtr.Zero)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            GetMonitorInfo(mon, ref mi);
            GetDpiForMonitor(mon, Monitor_DPI_Type.MDT_EFFECTIVE_DPI, out uint _, out uint dpiY);
            int topFrame = (int)(SystemParameters.WindowNonClientFrameThickness.Top * (dpiY / 96.0));
            info.ptMaxPosition.x = mi.rcWork.left; info.ptMaxPosition.y = mi.rcWork.top;
            info.ptMaxSize.x = mi.rcWork.right - mi.rcWork.left;
            info.ptMaxSize.y = mi.rcWork.bottom - mi.rcWork.top - topFrame + 25;
        }
        info.ptMinTrackSize.x = 803; info.ptMinTrackSize.y = 498;
        Marshal.StructureToPtr(info, lParam, true);
    }

    private void EnableDropShadow(IntPtr hwnd) { int v = GetClassLong(hwnd, -26) | 131072; SetClassLong(hwnd, -26, v); }
    private bool SetExcludeFromCapture(IntPtr hwnd, bool enable) => SetWindowDisplayAffinity(hwnd, enable ? 17u : 0u);

    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern bool Rectangle(IntPtr hdc, int left, int top, int right, int bottom);
    [DllImport("user32.dll")] private static extern int GetClassLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetClassLong(IntPtr hwnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmonitor, Monitor_DPI_Type dpiType, out uint dpiX, out uint dpiY);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    private void EnableDwmNonClientRendering(IntPtr hwnd) { int v = 2; DwmSetWindowAttribute(hwnd, 2, ref v, 4); }

    // App settings

    public class AppSettings
    {
        public bool AutoInject { get; set; }
        public bool SaveScripts { get; set; } = true;
        public bool UnlockFPS { get; set; }
        public bool TopMost { get; set; }
        public bool SaveWindowSize { get; set; }
        public bool IsNew { get; set; } = true;
        public bool HideVelocity { get; set; }
        public bool SaveOutputLocally { get; set; }
        public bool ExtendedLogging { get; set; }
        public bool StartMinimized { get; set; }
        public bool EnableBackground { get; set; }
        public string BgImgUrl { get; set; } = "";
        public double WindowWidth { get; set; } = 920;
        public double WindowHeight { get; set; } = 620;
        public bool PreserveUiElementSize { get; set; }
        public double SavedExplorerWidth { get; set; } = 310;
        public double SavedTerminalHeight { get; set; } = 170;
    }

    public static class SettingsManager
    {
        private static readonly string ConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Velocity Ui", "config.json");
        public static AppSettings Load()
        {
            try { return !File.Exists(ConfigPath) ? new AppSettings() : JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigPath)) ?? new AppSettings(); }
            catch { return new AppSettings(); }
        }
        public static void Save(AppSettings s)
        {
            try
            {
                string? dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }

    public class SettingItem
    {
        public string Name { get; set; }
        public string Icon { get; set; }
        public string Category { get; set; }
        public SettingItem(string name, string category, string icon) { Name = name; Category = category; Icon = icon; }
    }

    public class FileItem : INotifyPropertyChanged
    {
        private bool _isLoading;
        public string? Name { get; set; }
        public string? Icon { get; set; }
        public bool IsFavourite { get; set; }
        public string? Category { get; set; }
        public string? FullPath { get; set; }
        public bool IsLoading { get => _isLoading; set { if (_isLoading == value) return; _isLoading = value; OnPropertyChanged(nameof(IsLoading)); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public class SavedTab
    {
        public string? Title { get; set; } = "";
        public string? IconPath { get; set; } = "";
        public string? Content { get; set; } = "";
    }

    internal enum WindowCompositionAttribute { WCA_ACCENT_POLICY = 19 }
    internal struct WindowCompositionAttributeData { public WindowCompositionAttribute Attribute; public IntPtr Data; public int SizeOfData; }
    private enum DWM_WINDOW_CORNER_PREFERENCE { DWMWCP_DEFAULT, DWMWCP_DONOTROUND, DWMWCP_ROUND, DWMWCP_ROUNDSMALL }

    public class NativeScrollInterceptor
    {
        private readonly ScrollViewer _sv;
        private double _target;
        public NativeScrollInterceptor(ScrollViewer sv, Window window)
        {
            _sv = sv; _target = sv.VerticalOffset;
            HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle()).AddHook(WndProc);
            CompositionTarget.Rendering += OnRender;
        }
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 522) { _target -= (double)(short)(wParam.ToInt64() >> 16 & 0xFFFF); _target = Math.Max(0, Math.Min(_sv.ScrollableHeight, _target)); handled = true; }
            return IntPtr.Zero;
        }
        private void OnRender(object? sender, EventArgs e)
        {
            double cur = _sv.VerticalOffset, next = cur + (_target - cur) * 0.2;
            if (Math.Abs(next - cur) > 0.1) _sv.ScrollToVerticalOffset(next);
        }
    }

    private enum Monitor_DPI_Type { MDT_EFFECTIVE_DPI }
    private struct POINT { public int x, y; }
    private struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
    private struct RECT { public int left, top, right, bottom; }
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }
    private struct MARGINS { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }

    private int _newScriptCount = 1;

    private void TabCtrl_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TabControl tc) return;
        if (tc.Template.FindName("AddTabButton", tc) is Button btn) { btn.Click -= AddTabButton_Click; btn.Click += AddTabButton_Click; }
    }

    private void AddTabButton_Click(object sender, RoutedEventArgs e)
        => CreateEditorInstance($"New Script {_newScriptCount++}", "", null);
}
