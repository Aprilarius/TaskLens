using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using TaskLens.Core;

namespace TaskLens;

public partial class MainWindow
{
    private StackPanel CreatePage(string title, string? introduction = null)
    {
        var panel = new StackPanel();
        panel.Children.Add(CreateText(title, 30, FontWeights.SemiBold));

        if (!string.IsNullOrWhiteSpace(introduction))
        {
            panel.Children.Add(CreateText(
                introduction,
                14,
                FontWeights.Normal,
                _secondaryText,
                new Thickness(0, 7, 0, 20)));
        }

        return panel;
    }

    private TextBlock CreateText(
        string text,
        double fontSize = 14,
        FontWeight? fontWeight = null,
        System.Windows.Media.Brush? foreground = null,
        Thickness? margin = null)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = fontWeight ?? FontWeights.Normal,
            Foreground = foreground ?? _primaryText,
            Margin = margin ?? new Thickness(0)
        };
    }

    private Border CreateCard(UIElement child, Thickness? margin = null)
    {
        return new Border
        {
            Child = child,
            Background = _surface,
            BorderBrush = _border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20),
            Margin = margin ?? new Thickness(0, 0, 14, 14),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 4,
                Opacity = 0.08,
                Color = Colors.SlateGray
            }
        };
    }

    private Button CreateSecondaryButton(
        string text,
        RoutedEventHandler click,
        bool destructive = false)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource(destructive ? "DangerButton" : "QuietButton")
        };
        button.Click += click;
        return button;
    }

    private StackPanel BuildDashboardPage()
    {
        var page = CreatePage(_localizer.T("Dashboard"), _localizer.T("WelcomeText"));
        var reviewCount = _snapshot.Processes.Count(process =>
            process.Category == ProcessCategory.ReviewRecommended && !process.IsReviewed);
        var unknownCount = _snapshot.Processes.Count(process =>
            process.Category == ProcessCategory.Unknown);

        var cards = new UniformGrid { Columns = 4 };
        var status = GetPerformanceStatus(reviewCount);
        cards.Children.Add(CreateMetricCard(
            _localizer.T("Performance"),
            status,
            "◉",
            status == _localizer.T("Normal") ? "#26A875" : "#EF8D32"));
        cards.Children.Add(CreateMetricCard(_localizer.T("Processor"), $"{_snapshot.CpuPercent:N0}%", "⌁", "#2764E7"));
        cards.Children.Add(CreateMetricCard(_localizer.T("Memory"), $"{_snapshot.MemoryPercent:N0}%", "▥", "#8156D8"));
        cards.Children.Add(CreateMetricCard(_localizer.T("Disk"), FormatRate(_snapshot.DiskBytesPerSecond), "◫", "#E1698B"));
        cards.Children.Add(CreateMetricCard(_localizer.T("Network"), FormatRate(_snapshot.NetworkBytesPerSecond), "⌁", "#00A5A5"));
        cards.Children.Add(CreateMetricCard(_localizer.T("Startup"), _snapshot.StartupItems.Count.ToString(CultureInfo.CurrentCulture), "↗", "#F2A12C"));
        cards.Children.Add(CreateMetricCard(_localizer.T("Unknown"), unknownCount.ToString(CultureInfo.CurrentCulture), "?", "#66758D"));
        cards.Children.Add(CreateMetricCard(_localizer.T("NeedsReview"), reviewCount.ToString(CultureInfo.CurrentCulture), "◇", "#E45F5F"));
        page.Children.Add(cards);

        page.Children.Add(CreateSummaryCard());

        var topLists = new Grid();
        topLists.ColumnDefinitions.Add(new ColumnDefinition());
        topLists.ColumnDefinitions.Add(new ColumnDefinition());
        var cpu = CreateTopList(
            _localizer.T("TopCpu"),
            _snapshot.Processes.OrderByDescending(process => process.CpuPercent).Take(5),
            process => process.CpuText);
        var memory = CreateTopList(
            _localizer.T("TopMemory"),
            _snapshot.Processes.OrderByDescending(process => process.MemoryBytes).Take(5),
            process => process.MemoryText);
        Grid.SetColumn(memory, 1);
        topLists.Children.Add(cpu);
        topLists.Children.Add(memory);
        page.Children.Add(topLists);
        return page;
    }

    private string GetPerformanceStatus(int reviewCount)
    {
        if (_snapshot.CpuPercent > 85 || _snapshot.MemoryPercent > 92 || reviewCount > 8)
        {
            return _localizer.T("NeedsAttention");
        }

        if (_snapshot.CpuPercent > 55 || _snapshot.MemoryPercent > 80)
        {
            return _localizer.T("Busy");
        }

        return _localizer.T("Normal");
    }

    private Border CreateMetricCard(string label, string value, string icon, string accent)
    {
        var panel = new StackPanel();
        panel.Children.Add(CreateText(icon, 20, FontWeights.SemiBold, Brush(accent)));
        panel.Children.Add(CreateText(value, 25, FontWeights.SemiBold, _primaryText, new Thickness(0, 10, 0, 4)));
        panel.Children.Add(CreateText(label, 12, FontWeights.Normal, _secondaryText));
        return CreateCard(panel);
    }

    private Border CreateSummaryCard()
    {
        var busiestCpu = _snapshot.Processes
            .OrderByDescending(process => process.CpuPercent)
            .FirstOrDefault();
        var largestMemory = _snapshot.Processes
            .OrderByDescending(process => process.MemoryBytes)
            .FirstOrDefault();

        string reason;
        if (_snapshot.MemoryPercent > 82)
        {
            reason = string.Format(
                CultureInfo.CurrentCulture,
                _localizer.T("MemoryReason"),
                largestMemory?.FriendlyName ?? "—");
        }
        else if (_snapshot.CpuPercent > 60)
        {
            reason = string.Format(
                CultureInfo.CurrentCulture,
                _localizer.T("CpuReason"),
                busiestCpu?.FriendlyName ?? "—");
        }
        else
        {
            reason = _localizer.T("NoIssue");
        }

        var panel = new StackPanel();
        panel.Children.Add(CreateText(_localizer.T("MainReason"), 13, FontWeights.SemiBold, _secondaryText));
        panel.Children.Add(CreateText(reason, 22, FontWeights.SemiBold, _primaryText, new Thickness(0, 10, 0, 12)));
        panel.Children.Add(CreateText(_localizer.T("Privacy"), 12, FontWeights.Normal, Brush("#3973A8")));
        return CreateCard(panel, new Thickness(0, 8, 0, 14));
    }

    private Border CreateTopList(
        string title,
        IEnumerable<ProcessInfoModel> processes,
        Func<ProcessInfoModel, string> valueSelector)
    {
        var items = processes.ToList();
        var panel = new StackPanel();
        panel.Children.Add(CreateText(title, 17, FontWeights.SemiBold, _primaryText, new Thickness(0, 0, 0, 12)));

        foreach (var process in items)
        {
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
            var value = CreateText(valueSelector(process), 13, FontWeights.SemiBold);
            DockPanel.SetDock(value, Dock.Right);
            row.Children.Add(value);
            row.Children.Add(CreateText(process.FriendlyName, 13, FontWeights.Normal, _secondaryText));
            panel.Children.Add(row);
        }

        if (items.Count == 0)
        {
            panel.Children.Add(CreateText(_localizer.T("NoData"), 13, FontWeights.Normal, _secondaryText));
        }

        return CreateCard(panel);
    }

    private StackPanel BuildProcessesPage()
    {
        var page = CreatePage(_localizer.T("Processes"));
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        var search = new TextBox
        {
            Width = 320,
            ToolTip = _localizer.T("Search"),
            Margin = new Thickness(0, 0, 10, 8)
        };
        var filter = new ComboBox
        {
            ItemsSource = new[]
            {
                _localizer.T("All"), _localizer.T("System"), _localizer.T("UserApps"),
                _localizer.T("Background"), _localizer.T("Heavy"), _localizer.T("Review"),
                _localizer.T("Unsigned")
            },
            SelectedIndex = 0,
            Margin = new Thickness(0, 0, 10, 8)
        };
        var sort = new ComboBox
        {
            ItemsSource = new[]
            {
                _localizer.T("SortCpu"), _localizer.T("SortMemory"), _localizer.T("SortName")
            },
            SelectedIndex = 0,
            Margin = new Thickness(0, 0, 10, 8)
        };

        toolbar.Children.Add(search);
        toolbar.Children.Add(filter);
        toolbar.Children.Add(sort);
        page.Children.Add(toolbar);

        var grid = CreateProcessGrid(_snapshot.Processes);
        page.Children.Add(CreateCard(grid, new Thickness(0, 0, 0, 10)));

        void UpdateGrid()
        {
            IEnumerable<ProcessInfoModel> processes = _snapshot.Processes;
            if (!string.IsNullOrWhiteSpace(search.Text))
            {
                processes = processes.Where(process =>
                    process.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) ||
                    process.Publisher.Contains(search.Text, StringComparison.OrdinalIgnoreCase) ||
                    process.Path.Contains(search.Text, StringComparison.OrdinalIgnoreCase));
            }

            processes = filter.SelectedIndex switch
            {
                1 => processes.Where(process => process.Category == ProcessCategory.System),
                2 => processes.Where(process => process.Category == ProcessCategory.UserApplication),
                3 => processes.Where(process => process.Category == ProcessCategory.Background),
                4 => processes.Where(process => process.Category == ProcessCategory.ResourceHeavy),
                5 => processes.Where(process => process.Category == ProcessCategory.ReviewRecommended && !process.IsReviewed),
                6 => processes.Where(process => process.Signature == "Unsigned"),
                _ => processes
            };

            processes = sort.SelectedIndex switch
            {
                1 => processes.OrderByDescending(process => process.MemoryBytes),
                2 => processes.OrderBy(process => process.Name),
                _ => processes.OrderByDescending(process => process.CpuPercent)
            };
            grid.ItemsSource = processes.ToList();
        }

        search.TextChanged += (_, _) => UpdateGrid();
        filter.SelectionChanged += (_, _) => UpdateGrid();
        sort.SelectionChanged += (_, _) => UpdateGrid();
        return page;
    }

    private DataGrid CreateProcessGrid(IEnumerable<ProcessInfoModel> source)
    {
        var grid = CreateDataGrid(source);
        grid.MinHeight = 430;
        grid.MaxHeight = 650;
        grid.Columns.Add(TextColumn(_localizer.T("Name"), "FriendlyName", 2));
        grid.Columns.Add(TextColumn("PID", "Pid", 70));
        grid.Columns.Add(TextColumn("CPU", "CpuText", 75));
        grid.Columns.Add(TextColumn(_localizer.T("Memory"), "MemoryText", 100));
        grid.Columns.Add(TextColumn(_localizer.T("Disk"), "DiskText", 95));
        grid.Columns.Add(TextColumn(_localizer.T("Publisher"), "Publisher", 1.4));
        grid.MouseDoubleClick += (_, _) =>
        {
            if (grid.SelectedItem is ProcessInfoModel process)
            {
                ShowProcessDetails(process);
            }
        };
        return grid;
    }

    private DataGrid CreateDataGrid(IEnumerable<object> source)
    {
        var grid = new DataGrid
        {
            ItemsSource = source,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = _border,
            BorderThickness = new Thickness(0),
            RowHeight = 48,
            ColumnHeaderHeight = 40,
            SelectionMode = DataGridSelectionMode.Single,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true
        };
        ApplyDataGridTheme(grid);
        return grid;
    }

    private static DataGridTextColumn TextColumn(string header, string property, double width)
    {
        var dataGridWidth = width > 10
            ? new DataGridLength(width)
            : new DataGridLength(width, DataGridLengthUnitType.Star);
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(property),
            Width = dataGridWidth
        };
    }

    private void ApplyDataGridTheme(DataGrid grid)
    {
        grid.RowBackground = _surface;
        grid.AlternatingRowBackground = _pageBackground;
        grid.AlternationCount = 2;
        grid.Foreground = _primaryText;
        grid.Background = _surface;

        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, _primaryText));
        cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 4, 7, 4)));
        var selectedCell = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selectedCell.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#315B9D")));
        selectedCell.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        cellStyle.Triggers.Add(selectedCell);
        grid.CellStyle = cellStyle;

        var headerStyle = new Style(typeof(DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, _pageBackground));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, _primaryText));
        headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, _border));
        headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        grid.ColumnHeaderStyle = headerStyle;

        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, _primaryText));
        rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, _surface));
        var alternateRow = new Trigger { Property = ItemsControl.AlternationIndexProperty, Value = 1 };
        alternateRow.Setters.Add(new Setter(Control.BackgroundProperty, _pageBackground));
        rowStyle.Triggers.Add(alternateRow);
        grid.RowStyle = rowStyle;
    }

    private void ShowProcessDetails(ProcessInfoModel process)
    {
        var dialog = new Window
        {
            Title = $"TaskLens · {process.FriendlyName}",
            Owner = this,
            Width = 760,
            Height = 680,
            MinWidth = 650,
            MinHeight = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = _pageBackground
        };
        var page = CreatePage(process.FriendlyName, $"{process.Name}.exe · PID {process.Pid}");
        page.Margin = new Thickness(24);

        var information = new StackPanel();
        information.Children.Add(CreateText(_localizer.T("Explanation"), 17, FontWeights.SemiBold));
        information.Children.Add(CreateText(
            _localizer.T(process.ExplanationKey),
            14,
            FontWeights.Normal,
            _secondaryText,
            new Thickness(0, 8, 0, 18)));
        AddField(information, _localizer.T("Publisher"), process.Publisher);
        AddField(information, _localizer.T("Category"), LocalizeCategory(process.Category));
        AddField(information, _localizer.T("Safety"), LocalizeSafety(process.Safety));
        AddField(information, $"CPU / {_localizer.T("Memory")}", $"{process.CpuText} / {process.MemoryText}");
        AddField(information, _localizer.T("Signature"), LocalizeSignature(process.Signature));

        if (_settings.TechnicalDetails)
        {
            AddField(information, _localizer.T("Path"), ValueOrUnavailable(process.Path));
            AddField(information, _localizer.T("Started"), process.StartTime?.ToString("g", CultureInfo.CurrentCulture) ?? _localizer.T("Unavailable"));
            AddField(information, _localizer.T("Parent"), process.ParentPid == 0 ? _localizer.T("Unavailable") : process.ParentPid.ToString(CultureInfo.CurrentCulture));
        }
        page.Children.Add(CreateCard(information, new Thickness(0, 0, 0, 14)));

        if (process.Indicators.Count > 0)
        {
            page.Children.Add(CreateIndicatorsCard(process));
        }

        var actions = new WrapPanel();
        actions.Children.Add(CreateSecondaryButton(_localizer.T("OpenLocation"), (_, _) => OpenProcessLocation(process)));
        actions.Children.Add(CreateSecondaryButton(_localizer.T("OpenTaskManager"), (_, _) => OpenTaskManager()));
        actions.Children.Add(CreateSecondaryButton(_localizer.T("CopyDetails"), (_, _) => CopyProcessDetails(process)));
        actions.Children.Add(CreateSecondaryButton(_localizer.T("MarkReviewed"), (_, _) => MarkProcessReviewed(process)));
        var endButton = CreateSecondaryButton(_localizer.T("EndProcess"), (_, _) => EndProcess(process, dialog), destructive: true);
        endButton.IsEnabled = process.Safety != SafetyLevel.Critical && !ProcessScanner.IsCritical(process.Name);
        actions.Children.Add(endButton);
        page.Children.Add(actions);

        dialog.Content = new ScrollViewer
        {
            Content = page,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        dialog.ShowDialog();
    }

    private Border CreateIndicatorsCard(ProcessInfoModel process)
    {
        var panel = new StackPanel();
        panel.Children.Add(CreateText(_localizer.T("Indicators"), 17, FontWeights.SemiBold, _primaryText, new Thickness(0, 0, 0, 8)));
        foreach (var indicator in process.Indicators)
        {
            panel.Children.Add(CreateText($"• {_localizer.T(indicator)}", 13, FontWeights.Normal, Brush("#D87942"), new Thickness(0, 3, 0, 3)));
        }
        panel.Children.Add(CreateText(_localizer.T("ReviewNotice"), 12, FontWeights.Normal, _secondaryText, new Thickness(0, 12, 0, 0)));
        return CreateCard(panel, new Thickness(0, 0, 0, 14));
    }

    private void AddField(Panel panel, string label, string value)
    {
        panel.Children.Add(CreateText(label.ToUpperInvariant(), 10, FontWeights.SemiBold, _secondaryText, new Thickness(0, 8, 0, 2)));
        panel.Children.Add(CreateText(value, 13, FontWeights.Normal, _primaryText, new Thickness(0, 0, 0, 5)));
    }

    private void OpenProcessLocation(ProcessInfoModel process)
    {
        if (string.IsNullOrWhiteSpace(process.Path) || !File.Exists(process.Path))
        {
            MessageBox.Show(this, _localizer.T("Unavailable"), "TaskLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{process.Path}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowError(_localizer.T("OpenFailed"), exception);
        }
    }

    private void OpenTaskManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowError(_localizer.T("OpenFailed"), exception);
        }
    }

    private void CopyProcessDetails(ProcessInfoModel process)
    {
        var details = string.Join(Environment.NewLine,
            $"{process.Name} (PID {process.Pid})",
            process.Publisher,
            process.Path,
            $"CPU {process.CpuText}, {_localizer.T("Memory")} {process.MemoryText}",
            LocalizeSafety(process.Safety));
        Clipboard.SetText(details);
        StatusText.Text = _localizer.T("Copied");
    }

    private void EndProcess(ProcessInfoModel process, Window detailsDialog)
    {
        if (process.Safety == SafetyLevel.Critical || ProcessScanner.IsCritical(process.Name))
        {
            MessageBox.Show(this, _localizer.T("CriticalBlocked"), "TaskLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!ShowEndProcessConfirmation(process))
        {
            return;
        }

        try
        {
            using var runningProcess = Process.GetProcessById(process.Pid);
            runningProcess.Kill();
            detailsDialog.Close();
            _ = ScanAsync(showOverlay: false);
        }
        catch (Exception exception)
        {
            ShowError(_localizer.T("EndFailed"), exception);
        }
    }

    private bool ShowEndProcessConfirmation(ProcessInfoModel process)
    {
        var confirmed = false;
        var dialog = new Window
        {
            Title = _localizer.T("EndProcess"),
            Owner = this,
            Width = 510,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = _pageBackground
        };
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(CreateText(process.FriendlyName, 22, FontWeights.SemiBold));
        panel.Children.Add(CreateText(_localizer.T("UsuallySafe"), 14, FontWeights.Normal, _secondaryText, new Thickness(0, 10, 0, 18)));

        var acknowledgment = new CheckBox
        {
            Content = _localizer.T("EndWarning"),
            Foreground = _primaryText,
            Margin = new Thickness(0, 0, 0, 20)
        };
        panel.Children.Add(acknowledgment);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = CreateSecondaryButton(_localizer.T("Cancel"), (_, _) => dialog.Close());
        var end = CreateSecondaryButton(_localizer.T("EndProcess"), (_, _) =>
        {
            confirmed = true;
            dialog.Close();
        }, destructive: true);
        end.IsEnabled = false;
        acknowledgment.Checked += (_, _) => end.IsEnabled = true;
        acknowledgment.Unchecked += (_, _) => end.IsEnabled = false;
        buttons.Children.Add(cancel);
        buttons.Children.Add(end);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.ShowDialog();
        return confirmed;
    }

    private StackPanel BuildInsightsPage()
    {
        var page = CreatePage(_localizer.T("Insights"));
        var grid = new UniformGrid { Columns = 2 };
        grid.Children.Add(CreateTopList(_localizer.T("TopCpu"), _snapshot.Processes.OrderByDescending(process => process.CpuPercent).Take(8), process => process.CpuText));
        grid.Children.Add(CreateTopList(_localizer.T("TopMemory"), _snapshot.Processes.OrderByDescending(process => process.MemoryBytes).Take(8), process => process.MemoryText));
        grid.Children.Add(CreateTopList(_localizer.T("TopDisk"), _snapshot.Processes.OrderByDescending(process => process.DiskBytesPerSecond).Take(8), process => process.DiskText));
        grid.Children.Add(CreateTopList(_localizer.T("Growing"), _snapshot.Processes.Where(process => process.PossibleMemoryGrowth).Take(8), process => process.MemoryText));
        page.Children.Add(grid);
        return page;
    }

    private StackPanel BuildReviewPage()
    {
        var page = CreatePage(_localizer.T("Review"), _localizer.T("ReviewNotice"));
        var processes = _snapshot.Processes
            .Where(process => !process.IsReviewed &&
                (process.Category == ProcessCategory.ReviewRecommended || process.Indicators.Count > 0))
            .OrderByDescending(process => process.Indicators.Count)
            .ToList();

        page.Children.Add(processes.Count == 0
            ? CreateCard(CreateText(_localizer.T("NoReview"), 15, FontWeights.Normal, _secondaryText), new Thickness(0))
            : CreateCard(CreateProcessGrid(processes), new Thickness(0)));
        return page;
    }

    private StackPanel BuildStartupPage()
    {
        var page = CreatePage(_localizer.T("Startup"), _localizer.T("StartupIntro"));
        var actions = new WrapPanel();
        actions.Children.Add(CreateSecondaryButton(_localizer.T("OpenStartupSettings"), (_, _) => OpenStartupSettings()));
        var markReviewed = new Button
        {
            Content = _localizer.T("MarkReviewed"),
            Style = (Style)FindResource("QuietButton")
        };
        actions.Children.Add(markReviewed);
        page.Children.Add(actions);

        var grid = CreateDataGrid(_snapshot.StartupItems);
        grid.MinHeight = 400;
        grid.Columns.Add(TextColumn(_localizer.T("Name"), "Name", 1.2));
        grid.Columns.Add(TextColumn(_localizer.T("Publisher"), "Publisher", 1.2));
        grid.Columns.Add(TextColumn(_localizer.T("StartupLocation"), "Location", 1.2));
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = _localizer.T("Impact"),
            Binding = new Binding("Impact") { Converter = new LocalizationKeyConverter(_localizer) },
            Width = 110
        });
        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = _localizer.T("Reviewed"),
            Binding = new Binding("Reviewed"),
            Width = 90
        });
        grid.Columns.Add(TextColumn(_localizer.T("Command"), "Command", 2));
        markReviewed.Click += (_, _) =>
        {
            if (grid.SelectedItem is StartupItem item)
            {
                MarkStartupReviewed(item);
            }
            else
            {
                MessageBox.Show(this, _localizer.T("SelectItem"), "TaskLens", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        page.Children.Add(CreateCard(grid, new Thickness(0, 12, 0, 0)));
        return page;
    }

    private void OpenStartupSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowError(_localizer.T("OpenFailed"), exception);
        }
    }

    private StackPanel BuildReportsPage()
    {
        var page = CreatePage(_localizer.T("Reports"), _localizer.T("ReportIntro"));
        var mode = new ComboBox
        {
            ItemsSource = new[]
            {
                _localizer.T("SimpleReport"),
                _localizer.T("TechnicalReport"),
                _localizer.T("AnonymizedReport")
            },
            SelectedIndex = _settings.AnonymizeReports ? 2 : 0,
            Margin = new Thickness(0, 0, 0, 16),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        page.Children.Add(mode);

        var buttons = new WrapPanel();
        buttons.Children.Add(CreateSecondaryButton(_localizer.T("Html"), (_, _) => ExportReport("html", mode.SelectedIndex)));
        buttons.Children.Add(CreateSecondaryButton(_localizer.T("Json"), (_, _) => ExportReport("json", mode.SelectedIndex)));
        buttons.Children.Add(CreateSecondaryButton(_localizer.T("Csv"), (_, _) => ExportReport("csv", mode.SelectedIndex)));
        buttons.Children.Add(CreateSecondaryButton(_localizer.T("OpenReportFolder"), (_, _) => OpenReportsFolder()));
        page.Children.Add(buttons);
        page.Children.Add(CreateCard(CreateText(_localizer.T("Limitations"), 14, FontWeights.Normal, _secondaryText), new Thickness(0, 18, 0, 0)));
        return page;
    }

    private void ExportReport(string format, int mode)
    {
        if (_snapshot.Processes.Count == 0)
        {
            MessageBox.Show(this, _localizer.T("NoData"), "TaskLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var path = _reportService.Export(_snapshot, _localizer, format, technical: mode == 1, anonymized: mode == 2);
            StatusText.Text = $"{_localizer.T("ReportCreated")} {path}";
            MessageBox.Show(this, $"{_localizer.T("ReportCreated")}\n{path}", "TaskLens", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            ShowError(_localizer.T("ExportFailed"), exception);
        }
    }

    private void OpenReportsFolder()
    {
        try
        {
            Directory.CreateDirectory(_settingsService.ReportsFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", _settingsService.ReportsFolder) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowError(_localizer.T("OpenFailed"), exception);
        }
    }

    private StackPanel BuildPrivacyPage()
    {
        var page = CreatePage(_localizer.T("PrivacyCenter"), _localizer.T("Privacy"));
        page.Children.Add(CreateCard(BuildPrivacyTable(), new Thickness(0, 0, 0, 14)));
        page.Children.Add(CreateCard(
            CreateText(_localizer.T("Offline"), 14, FontWeights.SemiBold, Brush("#23825D")),
            new Thickness(0, 0, 0, 14)));

        var buttons = new WrapPanel();
        buttons.Children.Add(CreateSecondaryButton(_localizer.T("DeleteReports"), (_, _) => DeleteReports(), destructive: true));
        buttons.Children.Add(CreateSecondaryButton(_localizer.T("OpenReportFolder"), (_, _) => OpenReportsFolder()));
        buttons.Children.Add(CreateSecondaryButton(_localizer.T("ExportPrivacy"), (_, _) => ExportPrivacyStatement()));
        page.Children.Add(buttons);
        return page;
    }

    private Grid BuildPrivacyTable()
    {
        var rows = new[]
        {
            new[] { _localizer.T("DataType"), _localizer.T("Used"), _localizer.T("Stored"), _localizer.T("Sent") },
            new[] { _localizer.T("ProcessList"), _localizer.T("Yes"), _localizer.T("LocalOnly"), _localizer.T("No") },
            new[] { "CPU / Memory / Disk", _localizer.T("Yes"), _localizer.T("LocalOnly"), _localizer.T("No") },
            new[] { _localizer.T("PersonalFiles"), _localizer.T("No"), _localizer.T("No"), _localizer.T("No") },
            new[] { _localizer.T("Passwords"), _localizer.T("No"), _localizer.T("No"), _localizer.T("No") },
            new[] { _localizer.T("BrowserHistory"), _localizer.T("No"), _localizer.T("No"), _localizer.T("No") },
            new[] { _localizer.T("Reports"), _localizer.T("Yes"), _localizer.T("LocalOnly"), _localizer.T("No") },
            new[] { _localizer.T("Telemetry"), _localizer.T("No"), _localizer.T("No"), _localizer.T("No") }
        };

        var table = new Grid();
        for (var column = 0; column < 4; column++)
        {
            table.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (var row = 0; row < rows.Length; row++)
        {
            table.RowDefinitions.Add(new RowDefinition());
            for (var column = 0; column < 4; column++)
            {
                var cell = new Border
                {
                    BorderBrush = _border,
                    BorderThickness = new Thickness(column == 0 ? 1 : 0, row == 0 ? 1 : 0, 1, 1),
                    Padding = new Thickness(12),
                    Background = row == 0 ? _pageBackground : _surface,
                    Child = CreateText(
                        rows[row][column],
                        13,
                        row == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                        row == 0 ? _primaryText : _secondaryText)
                };
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                table.Children.Add(cell);
            }
        }
        return table;
    }

    private void DeleteReports()
    {
        if (!Directory.Exists(_settingsService.ReportsFolder))
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"{_localizer.T("DeleteReports")}?",
            "TaskLens",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            foreach (var report in Directory.GetFiles(_settingsService.ReportsFolder, "TaskLens-*"))
            {
                File.Delete(report);
            }
            StatusText.Text = _localizer.T("ReportsDeleted");
        }
        catch (Exception exception)
        {
            ShowError(_localizer.T("DeleteFailed"), exception);
        }
    }

    private void ExportPrivacyStatement()
    {
        try
        {
            Directory.CreateDirectory(_settingsService.ReportsFolder);
            var path = Path.Combine(_settingsService.ReportsFolder, "TaskLens-Privacy-Statement.txt");
            var content = string.Join(Environment.NewLine + Environment.NewLine,
                "TaskLens",
                _localizer.T("Privacy"),
                _localizer.T("Offline"),
                _localizer.T("Limitations"));
            File.WriteAllText(path, content);
            StatusText.Text = path;
        }
        catch (Exception exception)
        {
            ShowError(_localizer.T("ExportFailed"), exception);
        }
    }

    private StackPanel BuildSettingsPage()
    {
        var page = CreatePage(_localizer.T("Settings"));
        var form = new StackPanel { MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Left };
        var language = new ComboBox
        {
            ItemsSource = new[] { "English", "Русский", "Azərbaycan dili" },
            SelectedIndex = _localizer.Language == "ru" ? 1 : _localizer.Language == "az" ? 2 : 0
        };
        var theme = new ComboBox
        {
            ItemsSource = new[] { _localizer.T("SystemTheme"), _localizer.T("Light"), _localizer.T("Dark") },
            SelectedIndex = _settings.Theme == "Light" ? 1 : _settings.Theme == "Dark" ? 2 : 0
        };
        var intervals = new[] { 2, 3, 5, 10, 30 };
        var refreshInterval = new ComboBox
        {
            ItemsSource = intervals.Select(seconds => $"{seconds} s").ToArray(),
            SelectedIndex = Math.Max(0, Array.IndexOf(intervals, _settings.RefreshSeconds))
        };
        var glass = CheckBox(_localizer.T("Glass"), _settings.Glass, new Thickness(0, 12, 0, 8));
        var technical = CheckBox(_localizer.T("Technical"), _settings.TechnicalDetails, new Thickness(0, 8, 0, 8));
        var anonymize = CheckBox(_localizer.T("AnonymizeDefault"), _settings.AnonymizeReports, new Thickness(0, 8, 0, 18));

        AddFormField(form, _localizer.T("Language"), language);
        AddFormField(form, _localizer.T("Theme"), theme);
        AddFormField(form, _localizer.T("RefreshInterval"), refreshInterval);
        form.Children.Add(glass);
        form.Children.Add(technical);
        form.Children.Add(anonymize);

        var buttons = new WrapPanel();
        buttons.Children.Add(CreateSecondaryButton(_localizer.T("Save"), (_, _) =>
            SaveSettings(language, theme, refreshInterval, intervals, glass, technical, anonymize)));
        buttons.Children.Add(CreateSecondaryButton(_localizer.T("Reset"), (_, _) => ResetSettings(), destructive: true));
        form.Children.Add(buttons);

        var about = new StackPanel();
        about.Children.Add(CreateText($"TaskLens {AppVersion}", 18, FontWeights.SemiBold));
        about.Children.Add(CreateText(_localizer.T("Limitations"), 13, FontWeights.Normal, _secondaryText, new Thickness(0, 8, 0, 0)));
        form.Children.Add(CreateCard(about, new Thickness(0, 20, 0, 0)));
        page.Children.Add(form);
        return page;
    }

    private CheckBox CheckBox(string text, bool isChecked, Thickness margin) => new()
    {
        Content = text,
        IsChecked = isChecked,
        Margin = margin,
        Foreground = _primaryText
    };

    private void AddFormField(Panel panel, string label, Control control)
    {
        panel.Children.Add(CreateText(label, 12, FontWeights.SemiBold, _secondaryText, new Thickness(0, 0, 0, 5)));
        control.Margin = new Thickness(0, 0, 0, 14);
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.Children.Add(control);
    }

    private void SaveSettings(
        ComboBox language,
        ComboBox theme,
        ComboBox refreshInterval,
        int[] intervals,
        CheckBox glass,
        CheckBox technical,
        CheckBox anonymize)
    {
        try
        {
            _settings.Language = language.SelectedIndex == 1 ? "ru" : language.SelectedIndex == 2 ? "az" : "en";
            _settings.Theme = theme.SelectedIndex == 1 ? "Light" : theme.SelectedIndex == 2 ? "Dark" : "System";
            _settings.RefreshSeconds = intervals[Math.Max(0, refreshInterval.SelectedIndex)];
            _settings.Glass = glass.IsChecked == true;
            _settings.TechnicalDetails = technical.IsChecked == true;
            _settings.AnonymizeReports = anonymize.IsChecked == true;
            _settingsService.Save(_settings);

            _localizer.SetLanguage(_settings.Language);
            _refreshTimer.Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds);
            ApplyTheme();
            BuildChrome();
            StatusText.Text = _localizer.T("Saved");
            Navigate(AppPage.Settings);
        }
        catch (Exception exception)
        {
            ShowError(_localizer.T("SaveFailed"), exception);
        }
    }

    private void ResetSettings()
    {
        var confirmation = MessageBox.Show(
            this,
            $"{_localizer.T("Reset")}?",
            "TaskLens",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _settingsService.Reset();
            _settings.Language = "en";
            _settings.Theme = "System";
            _settings.Glass = true;
            _settings.RefreshSeconds = 3;
            _settings.TechnicalDetails = false;
            _settings.AnonymizeReports = true;
            _settings.ReviewedProcessKeys.Clear();
            _settings.ReviewedStartupKeys.Clear();
            _localizer.SetLanguage("en");
            _refreshTimer.Interval = TimeSpan.FromSeconds(3);
            ApplyTheme();
            BuildChrome();
            StatusText.Text = _localizer.T("SettingsReset");
            Navigate(AppPage.Settings);
        }
        catch (Exception exception)
        {
            ShowError(_localizer.T("ResetFailed"), exception);
        }
    }

    private string LocalizeCategory(ProcessCategory category) => category switch
    {
        ProcessCategory.System => _localizer.T("System"),
        ProcessCategory.UserApplication => _localizer.T("UserApps"),
        ProcessCategory.Background => _localizer.T("Background"),
        ProcessCategory.Startup => _localizer.T("Startup"),
        ProcessCategory.ResourceHeavy => _localizer.T("Heavy"),
        ProcessCategory.ReviewRecommended => _localizer.T("Review"),
        _ => _localizer.T("Unknown")
    };

    private string LocalizeSafety(SafetyLevel safety) => _localizer.T(safety switch
    {
        SafetyLevel.Safe => "Safe",
        SafetyLevel.UsuallySafe => "UsuallySafe",
        SafetyLevel.NotRecommended => "NotRecommended",
        SafetyLevel.Critical => "Critical",
        _ => "ReviewFirst"
    });

    private string LocalizeSignature(string signature) => signature switch
    {
        "Signed" => _localizer.T("Signed"),
        "Unsigned" => _localizer.T("UnsignedStatus"),
        _ => _localizer.T("Unavailable")
    };

    private string ValueOrUnavailable(string value) =>
        string.IsNullOrWhiteSpace(value) ? _localizer.T("Unavailable") : value;

    private static string FormatRate(double bytesPerSecond) => bytesPerSecond switch
    {
        < 1024 => "—",
        < 1024 * 1024 => $"{bytesPerSecond / 1024:N0} KB/s",
        _ => $"{bytesPerSecond / 1024 / 1024:N1} MB/s"
    };
}

internal sealed class LocalizationKeyConverter : IValueConverter
{
    private readonly LocalizationService _localizer;

    public LocalizationKeyConverter(LocalizationService localizer)
    {
        _localizer = localizer;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key ? _localizer.T(key) : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
