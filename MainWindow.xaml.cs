using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TaskLens.Core;

namespace TaskLens;

public partial class MainWindow : Window
{
    private enum AppPage
    {
        Dashboard,
        Processes,
        Insights,
        Review,
        Startup,
        Reports,
        PrivacyCenter,
        Settings
    }

    private readonly SettingsService _settingsService = new();
    private readonly ProcessScanner _processScanner = new();
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly Dictionary<AppPage, Button> _navigationButtons = [];
    private readonly AppSettings _settings;
    private readonly LocalizationService _localizer;
    private readonly ReportService _reportService;

    private SystemSnapshot _snapshot = new();
    private CancellationTokenSource? _scanCancellation;
    private AppPage _currentPage = AppPage.Dashboard;

    private SolidColorBrush _primaryText = Brush("#18243B");
    private SolidColorBrush _secondaryText = Brush("#66758D");
    private SolidColorBrush _surface = Brushes.White;
    private SolidColorBrush _pageBackground = Brush("#EEF3FB");
    private SolidColorBrush _border = Brush("#DCE3EF");

    private static string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.1";

    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsService.Load();
        _localizer = new LocalizationService(_settings.Language);
        _reportService = new ReportService(_settingsService);

        ApplyTheme();
        BuildChrome();
        Navigate(AppPage.Dashboard);

        _refreshTimer.Interval = TimeSpan.FromSeconds(ClampRefreshInterval(_settings.RefreshSeconds));
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_settings.OnboardingSeen)
        {
            Onboarding.Visibility = Visibility.Visible;
            return;
        }

        await ScanAsync(showOverlay: false);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _scanCancellation?.Cancel();
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await ScanAsync(showOverlay: false);
    }

    private async void WelcomeStart_Click(object sender, RoutedEventArgs e)
    {
        CloseOnboarding();
        await ScanAsync(showOverlay: true);
    }

    private void WelcomeCheck_Click(object sender, RoutedEventArgs e)
    {
        CloseOnboarding();
        Navigate(AppPage.Processes);
    }

    private void WelcomePrivacy_Click(object sender, RoutedEventArgs e)
    {
        CloseOnboarding();
        Navigate(AppPage.PrivacyCenter);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await ScanAsync(showOverlay: true);
    }

    private void CloseOnboarding()
    {
        _settings.OnboardingSeen = true;
        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception exception)
        {
            ShowError(_localizer.T("SaveFailed"), exception);
        }
        Onboarding.Visibility = Visibility.Collapsed;
    }

    private async Task ScanAsync(bool showOverlay)
    {
        if (_scanCancellation is not null)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        SetScanState(isScanning: true, showOverlay);

        try
        {
            var startupItems = await Task.Run(StartupScanner.Scan, cancellation.Token);
            var snapshot = await _processScanner.ScanAsync(startupItems, cancellation.Token);
            ApplyReviewedState(snapshot);

            _snapshot = snapshot;
            StatusText.Text = $"{_localizer.T("Ready")} · {_snapshot.CapturedAt:t}";
            Navigate(_currentPage);
        }
        catch (OperationCanceledException)
        {
            // Closing the application cancels an in-progress scan.
        }
        catch (Exception exception)
        {
            StatusText.Text = _localizer.T("ScanFailed");
            ShowError(_localizer.T("ScanFailed"), exception);
        }
        finally
        {
            SetScanState(isScanning: false, showOverlay: false);
            _scanCancellation = null;
        }
    }

    private void ApplyReviewedState(SystemSnapshot snapshot)
    {
        var reviewedProcesses = _settings.ReviewedProcessKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var process in snapshot.Processes)
        {
            process.IsReviewed = reviewedProcesses.Contains(process.ReviewKey);
        }

        var reviewedStartupItems = _settings.ReviewedStartupKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var startupItem in snapshot.StartupItems)
        {
            startupItem.Reviewed = reviewedStartupItems.Contains(startupItem.ReviewKey);
        }
    }

    private void SetScanState(bool isScanning, bool showOverlay)
    {
        BusyLayer.Visibility = isScanning && showOverlay ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.IsEnabled = !isScanning;

        if (isScanning)
        {
            StatusText.Text = _localizer.T("Analyzing");
        }
    }

    private void BuildChrome()
    {
        TaglineText.Text = _localizer.T("Tagline");
        PrivacyText.Text = _localizer.T("Privacy");
        SidePrivacy.Text = _localizer.T("Privacy");
        OfflineLabel.Text = $"●  {_localizer.T("OfflineLabel")}";
        RefreshButton.Content = $"↻  {_localizer.T("Refresh")}";
        BusyText.Text = _localizer.T("Analyzing");
        StatusText.Text = _localizer.T("Ready");
        VersionText.Text = $"TaskLens {AppVersion}";

        NavPanel.Children.Clear();
        _navigationButtons.Clear();
        AddNavigationButton(AppPage.Dashboard, "Dashboard", "⌂");
        AddNavigationButton(AppPage.Processes, "Processes", "▤");
        AddNavigationButton(AppPage.Insights, "Insights", "◫");
        AddNavigationButton(AppPage.Review, "Review", "◇");
        AddNavigationButton(AppPage.Startup, "Startup", "↗");
        AddNavigationButton(AppPage.Reports, "Reports", "▧");
        AddNavigationButton(AppPage.PrivacyCenter, "PrivacyCenter", "♢");
        AddNavigationButton(AppPage.Settings, "Settings", "⚙");

        WelcomeTagline.Text = _localizer.T("Tagline");
        WelcomeBody.Text = _localizer.T("WelcomeText");
        WelcomePrivacy.Text = _localizer.T("Privacy");
        WelcomeStart.Content = _localizer.T("Start");
        WelcomeCheck.Content = _localizer.T("WhatChecked");
        WelcomePrivacyButton.Content = _localizer.T("PrivacyDetails");
    }

    private void AddNavigationButton(AppPage page, string textKey, string icon)
    {
        var button = new Button
        {
            Content = $"{icon}   {_localizer.T(textKey)}",
            Style = (Style)FindResource("NavButton"),
            Tag = page
        };

        button.Click += (_, _) => Navigate((AppPage)button.Tag);
        NavPanel.Children.Add(button);
        _navigationButtons[page] = button;
    }

    private void Navigate(AppPage page)
    {
        _currentPage = page;
        foreach (var pair in _navigationButtons)
        {
            pair.Value.Background = pair.Key == page ? Brush("#2A3D68") : Brushes.Transparent;
        }

        PageScroll.ScrollToTop();
        PageHost.Content = page switch
        {
            AppPage.Dashboard => BuildDashboardPage(),
            AppPage.Processes => BuildProcessesPage(),
            AppPage.Insights => BuildInsightsPage(),
            AppPage.Review => BuildReviewPage(),
            AppPage.Startup => BuildStartupPage(),
            AppPage.Reports => BuildReportsPage(),
            AppPage.PrivacyCenter => BuildPrivacyPage(),
            AppPage.Settings => BuildSettingsPage(),
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, null)
        };
    }

    private void MarkProcessReviewed(ProcessInfoModel process)
    {
        process.IsReviewed = true;
        AddUnique(_settings.ReviewedProcessKeys, process.ReviewKey);
        SaveReviewState();
    }

    private void MarkStartupReviewed(StartupItem startupItem)
    {
        startupItem.Reviewed = true;
        AddUnique(_settings.ReviewedStartupKeys, startupItem.ReviewKey);
        SaveReviewState();
        Navigate(AppPage.Startup);
    }

    private void SaveReviewState()
    {
        try
        {
            _settingsService.Save(_settings);
            StatusText.Text = _localizer.T("MarkedReviewed");
        }
        catch (Exception exception)
        {
            ShowError(_localizer.T("SaveFailed"), exception);
        }
    }

    private static void AddUnique(List<string> values, string value)
    {
        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }

    private void ApplyTheme()
    {
        var useDarkTheme = _settings.Theme == "Dark" ||
            (_settings.Theme == "System" && IsSystemDarkTheme());

        if (useDarkTheme)
        {
            _primaryText = Brush("#EDF2FC");
            _secondaryText = Brush("#A9B6CB");
            _surface = Brush(_settings.Glass ? "#E618243A" : "#18243A");
            _pageBackground = Brush("#0E1628");
            _border = Brush("#2B3A53");
            PrivacyBar.Background = Brush("#172B49");
            PrivacyText.Foreground = Brush("#BCD3F2");
            FooterBar.Background = Brush("#111B2E");
            FooterBar.BorderBrush = Brush("#2B3A53");
            StatusText.Foreground = _secondaryText;
            VersionText.Foreground = _secondaryText;
        }
        else
        {
            _primaryText = Brush("#18243B");
            _secondaryText = Brush("#66758D");
            _surface = Brush(_settings.Glass ? "#EDFFFFFF" : "#FFFFFFFF");
            _pageBackground = Brush("#EEF3FB");
            _border = Brush("#DCE3EF");
            PrivacyBar.Background = Brush("#DCEBFF");
            PrivacyText.Foreground = Brush("#24446F");
            FooterBar.Background = Brush("#F9FBFF");
            FooterBar.BorderBrush = Brush("#DDE4EF");
            StatusText.Foreground = Brush("#64728C");
            VersionText.Foreground = Brush("#8B96AA");
        }

        Background = _pageBackground;
        PageScroll.Background = _pageBackground;
    }

    private static bool IsSystemDarkTheme()
    {
        var value = Microsoft.Win32.Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme",
            1);
        return value is int integer && integer == 0;
    }

    private static int ClampRefreshInterval(int seconds) => Math.Clamp(seconds, 2, 30);

    private static SolidColorBrush Brush(string color) =>
        new((Color)ColorConverter.ConvertFromString(color));

    private void ShowError(string message, Exception exception)
    {
        var detail = _settings.TechnicalDetails ? $"\n\n{exception.Message}" : string.Empty;
        MessageBox.Show(this, message + detail, "TaskLens", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
