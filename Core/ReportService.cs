using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TaskLens.Core;

public sealed class ReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SettingsService _settings;

    public ReportService(SettingsService settings)
    {
        _settings = settings;
    }

    public string Export(
        SystemSnapshot snapshot,
        LocalizationService localizer,
        string format,
        bool technical,
        bool anonymized)
    {
        Directory.CreateDirectory(_settings.ReportsFolder);
        var normalizedFormat = format.ToLowerInvariant();
        var path = Path.Combine(
            _settings.ReportsFolder,
            $"TaskLens-{DateTime.Now:yyyyMMdd-HHmmss}.{normalizedFormat}");

        switch (normalizedFormat)
        {
            case "json":
                WriteJson(path, snapshot, anonymized);
                break;
            case "csv":
                WriteCsv(path, snapshot, anonymized);
                break;
            case "html":
                File.WriteAllText(path, BuildHtml(snapshot, localizer, technical, anonymized), Encoding.UTF8);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported report format.");
        }

        return path;
    }

    private static void WriteJson(string path, SystemSnapshot snapshot, bool anonymized)
    {
        var rows = snapshot.Processes.Select(process => new
        {
            process.Name,
            process.Pid,
            process.ParentPid,
            process.CpuPercent,
            MemoryMB = process.MemoryBytes / 1024 / 1024,
            process.DiskBytesPerSecond,
            Category = process.Category.ToString(),
            Safety = process.Safety.ToString(),
            process.Publisher,
            Path = Anonymize(process.Path, anonymized),
            process.Signature,
            process.Indicators
        });
        File.WriteAllText(path, JsonSerializer.Serialize(rows, JsonOptions), Encoding.UTF8);
    }

    private static void WriteCsv(string path, SystemSnapshot snapshot, bool anonymized)
    {
        var csv = new StringBuilder("Name,PID,CPU,MemoryMB,Category,Publisher,Path\r\n");
        foreach (var process in snapshot.Processes)
        {
            csv.Append(Csv(process.Name)).Append(',')
                .Append(process.Pid).Append(',')
                .Append(process.CpuPercent.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
                .Append(process.MemoryBytes / 1024 / 1024).Append(',')
                .Append(Csv(process.Category.ToString())).Append(',')
                .Append(Csv(process.Publisher)).Append(',')
                .Append(Csv(Anonymize(process.Path, anonymized)))
                .AppendLine();
        }
        File.WriteAllText(path, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string BuildHtml(
        SystemSnapshot snapshot,
        LocalizationService localizer,
        bool technical,
        bool anonymized)
    {
        var topProcesses = snapshot.Processes
            .OrderByDescending(process => process.CpuPercent)
            .Take(20);
        var reviewProcesses = snapshot.Processes
            .Where(process => process.Category == ProcessCategory.ReviewRecommended && !process.IsReviewed)
            .Take(25);
        var growingProcesses = snapshot.Processes
            .Where(process => process.PossibleMemoryGrowth)
            .Take(25);

        var header = BuildTableHeader(localizer, technical);
        var topRows = BuildRows(topProcesses, localizer, technical, anonymized);
        var reviewRows = BuildRows(reviewProcesses, localizer, technical, anonymized);
        var growingRows = BuildRows(growingProcesses, localizer, technical, anonymized);
        var startupHeader = BuildStartupHeader(localizer, technical);
        var startupRows = BuildStartupRows(snapshot.StartupItems, localizer, technical, anonymized);
        var reportCulture = CultureInfo.GetCultureInfo(localizer.Language);
        var cpuUsage = snapshot.CpuPercent.ToString("F1", reportCulture);
        var memoryUsage = snapshot.MemoryPercent.ToString("F1", reportCulture);
        var diskUsage = (snapshot.DiskBytesPerSecond / 1024).ToString("F0", reportCulture);
        var generatedAt = DateTime.Now.ToString("g", reportCulture);
        return $$"""
            <!doctype html>
            <html lang="{{Html(localizer.Language)}}">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>TaskLens</title>
              <style>
                body { font: 15px 'Segoe UI', Arial, sans-serif; margin: 40px; color: #182136; background: #f5f7fb; }
                main { max-width: 1100px; margin: auto; background: white; padding: 36px; border-radius: 18px; }
                h1 { color: #245ee5; }
                .privacy { padding: 16px; background: #eef6ff; border-radius: 12px; }
                table { border-collapse: collapse; width: 100%; margin: 14px 0 30px; }
                th, td { padding: 10px; border-bottom: 1px solid #e5e9f1; text-align: left; }
              </style>
            </head>
            <body><main>
              <h1>TaskLens</h1>
              <p class="privacy">{{Html(localizer.T("Privacy"))}}</p>
              <h2>{{Html(localizer.T("Performance"))}}</h2>
              <p>{{Html(localizer.T("Processor"))}}: {{cpuUsage}}% · {{Html(localizer.T("Memory"))}}: {{memoryUsage}}% · {{Html(localizer.T("Disk"))}}: {{diskUsage}} KB/s</p>
              <h2>{{Html(localizer.T("TopCpu"))}}</h2>
              <table>{{header}}{{topRows}}</table>
              <h2>{{Html(localizer.T("NeedsReview"))}}</h2>
              <table>{{header}}{{reviewRows}}</table>
              <h2>{{Html(localizer.T("Growing"))}}</h2>
              <table>{{header}}{{growingRows}}</table>
              <h2>{{Html(localizer.T("Startup"))}}</h2>
              <p>{{snapshot.StartupItems.Count}} {{Html(localizer.T("Items"))}}</p>
              <table>{{startupHeader}}{{startupRows}}</table>
              <h2>{{Html(localizer.T("LimitationsTitle"))}}</h2>
              <p>{{Html(localizer.T("Limitations"))}}</p>
              <p><small>{{Html(localizer.T("Generated"))}} {{generatedAt}}. {{Html(localizer.T("LocalReportNote"))}}</small></p>
            </main></body>
            </html>
            """;
    }

    private static string BuildTableHeader(LocalizationService localizer, bool technical)
    {
        var pathHeader = technical ? $"<th>{Html(localizer.T("Path"))}</th>" : string.Empty;
        return $"<tr><th>{Html(localizer.T("Name"))}</th><th>CPU</th><th>{Html(localizer.T("Memory"))}</th><th>{Html(localizer.T("Publisher"))}</th><th>{Html(localizer.T("Category"))}</th>{pathHeader}</tr>";
    }

    private static string BuildRows(
        IEnumerable<ProcessInfoModel> processes,
        LocalizationService localizer,
        bool technical,
        bool anonymized)
    {
        return string.Join(string.Empty, processes.Select(process =>
        {
            var pathCell = technical
                ? $"<td>{Html(Anonymize(process.Path, anonymized))}</td>"
                : string.Empty;
            return $"<tr><td>{Html(process.Name)}</td><td>{process.CpuPercent:F1}%</td><td>{process.MemoryText}</td><td>{Html(process.Publisher)}</td><td>{Html(LocalizeCategory(process.Category, localizer))}</td>{pathCell}</tr>";
        }));
    }

    private static string BuildStartupHeader(LocalizationService localizer, bool technical)
    {
        var commandHeader = technical ? $"<th>{Html(localizer.T("Command"))}</th>" : string.Empty;
        return $"<tr><th>{Html(localizer.T("Name"))}</th><th>{Html(localizer.T("Publisher"))}</th><th>{Html(localizer.T("Impact"))}</th><th>{Html(localizer.T("StartupLocation"))}</th>{commandHeader}</tr>";
    }

    private static string BuildStartupRows(
        IEnumerable<StartupItem> items,
        LocalizationService localizer,
        bool technical,
        bool anonymized)
    {
        return string.Join(string.Empty, items.Select(item =>
        {
            var commandCell = technical
                ? $"<td>{Html(Anonymize(item.Command, anonymized))}</td>"
                : string.Empty;
            return $"<tr><td>{Html(item.Name)}</td><td>{Html(item.Publisher)}</td><td>{Html(localizer.T(item.Impact))}</td><td>{Html(item.Location)}</td>{commandCell}</tr>";
        }));
    }

    private static string LocalizeCategory(ProcessCategory category, LocalizationService localizer) => category switch
    {
        ProcessCategory.System => localizer.T("System"),
        ProcessCategory.UserApplication => localizer.T("UserApps"),
        ProcessCategory.Background => localizer.T("Background"),
        ProcessCategory.Startup => localizer.T("Startup"),
        ProcessCategory.ResourceHeavy => localizer.T("Heavy"),
        ProcessCategory.ReviewRecommended => localizer.T("Review"),
        _ => localizer.T("Unknown")
    };

    private static string Anonymize(string value, bool enabled)
    {
        if (!enabled || string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value
            .Replace(Environment.UserName, "[USER]", StringComparison.OrdinalIgnoreCase)
            .Replace(Environment.MachineName, "[COMPUTER]", StringComparison.OrdinalIgnoreCase);
    }

    private static string Html(string value) => System.Net.WebUtility.HtmlEncode(value);

    private static string Csv(string value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
