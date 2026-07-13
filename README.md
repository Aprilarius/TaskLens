# TaskLens

TaskLens is a privacy-first Windows process and performance analyzer for ordinary users. It turns the process list into plain-language categories, explains common Windows and application processes, highlights resource-heavy or unusual local behavior, reviews startup entries, and creates local reports.

> All analysis is performed locally. No process data leaves this device.

## Features

- Live CPU, memory, disk-I/O and network activity summary with a configurable 2–30 second refresh interval.
- Process search, filters, sorting, parent PID, start time, executable path, cached signature/publisher lookup, explanations and safe-to-close guidance.
- Conservative review indicators for unsigned executables, user-writable locations, unusual Windows-like process locations, high resource use and sustained memory growth.
- Critical Windows process deny-list and confirmation before ending eligible processes.
- Startup review from per-user/all-user registry keys and Startup folders; TaskLens never disables entries automatically.
- Local HTML, JSON and CSV reports with optional path anonymization.
- English, Russian and Azerbaijani UI and localized HTML reports, with English fallback.
- Light, dark and system theme, responsive navigation and virtualized process rows.

TaskLens does not upload telemetry, call web services, read browser history, inspect personal file contents, collect passwords, or claim to detect malware.

## Build the one-file app

Requirements: Windows x64 and the .NET 8 SDK.

```powershell
./build.ps1 -Clean
```

The user-facing artifact is `dist/TaskLens.exe`. It is a self-contained WPF single-file publish and does not require a separately installed .NET runtime. The `dist` directory is intentionally ignored by Git; published binaries should be attached to a GitHub Release. Settings are created under `%LOCALAPPDATA%\TaskLens` only after they are saved. Reports are created under `Documents\TaskLens Reports` only when requested.

## Architecture

- `Core/Models.cs` defines scan results, process classifications and persisted settings.
- `Core/Services.cs` contains Windows process, startup and settings services.
- `Core/ReportService.cs` owns HTML, JSON and CSV serialization.
- `Core/LocalizationService.cs` provides language selection and English fallback.
- `MainWindow.xaml.cs` owns application lifecycle, navigation and refresh coordination.
- `UI/MainWindow.Pages.cs` builds the individual pages and reusable controls.
- `Localization/` contains standard RESX resources for future designer-based localization.

The scanner runs away from the UI thread. Process-level failures are isolated because a process can exit or become inaccessible at any point during enumeration. Digital-signature results are cached by executable path to avoid repeating expensive checks on every refresh.

## Quality checks

The project enables the latest recommended .NET analyzers and treats compiler warnings as errors. `tools/Test-Localization.ps1` verifies that English, Russian and Azerbaijani catalogs contain the same non-empty keys. The GitHub Actions workflow restores, builds and publishes the Windows x64 executable on every push and pull request to `main`.

## What TaskLens checks

Running process metadata and short-window resource counters, executable signature/publisher where accessible, startup entries, process location, parent PID and memory trend. Access-denied or rapidly exiting processes are skipped or shown as unavailable without failing the scan.

## Limitations

TaskLens does not replace Task Manager or antivirus software, detect or remove malware, or guarantee that a process is safe. Per-process network attribution and command lines are deliberately shown as unavailable in this MVP when they cannot be collected safely without heavier tracing or elevation. Results are a point-in-time local guide and some details require administrator rights.

## Roadmap

Deeper process/service explanations, Turkish UI, richer startup-impact sampling, PDF export, scheduled local snapshots, Event Viewer integration, signed releases, portable mode, and opt-in reputation updates.
