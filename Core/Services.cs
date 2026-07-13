using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace TaskLens.Core;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string AppFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskLens");
    public string ReportsFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TaskLens Reports");
    private string SettingsPath => Path.Combine(AppFolder, "settings.json");
    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings value)
    {
        Directory.CreateDirectory(AppFolder);
        var temporaryPath = SettingsPath + ".tmp";
        var json = JsonSerializer.Serialize(value, JsonOptions);
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, SettingsPath, true);
    }

    public void Reset()
    {
        if (File.Exists(SettingsPath))
        {
            File.Delete(SettingsPath);
        }
    }
}

public sealed class StartupScanner
{
    public static List<StartupItem> Scan()
    {
        var list = new List<StartupItem>();
        ReadKey(list, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Current user registry");
        ReadKey(list, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "All users registry");
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Current user Startup folder");
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "All users Startup folder");
        return list
            .GroupBy(item => item.ReviewKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Name)
            .ToList();
    }
    private static void ReadKey(List<StartupItem> list, RegistryKey root, string path, string location)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            if (key is null)
            {
                return;
            }

            foreach (var name in key.GetValueNames())
            {
                list.Add(Create(
                    name,
                    Convert.ToString(key.GetValue(name), CultureInfo.InvariantCulture) ?? string.Empty,
                    location));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Startup data from this registry hive is unavailable without elevation.
        }
        catch (System.Security.SecurityException)
        {
            // Registry policy can block access on managed computers.
        }
    }
    private static void ReadFolder(List<StartupItem> list, string path, string location)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(path))
            {
                list.Add(Create(Path.GetFileNameWithoutExtension(file), file, location));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Startup folders can be restricted by local policy.
        }
        catch (IOException)
        {
            // A shortcut may be removed while the folder is being enumerated.
        }
    }
    private static StartupItem Create(string name, string command, string location)
    {
        var exe = ExtractExecutable(command);
        var publisher = ProcessScanner.GetPublisher(exe);
        return new StartupItem
        {
            Name = name,
            Command = command,
            Location = location,
            Publisher = string.IsNullOrWhiteSpace(publisher) ? "Unknown" : publisher,
            Impact = EstimateImpact(name, command, publisher)
        };
    }

    private static string EstimateImpact(string name, string command, string publisher)
    {
        var combined = $"{name} {command} {publisher}";
        if (combined.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("SecurityHealth", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Windows", StringComparison.OrdinalIgnoreCase))
        {
            return "Low";
        }

        if (combined.Contains("update", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("launcher", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("steam", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("discord", StringComparison.OrdinalIgnoreCase))
        {
            return "Medium";
        }

        return string.IsNullOrWhiteSpace(publisher) ? "High" : "Medium";
    }
    private static string ExtractExecutable(string command)
    {
        var value = Environment.ExpandEnvironmentVariables(command.Trim());
        if (value.StartsWith('"')) { var end = value.IndexOf('"', 1); return end > 1 ? value[1..end] : value.Trim('"'); }
        var exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); return exe >= 0 ? value[..(exe + 4)] : value;
    }
}

public sealed class ProcessScanner
{
    private readonly Dictionary<int, Sample> _previous = [];
    private readonly Dictionary<int, Queue<long>> _memoryHistory = [];
    private readonly Dictionary<string, (string Signature, string Publisher)> _signatureCache = new(StringComparer.OrdinalIgnoreCase);
    private ulong _lastIdle, _lastKernel, _lastUser;
    private long _lastNetwork;
    private DateTime _lastTime = DateTime.UtcNow;
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase) { "idle", "system", "registry", "memory compression", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "fontdrvhost", "secure system" };
    private static readonly HashSet<string> KnownApps = new(StringComparer.OrdinalIgnoreCase) { "chrome", "msedge", "firefox", "discord", "steam", "onedrive", "teams", "spotify", "telegram", "epicgameslauncher", "battle.net", "riotclientservices" };
    private static readonly HashSet<string> KnownWindows = new(StringComparer.OrdinalIgnoreCase) { "explorer", "svchost", "dwm", "lsass", "winlogon", "csrss", "services", "spoolsv", "searchindexer", "msmpeng", "runtimebroker", "shellexperiencehost", "startmenuexperiencehost", "smss", "wininit", "sihost", "taskhostw" };

    public async Task<SystemSnapshot> ScanAsync(List<StartupItem> startup, CancellationToken token) => await Task.Run(() => Scan(startup, token), token);

    private SystemSnapshot Scan(List<StartupItem> startup, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        var seconds = Math.Max(.2, (now - _lastTime).TotalSeconds);
        var parents = GetParents();
        var startupCommands = startup.Select(x => x.Command).ToArray();
        var current = new Dictionary<int, Sample>();
        var result = new List<ProcessInfoModel>();
        foreach (var p in Process.GetProcesses())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var name = p.ProcessName;
                long mem = Safe(() => p.WorkingSet64, -1L);
                var cpuTime = Safe(() => p.TotalProcessorTime, TimeSpan.Zero);
                var io = Safe(() => GetIo(p.Handle), 0L);
                current[p.Id] = new(cpuTime, io, mem);
                _previous.TryGetValue(p.Id, out var old);
                var cpu = old == null ? 0 : Math.Max(0, (cpuTime - old.Cpu).TotalSeconds / seconds / Environment.ProcessorCount * 100);
                var disk = old == null ? 0 : Math.Max(0, (io - old.Io) / seconds);
                var path = Safe(() => p.MainModule?.FileName ?? "", "");
                var friendly = Safe(() => p.MainModule?.FileVersionInfo.FileDescription ?? "", "");
                var start = Safe<DateTime?>(() => p.StartTime, null);
                var signed = Signature(path);
                var isStartup = startupCommands.Any(c => !string.IsNullOrWhiteSpace(path) && c.Contains(path, StringComparison.OrdinalIgnoreCase)) || startup.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                var model = new ProcessInfoModel { Pid = p.Id, ParentPid = parents.GetValueOrDefault(p.Id), Name = name, FriendlyName = string.IsNullOrWhiteSpace(friendly) ? name : friendly, Path = path, Publisher = signed.Publisher, Signature = signed.Signature, CpuPercent = Math.Min(cpu, 100), DiskBytesPerSecond = disk, MemoryBytes = mem, StartTime = start, IsStartup = isStartup, ExplanationKey = KnownWindows.Contains(name) || KnownApps.Contains(name) ? "Process_" + name.ToLowerInvariant() : "Process_Generic" };
                TrackMemory(model);
                Classify(model);
                result.Add(model);
            }
            catch (Win32Exception)
            {
                // Protected processes expose only the fields Windows permits.
            }
            catch (InvalidOperationException)
            {
                // The process exited between enumeration and inspection.
            }
            finally
            {
                p.Dispose();
            }
        }
        _previous.Clear();
        foreach (var sample in current)
        {
            _previous[sample.Key] = sample.Value;
        }

        foreach (var exitedProcessId in _memoryHistory.Keys.Except(current.Keys).ToArray())
        {
            _memoryHistory.Remove(exitedProcessId);
        }
        var cpuTotal = OverallCpu();
        var memory = OverallMemory();
        var networkNow = NetworkTotal();
        var networkRate = _lastNetwork == 0 ? 0 : Math.Max(0, (networkNow - _lastNetwork) / seconds);
        _lastNetwork = networkNow; _lastTime = now;
        return new() { Processes = result.OrderByDescending(x => x.CpuPercent).ToList(), StartupItems = startup, CpuPercent = cpuTotal, MemoryPercent = memory, DiskBytesPerSecond = result.Sum(x => x.DiskBytesPerSecond), NetworkBytesPerSecond = networkRate };
    }

    private void TrackMemory(ProcessInfoModel p)
    {
        if (!_memoryHistory.TryGetValue(p.Pid, out var q)) _memoryHistory[p.Pid] = q = new();
        q.Enqueue(p.MemoryBytes); while (q.Count > 8) q.Dequeue();
        if (q.Count >= 6)
        {
            var a = q.ToArray(); var rises = Enumerable.Range(1, a.Length - 1).Count(i => a[i] > a[i - 1]);
            p.PossibleMemoryGrowth = rises >= a.Length - 2 && a[^1] - a[0] > 25 * 1024 * 1024;
        }
    }
    private static void Classify(ProcessInfoModel p)
    {
        var n = p.Name; var windowsPath = !string.IsNullOrWhiteSpace(p.Path) && p.Path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase);
        var unusual = p.Path.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase) || p.Path.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase);
        var unsigned = p.Signature == "Unsigned";
        if (string.IsNullOrWhiteSpace(p.Publisher)) p.Publisher = "Unknown";
        if (Critical.Contains(n)) { p.Category = ProcessCategory.System; p.Safety = SafetyLevel.Critical; }
        else if (windowsPath && (p.Publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) || KnownWindows.Contains(n))) { p.Category = ProcessCategory.System; p.Safety = SafetyLevel.NotRecommended; }
        else if ((unsigned && unusual) || (unusual && p.Publisher == "Unknown")) { p.Category = ProcessCategory.ReviewRecommended; p.Safety = SafetyLevel.ReviewFirst; }
        else if (p.CpuPercent >= 15 || p.MemoryBytes >= 800L * 1024 * 1024 || p.DiskBytesPerSecond >= 20L * 1024 * 1024) { p.Category = ProcessCategory.ResourceHeavy; p.Safety = KnownApps.Contains(n) ? SafetyLevel.UsuallySafe : SafetyLevel.ReviewFirst; }
        else if (p.IsStartup) { p.Category = ProcessCategory.Startup; p.Safety = KnownApps.Contains(n) ? SafetyLevel.UsuallySafe : SafetyLevel.ReviewFirst; }
        else if (KnownApps.Contains(n)) { p.Category = ProcessCategory.UserApplication; p.Safety = SafetyLevel.UsuallySafe; }
        else if (!string.IsNullOrWhiteSpace(p.Path)) { p.Category = ProcessCategory.Background; p.Safety = SafetyLevel.ReviewFirst; }
        else { p.Category = ProcessCategory.Unknown; p.Safety = SafetyLevel.ReviewFirst; }
        if (p.Publisher == "Unknown") p.Indicators.Add("UnknownPublisher");
        if (unusual) p.Indicators.Add("UnusualLocation");
        if (p.CpuPercent >= 15 || p.MemoryBytes >= 800L * 1024 * 1024) p.Indicators.Add("HighUsage");
        if (p.PossibleMemoryGrowth) p.Indicators.Add("MemoryGrowth");
        if (KnownWindows.Contains(n) && !windowsPath && !string.IsNullOrWhiteSpace(p.Path)) p.Indicators.Add("Lookalike");
        if (p.Indicators.Count > 0 && p.Category is not ProcessCategory.System and not ProcessCategory.ResourceHeavy) p.Category = ProcessCategory.ReviewRecommended;
    }
    private (string Signature, string Publisher) Signature(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return ("Unavailable", "Unknown");
        if (_signatureCache.TryGetValue(path, out var cached)) return cached;
        try
        {
            using var sourceCertificate = X509Certificate.CreateFromSignedFile(path);
            using var certificate = new X509Certificate2(sourceCertificate);
            var publisher = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            return _signatureCache[path] = ("Signed", publisher);
        }
        catch (CryptographicException)
        {
            return _signatureCache[path] = (File.Exists(path) ? "Unsigned" : "Unavailable", GetPublisher(path));
        }
        catch (IOException)
        {
            return _signatureCache[path] = ("Unavailable", GetPublisher(path));
        }
    }

    public static string GetPublisher(string path)
    {
        try
        {
            return File.Exists(path)
                ? FileVersionInfo.GetVersionInfo(path).CompanyName ?? string.Empty
                : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
    public static bool IsCritical(string name) => Critical.Contains(name);
    private static T Safe<T>(Func<T> action, T fallback) { try { return action(); } catch { return fallback; } }
    private static long GetIo(IntPtr handle) { try { return GetProcessIoCounters(handle, out var c) ? (long)(c.ReadTransferCount + c.WriteTransferCount) : 0; } catch { return 0; } }
    private double OverallCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var i = ToUlong(idle); var k = ToUlong(kernel); var u = ToUlong(user);
        var sys = (k - _lastKernel) + (u - _lastUser); var result = sys == 0 || _lastKernel == 0 ? 0 : (sys - (i - _lastIdle)) * 100d / sys;
        _lastIdle = i; _lastKernel = k; _lastUser = u; return Math.Clamp(result, 0, 100);
    }
    private static double OverallMemory() { var m = new MEMORYSTATUSEX(); return GlobalMemoryStatusEx(m) ? m.dwMemoryLoad : 0; }
    private static long NetworkTotal() { try { return NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up).Sum(x => { var s = x.GetIPv4Statistics(); return s.BytesReceived + s.BytesSent; }); } catch { return 0; } }
    private static Dictionary<int, int> GetParents()
    {
        var d = new Dictionary<int, int>(); var snap = CreateToolhelp32Snapshot(2, 0); if (snap == new IntPtr(-1)) return d;
        try { var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() }; if (Process32First(snap, ref e)) do { d[(int)e.th32ProcessID] = (int)e.th32ParentProcessID; } while (Process32Next(snap, ref e)); } finally { CloseHandle(snap); }
        return d;
    }
    private static ulong ToUlong(FILETIME f) => ((ulong)f.dwHighDateTime << 32) | f.dwLowDateTime;
    private sealed record Sample(TimeSpan Cpu, long Io, long Memory);
    [StructLayout(LayoutKind.Sequential)] private struct IO_COUNTERS { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)] private struct FILETIME { public uint dwLowDateTime, dwHighDateTime; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct PROCESSENTRY32 { public uint dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID, cntThreads, th32ParentProcessID; public int pcPriClassBase; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private sealed class MEMORYSTATUSEX { public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>(), dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
    [DllImport("kernel32.dll")] private static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS lpIoCounters);
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);
    [DllImport("kernel32.dll")] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint id);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
