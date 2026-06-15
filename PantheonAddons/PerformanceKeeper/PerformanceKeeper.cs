using PantheonAddonFramework;
using PantheonAddonFramework.Configuration;
using System.Diagnostics;
using System.Text.Json;
using UnityEngine;

namespace PantheonAddons.PerformanceKeeper;

[AddonMetadata("Performance Keeper", "Codex", "Keeps background Pantheon clients running at a usable frame rate")]
public sealed class PerformanceKeeper : Addon
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ConfigJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _gameFolder = ResolveGameFolder();
    private string _outputFolder = DefaultOutputFolder;
    private string _logPath = Path.Combine(DefaultOutputFolder, "performance-current.jsonl");
    private StreamWriter? _jsonWriter;
    private bool _isEnabled = true;
    private bool _runInBackground = true;
    private bool _forceVSyncOff = true;
    private int _activeFps = 60;
    private int _backgroundFps = 30;
    private int _sampleSeconds = 5;
    private int _originalTargetFrameRate;
    private int _originalVSyncCount;
    private bool _originalRunInBackground;
    private bool _capturedOriginals;
    private bool _lastFocused;
    private int _lastAppliedFps = int.MinValue;
    private int _lastAppliedVSync = int.MinValue;
    private DateTime _nextSampleAt = DateTime.MinValue;
    private DateTime _nextApplyAt = DateTime.MinValue;
    private double _frameAccumulator;
    private int _frameCount;
    private float _lastAverageFps;
    private string _lastStatus = "Performance Keeper idle.";

    public override void OnCreate()
    {
        LoadConfig();
        CustomChatCommands.Add("/perfkeeper", HandleCommand);
        CustomChatCommands.Add("/perfmode", HandleCommand);
        LifecycleEvents.OnUpdate.Subscribe(OnUpdate);
        OpenLog();
        CaptureOriginals();
        ApplySettings(true);
    }

    public override void Enable()
    {
        _isEnabled = true;
        EnsureLogOpen();
        ApplySettings(true);
    }

    public override void Disable()
    {
        _isEnabled = false;
        RestoreOriginals();
    }

    public override IEnumerable<IConfigurationValue> GetConfiguration()
    {
        return new IConfigurationValue[]
        {
            new BoolConfigurationValue("Run in background", "Requests that Unity keep updating while this client is unfocused.", _runInBackground, value =>
            {
                _runInBackground = value;
                SaveConfig();
                ApplySettings(true);
            }),
            new BoolConfigurationValue("Force v-sync off", "Disables v-sync so Unity targetFrameRate can take effect.", _forceVSyncOff, value =>
            {
                _forceVSyncOff = value;
                SaveConfig();
                ApplySettings(true);
            }),
            new IntConfigurationValue("Active FPS", "Target FPS while this Pantheon window is focused.", _activeFps, 15, 240, 5, value =>
            {
                _activeFps = value;
                SaveConfig();
                ApplySettings(true);
            }),
            new IntConfigurationValue("Background FPS", "Target FPS while this Pantheon window is unfocused.", _backgroundFps, 15, 240, 5, value =>
            {
                _backgroundFps = value;
                SaveConfig();
                ApplySettings(true);
            })
        };
    }

    public override void Dispose()
    {
        CustomChatCommands.Remove("/perfkeeper");
        CustomChatCommands.Remove("/perfmode");
        LifecycleEvents.OnUpdate.Unsubscribe(OnUpdate);
        RestoreOriginals();
        CloseLog();
    }

    private void HandleCommand(string[] args)
    {
        if (args.Length == 0 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage("Performance Keeper: /perfmode status, path, on, off, bgfps <15-240>, activefps <15-240>, vsync on|off, background on|off, sample <seconds>");
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "status":
                Chat.AddInfoMessage($"{_lastStatus} enabled={_isEnabled}; focused={SafeIsFocused()}; target={Application.targetFrameRate}; active={_activeFps}; bg={_backgroundFps}; vSync={QualitySettings.vSyncCount}; avg={_lastAverageFps:F1}fps");
                break;
            case "path":
                Chat.AddInfoMessage($"Performance Keeper log: {_logPath}");
                Chat.AddInfoMessage($"Performance Keeper config: {LocalConfigPath}");
                break;
            case "on":
                _isEnabled = true;
                SaveConfig();
                ApplySettings(true);
                Chat.AddInfoMessage("Performance Keeper enabled.");
                break;
            case "off":
                _isEnabled = false;
                SaveConfig();
                RestoreOriginals();
                Chat.AddInfoMessage("Performance Keeper disabled and original Unity settings restored.");
                break;
            case "bgfps":
                SetFps(args, true);
                break;
            case "activefps":
                SetFps(args, false);
                break;
            case "vsync":
                HandleToggle(args, "v-sync override", value => _forceVSyncOff = value, _forceVSyncOff);
                break;
            case "background":
                HandleToggle(args, "run in background", value => _runInBackground = value, _runInBackground);
                break;
            case "sample":
                SetSampleSeconds(args);
                break;
            default:
                Chat.AddInfoMessage("Performance Keeper: unknown command. Try /perfmode help.");
                break;
        }
    }

    private void OnUpdate()
    {
        _frameCount++;
        _frameAccumulator += Math.Max(0.0001f, Time.unscaledDeltaTime);

        if (!_isEnabled)
        {
            return;
        }

        if (DateTime.UtcNow >= _nextApplyAt)
        {
            _nextApplyAt = DateTime.UtcNow.AddSeconds(0.5);
            ApplySettings(false);
        }

        if (DateTime.UtcNow >= _nextSampleAt)
        {
            WriteSample();
            _nextSampleAt = DateTime.UtcNow.AddSeconds(Math.Clamp(_sampleSeconds, 1, 60));
        }
    }

    private void ApplySettings(bool force)
    {
        if (!_isEnabled)
        {
            return;
        }

        CaptureOriginals();

        try
        {
            Application.runInBackground = _runInBackground;
            if (_forceVSyncOff && QualitySettings.vSyncCount != 0)
            {
                QualitySettings.vSyncCount = 0;
            }

            var focused = SafeIsFocused();
            var targetFps = focused ? _activeFps : _backgroundFps;
            if (force || focused != _lastFocused || targetFps != _lastAppliedFps || QualitySettings.vSyncCount != _lastAppliedVSync)
            {
                Application.targetFrameRate = Math.Clamp(targetFps, 15, 240);
                _lastFocused = focused;
                _lastAppliedFps = Application.targetFrameRate;
                _lastAppliedVSync = QualitySettings.vSyncCount;
                _lastStatus = focused
                    ? $"Performance Keeper focused target {_lastAppliedFps}fps."
                    : $"Performance Keeper background target {_lastAppliedFps}fps.";
                WriteEvent("settingsApplied");
            }
        }
        catch (Exception ex)
        {
            _lastStatus = $"Performance Keeper apply failed: {ex.GetType().Name}";
            Logger.Error($"Performance Keeper apply failed: {ex}");
        }
    }

    private void RestoreOriginals()
    {
        if (!_capturedOriginals)
        {
            return;
        }

        try
        {
            Application.targetFrameRate = _originalTargetFrameRate;
            Application.runInBackground = _originalRunInBackground;
            QualitySettings.vSyncCount = _originalVSyncCount;
            _lastStatus = "Performance Keeper restored original Unity settings.";
            WriteEvent("settingsRestored");
        }
        catch (Exception ex)
        {
            Logger.Error($"Performance Keeper restore failed: {ex}");
        }
    }

    private void CaptureOriginals()
    {
        if (_capturedOriginals)
        {
            return;
        }

        try
        {
            _originalTargetFrameRate = Application.targetFrameRate;
            _originalRunInBackground = Application.runInBackground;
            _originalVSyncCount = QualitySettings.vSyncCount;
            _lastFocused = SafeIsFocused();
            _capturedOriginals = true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Performance Keeper could not capture original settings: {ex}");
        }
    }

    private void SetFps(string[] args, bool background)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var fps))
        {
            Chat.AddInfoMessage($"Performance Keeper {(background ? "background" : "active")} FPS is {(background ? _backgroundFps : _activeFps)}.");
            return;
        }

        fps = Math.Clamp(fps, 15, 240);
        if (background)
        {
            _backgroundFps = fps;
        }
        else
        {
            _activeFps = fps;
        }

        SaveConfig();
        ApplySettings(true);
        Chat.AddInfoMessage($"Performance Keeper {(background ? "background" : "active")} FPS set to {fps}.");
    }

    private void SetSampleSeconds(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var seconds))
        {
            Chat.AddInfoMessage($"Performance Keeper sample interval is {_sampleSeconds}s.");
            return;
        }

        _sampleSeconds = Math.Clamp(seconds, 1, 60);
        SaveConfig();
        Chat.AddInfoMessage($"Performance Keeper sample interval set to {_sampleSeconds}s.");
    }

    private void HandleToggle(string[] args, string label, Action<bool> setter, bool current)
    {
        if (args.Length < 2 || args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage($"Performance Keeper {label} is {(current ? "on" : "off")}.");
            return;
        }

        if (args[1].Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            setter(true);
            SaveConfig();
            ApplySettings(true);
            Chat.AddInfoMessage($"Performance Keeper {label} enabled.");
            return;
        }

        if (args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            setter(false);
            SaveConfig();
            ApplySettings(true);
            Chat.AddInfoMessage($"Performance Keeper {label} disabled.");
            return;
        }

        Chat.AddInfoMessage($"Performance Keeper {label}: on, off, status");
    }

    private void WriteSample()
    {
        var seconds = Math.Max(0.0001, _frameAccumulator);
        _lastAverageFps = (float)(_frameCount / seconds);
        _frameCount = 0;
        _frameAccumulator = 0;
        WriteEvent("sample");
    }

    private void WriteEvent(string eventType)
    {
        try
        {
            EnsureLogOpen();
            var payload = new PerformanceSample(
                TimestampUtc: DateTime.UtcNow,
                EventType: eventType,
                ProcessId: Process.GetCurrentProcess().Id,
                Focused: SafeIsFocused(),
                RunInBackground: Application.runInBackground,
                TargetFrameRate: Application.targetFrameRate,
                VSyncCount: QualitySettings.vSyncCount,
                AverageFps: _lastAverageFps,
                UnscaledDeltaTime: Time.unscaledDeltaTime);

            _jsonWriter?.WriteLine(JsonSerializer.Serialize(payload));
        }
        catch (Exception ex)
        {
            _lastStatus = $"Performance Keeper log failed: {ex.GetType().Name}";
        }
    }

    private void OpenLog()
    {
        CloseLog();
        Directory.CreateDirectory(_outputFolder);
        _logPath = Path.Combine(_outputFolder, $"performance-{Process.GetCurrentProcess().Id}.jsonl");
        _jsonWriter = new StreamWriter(new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
    }

    private void EnsureLogOpen()
    {
        if (_jsonWriter == null)
        {
            OpenLog();
        }
    }

    private void CloseLog()
    {
        _jsonWriter?.Dispose();
        _jsonWriter = null;
    }

    private void LoadConfig()
    {
        if (!File.Exists(LocalConfigPath))
        {
            return;
        }

        try
        {
            var config = JsonSerializer.Deserialize<PathConfig>(File.ReadAllText(LocalConfigPath), ConfigJsonOptions);
            var configuredOutputFolder = ExpandConfiguredPath(config?.OutputFolder);
            if (!string.IsNullOrWhiteSpace(configuredOutputFolder))
            {
                _outputFolder = configuredOutputFolder;
            }

            _isEnabled = config?.Enabled ?? _isEnabled;
            _runInBackground = config?.RunInBackground ?? _runInBackground;
            _forceVSyncOff = config?.ForceVSyncOff ?? _forceVSyncOff;
            _activeFps = Math.Clamp(config?.ActiveFps ?? _activeFps, 15, 240);
            _backgroundFps = Math.Clamp(config?.BackgroundFps ?? _backgroundFps, 15, 240);
            _sampleSeconds = Math.Clamp(config?.SampleSeconds ?? _sampleSeconds, 1, 60);
        }
        catch (Exception ex)
        {
            Logger.Error($"Performance Keeper config failed: {ex}");
        }
    }

    private void SaveConfig()
    {
        var config = new PathConfig(
            OutputFolder: _outputFolder,
            Enabled: _isEnabled,
            RunInBackground: _runInBackground,
            ForceVSyncOff: _forceVSyncOff,
            ActiveFps: _activeFps,
            BackgroundFps: _backgroundFps,
            SampleSeconds: _sampleSeconds);

        Directory.CreateDirectory(Path.GetDirectoryName(LocalConfigPath) ?? _gameFolder);
        File.WriteAllText(LocalConfigPath, JsonSerializer.Serialize(config, JsonOptions));
    }

    private string ExpandConfiguredPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        return Path.IsPathRooted(expanded) ? expanded : Path.GetFullPath(Path.Combine(_gameFolder, expanded));
    }

    private static bool SafeIsFocused()
    {
        try
        {
            return Application.isFocused;
        }
        catch
        {
            return true;
        }
    }

    private static string ResolveGameFolder()
    {
        try
        {
            var mainModulePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(mainModulePath))
            {
                var folder = Path.GetDirectoryName(mainModulePath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    return folder;
                }
            }
        }
        catch
        {
        }

        return AppContext.BaseDirectory;
    }

    private static string DefaultOutputFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PantheonPerformance");

    private string LocalConfigPath => Path.Combine(_gameFolder, "Mods", "PantheonAddons", "PerformanceKeeperConfig.json");

    private sealed record PathConfig(
        string? OutputFolder,
        bool? Enabled = null,
        bool? RunInBackground = null,
        bool? ForceVSyncOff = null,
        int? ActiveFps = null,
        int? BackgroundFps = null,
        int? SampleSeconds = null);

    private sealed record PerformanceSample(
        DateTime TimestampUtc,
        string EventType,
        int ProcessId,
        bool Focused,
        bool RunInBackground,
        int TargetFrameRate,
        int VSyncCount,
        float AverageFps,
        float UnscaledDeltaTime);
}
