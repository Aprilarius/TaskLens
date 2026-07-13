namespace TaskLens.Core;

public enum ProcessCategory { System, UserApplication, Background, Startup, ResourceHeavy, ReviewRecommended, Unknown }
public enum SafetyLevel { Safe, UsuallySafe, NotRecommended, Critical, ReviewFirst }

public sealed class ProcessInfoModel
{
    public int Pid { get; set; }
    public int ParentPid { get; set; }
    public string Name { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Path { get; set; } = "";
    public string Signature { get; set; } = "Unavailable";
    public string ExplanationKey { get; set; } = "Process_Generic";
    public ProcessCategory Category { get; set; }
    public SafetyLevel Safety { get; set; }
    public double CpuPercent { get; set; }
    public double DiskBytesPerSecond { get; set; }
    public long MemoryBytes { get; set; }
    public DateTime? StartTime { get; set; }
    public bool IsStartup { get; set; }
    public bool IsReviewed { get; set; }
    public bool PossibleMemoryGrowth { get; set; }
    public List<string> Indicators { get; set; } = [];
    public string ReviewKey => string.IsNullOrWhiteSpace(Path)
        ? Name.ToLowerInvariant()
        : Path.ToLowerInvariant();
    public string MemoryText => MemoryBytes < 0 ? "—" : $"{MemoryBytes / 1024d / 1024d:N0} MB";
    public string CpuText => $"{CpuPercent:N1}%";
    public string DiskText => DiskBytesPerSecond < 1024 ? "—" : $"{DiskBytesPerSecond / 1024d:N0} KB/s";
}

public sealed class StartupItem
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Location { get; set; } = "";
    public string Publisher { get; set; } = "Unknown";
    public string Impact { get; set; } = "Unknown";
    public bool Reviewed { get; set; }
    public string ReviewKey => $"{Location}|{Command}".ToLowerInvariant();
}

public sealed class SystemSnapshot
{
    public List<ProcessInfoModel> Processes { get; set; } = [];
    public List<StartupItem> StartupItems { get; set; } = [];
    public double CpuPercent { get; set; }
    public double MemoryPercent { get; set; }
    public double DiskBytesPerSecond { get; set; }
    public double NetworkBytesPerSecond { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.Now;
}

public sealed class AppSettings
{
    public string Language { get; set; } = "";
    public string Theme { get; set; } = "System";
    public bool Glass { get; set; } = true;
    public int RefreshSeconds { get; set; } = 3;
    public bool TechnicalDetails { get; set; }
    public bool AnonymizeReports { get; set; } = true;
    public bool OnboardingSeen { get; set; }
    public List<string> ReviewedProcessKeys { get; set; } = [];
    public List<string> ReviewedStartupKeys { get; set; } = [];
}
