using PantheonAddonFramework;
using PantheonAddonFramework.Configuration;
using PantheonAddonFramework.Models;
using System.Diagnostics;
using System.Text.Json;

namespace PantheonAddons.EntityScanner;

[AddonMetadata("Entity Scanner", "Codex", "Exports nearby player, NPC, and local character snapshots to JSONL")]
public sealed class EntityScanner : Addon
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ConfigJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const int MaxBufferedEvents = 1000;

    private readonly string _gameFolder = ResolveGameFolder();
    private readonly List<string> _bufferedEvents = new();
    private readonly Dictionary<uint, EntitySnapshot> _latestByNetworkId = new();
    private string _outputFolder = DefaultOutputFolder;
    private string _jsonLogPath = Path.Combine(DefaultOutputFolder, "entities-live-current.jsonl");
    private StreamWriter? _jsonWriter;
    private IPlayer? _localPlayer;
    private DateTime _nextLocalWriteUtc = DateTime.MinValue;
    private bool _isEnabled;
    private bool _includePlayers = true;
    private bool _includeNpcs = true;
    private bool _includeLocalPlayer = true;
    private float _maxDistance = 300f;
    private int _maxFileMegabytes = 10;
    private int _seenCount;
    private int _updatedCount;
    private int _removedCount;
    private int _localCount;
    private string _lastStatus = "Entity Scanner idle.";

    public override void OnCreate()
    {
        LoadPathConfig();
        CustomChatCommands.Add("/entityscan", HandleCommand);
        EntityEvents.EntitySeen.Subscribe(OnEntitySeen);
        EntityEvents.EntityUpdated.Subscribe(OnEntityUpdated);
        EntityEvents.EntityRemoved.Subscribe(OnEntityRemoved);
        LocalPlayerEvents.LocalPlayerEntered.Subscribe(OnLocalPlayerEntered);
        LocalPlayerEvents.LocalPlayerLeft.Subscribe(OnLocalPlayerLeft);
        LifecycleEvents.OnUpdate.Subscribe(OnUpdate);
        OpenLog();
    }

    public override void Enable()
    {
        _isEnabled = true;
        EnsureLogOpen();
    }

    public override void Disable()
    {
        _isEnabled = false;
    }

    public override IEnumerable<IConfigurationValue> GetConfiguration()
    {
        return new IConfigurationValue[]
        {
            new BoolConfigurationValue("Players", "Writes player entity snapshots.", _includePlayers, value => _includePlayers = value),
            new BoolConfigurationValue("NPCs", "Writes NPC entity snapshots.", _includeNpcs, value => _includeNpcs = value),
            new BoolConfigurationValue("Local player", "Writes this character's own position and character info.", _includeLocalPlayer, value => _includeLocalPlayer = value),
            new IntConfigurationValue("Max file MB", "Rotates live JSONL when it reaches this size.", _maxFileMegabytes, 1, 250, 1, value => _maxFileMegabytes = value)
        };
    }

    public override void Dispose()
    {
        CustomChatCommands.Remove("/entityscan");
        EntityEvents.EntitySeen.Unsubscribe(OnEntitySeen);
        EntityEvents.EntityUpdated.Unsubscribe(OnEntityUpdated);
        EntityEvents.EntityRemoved.Unsubscribe(OnEntityRemoved);
        LocalPlayerEvents.LocalPlayerEntered.Unsubscribe(OnLocalPlayerEntered);
        LocalPlayerEvents.LocalPlayerLeft.Unsubscribe(OnLocalPlayerLeft);
        LifecycleEvents.OnUpdate.Unsubscribe(OnUpdate);
        CloseLog();
    }

    private void HandleCommand(string[] args)
    {
        if (args.Length == 0 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage("Entity Scanner: /entityscan status, path, clear, save, reopen, players on|off, npcs on|off, local on|off, distance <meters|0>");
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "status":
                Chat.AddInfoMessage($"{_lastStatus} enabled={_isEnabled}; known={_latestByNetworkId.Count}; seen={_seenCount}; updates={_updatedCount}; removed={_removedCount}; local={_localCount}; cap={_maxFileMegabytes}MB; distance={_maxDistance:F0}m");
                break;
            case "path":
                Chat.AddInfoMessage($"Entity Scanner json: {_jsonLogPath}");
                Chat.AddInfoMessage($"Entity Scanner config: {LocalPathConfigPath}");
                break;
            case "clear":
                ClearLog();
                Chat.AddInfoMessage("Entity Scanner live log cleared.");
                break;
            case "save":
                SaveSnapshot();
                break;
            case "reopen":
                OpenLog();
                Chat.AddInfoMessage("Entity Scanner log reopened.");
                break;
            case "players":
                HandleToggle(args, "Players", value => _includePlayers = value, _includePlayers);
                break;
            case "npcs":
                HandleToggle(args, "NPCs", value => _includeNpcs = value, _includeNpcs);
                break;
            case "local":
                HandleToggle(args, "Local player", value => _includeLocalPlayer = value, _includeLocalPlayer);
                break;
            case "distance":
                HandleDistance(args);
                break;
            default:
                Chat.AddInfoMessage("Entity Scanner: unknown command. Try /entityscan help.");
                break;
        }
    }

    private void HandleToggle(string[] args, string label, Action<bool> setter, bool current)
    {
        if (args.Length < 2 || args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage($"Entity Scanner {label.ToLowerInvariant()} is {(current ? "on" : "off")}.");
            return;
        }

        if (args[1].Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            setter(true);
            SavePathConfig();
            Chat.AddInfoMessage($"Entity Scanner {label.ToLowerInvariant()} enabled.");
            return;
        }

        if (args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            setter(false);
            SavePathConfig();
            Chat.AddInfoMessage($"Entity Scanner {label.ToLowerInvariant()} disabled.");
            return;
        }

        Chat.AddInfoMessage($"Entity Scanner {label.ToLowerInvariant()}: on, off, status");
    }

    private void HandleDistance(string[] args)
    {
        if (args.Length < 2 || !float.TryParse(args[1], out var meters))
        {
            Chat.AddInfoMessage($"Entity Scanner distance is {_maxDistance:F0}m. Use 0 for unlimited.");
            return;
        }

        _maxDistance = Math.Clamp(meters, 0f, 10000f);
        SavePathConfig();
        Chat.AddInfoMessage(_maxDistance <= 0
            ? "Entity Scanner distance filter disabled."
            : $"Entity Scanner distance set to {_maxDistance:F0}m.");
    }

    private void OnUpdate()
    {
        if (!_isEnabled || !_includeLocalPlayer || DateTime.UtcNow < _nextLocalWriteUtc)
        {
            return;
        }

        _nextLocalWriteUtc = DateTime.UtcNow.AddSeconds(1);
        WriteLocalPlayer();
    }

    private void OnLocalPlayerEntered(IPlayer player)
    {
        _localPlayer = player;
        OpenLog();
        WriteLocalPlayer();
    }

    private void OnLocalPlayerLeft(IPlayer player)
    {
        if (_localPlayer?.CharacterId == player.CharacterId)
        {
            _localPlayer = null;
            OpenLog();
        }
    }

    private void OnEntitySeen(EntitySnapshot snapshot)
    {
        _seenCount++;
        _latestByNetworkId[snapshot.NetworkId] = snapshot;
        WriteEntity(snapshot);
    }

    private void OnEntityUpdated(EntitySnapshot snapshot)
    {
        _updatedCount++;
        _latestByNetworkId[snapshot.NetworkId] = snapshot;
        WriteEntity(snapshot);
    }

    private void OnEntityRemoved(EntitySnapshot snapshot)
    {
        _removedCount++;
        _latestByNetworkId.Remove(snapshot.NetworkId);
        WriteEntity(snapshot);
    }

    private void WriteEntity(EntitySnapshot snapshot)
    {
        if (!_isEnabled || !ShouldWrite(snapshot))
        {
            return;
        }

        WriteJsonLine(snapshot);
    }

    private bool ShouldWrite(EntitySnapshot snapshot)
    {
        if (snapshot.IsLocalPlayer)
        {
            return _includeLocalPlayer;
        }

        if (snapshot.EntityType.Equals("Player", StringComparison.OrdinalIgnoreCase) && !_includePlayers)
        {
            return false;
        }

        if (snapshot.EntityType.Equals("NPC", StringComparison.OrdinalIgnoreCase) && !_includeNpcs)
        {
            return false;
        }

        return _maxDistance <= 0 || snapshot.DistanceFromLocal <= _maxDistance;
    }

    private void WriteLocalPlayer()
    {
        var player = _localPlayer;
        var position = player?.GetPosition();
        if (player == null || position == null)
        {
            return;
        }

        _localCount++;
        WriteJsonLine(new LocalPlayerSnapshot(
            TimestampUtc: DateTime.UtcNow,
            EventType: "localPlayer",
            EntityType: "LocalPlayer",
            CharacterId: player.CharacterId,
            Name: player.Name,
            Race: player.Race,
            Class: player.Class,
            Level: player.Level,
            X: position.X,
            Y: position.Y,
            Z: position.Z,
            HeadingY: position.HeadingY,
            OffensiveTarget: player.GetOffensiveTarget(),
            DefensiveTarget: player.GetDefensiveTarget()));
    }

    private void WriteJsonLine<T>(T payload)
    {
        EnsureLogOpen();
        RotateLogIfNeeded();

        var line = JsonSerializer.Serialize(payload);
        _bufferedEvents.Add(line);
        TrimBuffer();
        _jsonWriter?.WriteLine(line);
        _lastStatus = $"Entity Scanner writing to {_jsonLogPath}";
    }

    private void OpenLog()
    {
        CloseLog();
        Directory.CreateDirectory(_outputFolder);
        _jsonLogPath = Path.Combine(_outputFolder, $"entities-live-{GetCurrentFileSuffix()}.jsonl");
        _jsonWriter = new StreamWriter(new FileStream(_jsonLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        _lastStatus = $"Entity Scanner writing to {_jsonLogPath}";
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

    private void ClearLog()
    {
        _bufferedEvents.Clear();
        _latestByNetworkId.Clear();
        CloseLog();
        DeleteIfExists(_jsonLogPath);
        _seenCount = 0;
        _updatedCount = 0;
        _removedCount = 0;
        _localCount = 0;
        OpenLog();
    }

    private void SaveSnapshot()
    {
        if (_bufferedEvents.Count == 0)
        {
            Chat.AddInfoMessage("Entity Scanner buffer is empty.");
            return;
        }

        Directory.CreateDirectory(_outputFolder);
        var filePath = Path.Combine(_outputFolder, $"entities-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
        File.WriteAllLines(filePath, _bufferedEvents);
        Chat.AddInfoMessage($"Entity Scanner saved {_bufferedEvents.Count} lines: {filePath}");
    }

    private void RotateLogIfNeeded()
    {
        var maxBytes = Math.Max(1, _maxFileMegabytes) * 1024L * 1024L;
        if (!File.Exists(_jsonLogPath) || new FileInfo(_jsonLogPath).Length < maxBytes)
        {
            return;
        }

        CloseLog();
        var previousPath = $"{_jsonLogPath}.previous";
        DeleteIfExists(previousPath);
        File.Move(_jsonLogPath, previousPath);
        OpenLog();
    }

    private static void DeleteIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void TrimBuffer()
    {
        if (_bufferedEvents.Count <= MaxBufferedEvents)
        {
            return;
        }

        _bufferedEvents.RemoveRange(0, _bufferedEvents.Count - MaxBufferedEvents);
    }

    private void LoadPathConfig()
    {
        if (!File.Exists(LocalPathConfigPath))
        {
            return;
        }

        try
        {
            var config = JsonSerializer.Deserialize<PathConfig>(File.ReadAllText(LocalPathConfigPath), ConfigJsonOptions);
            var configuredOutputFolder = ExpandConfiguredPath(FirstNonBlank(config?.OutputFolder, config?.DataFolder, config?.Directory, config?.Folder));
            if (!string.IsNullOrWhiteSpace(configuredOutputFolder))
            {
                _outputFolder = configuredOutputFolder;
                _jsonLogPath = Path.Combine(_outputFolder, $"entities-live-{GetCurrentFileSuffix()}.jsonl");
            }

            _includePlayers = config?.IncludePlayers ?? _includePlayers;
            _includeNpcs = config?.IncludeNpcs ?? _includeNpcs;
            _includeLocalPlayer = config?.IncludeLocalPlayer ?? _includeLocalPlayer;
            _maxDistance = Math.Clamp(config?.MaxDistance ?? _maxDistance, 0f, 10000f);
            _maxFileMegabytes = Math.Clamp(config?.MaxFileMegabytes ?? _maxFileMegabytes, 1, 250);
        }
        catch (Exception ex)
        {
            Logger.Error($"Entity Scanner config failed: {ex}");
        }
    }

    private void SavePathConfig()
    {
        var config = new PathConfig(
            OutputFolder: _outputFolder,
            IncludePlayers: _includePlayers,
            IncludeNpcs: _includeNpcs,
            IncludeLocalPlayer: _includeLocalPlayer,
            MaxDistance: _maxDistance,
            MaxFileMegabytes: _maxFileMegabytes);

        Directory.CreateDirectory(Path.GetDirectoryName(LocalPathConfigPath) ?? _gameFolder);
        File.WriteAllText(LocalPathConfigPath, JsonSerializer.Serialize(config, JsonOptions));
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

    private static string? FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private string GetCurrentFileSuffix()
    {
        return string.IsNullOrWhiteSpace(_localPlayer?.Name) ? "current" : SanitizeFileName(_localPlayer.Name);
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "current" : sanitized;
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

    private static string DefaultOutputFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PantheonEntityScanner");

    private string LocalPathConfigPath => Path.Combine(_gameFolder, "Mods", "PantheonAddons", "EntityScannerConfig.json");

    private sealed record PathConfig(
        string? OutputFolder,
        bool? IncludePlayers = null,
        bool? IncludeNpcs = null,
        bool? IncludeLocalPlayer = null,
        float? MaxDistance = null,
        int? MaxFileMegabytes = null,
        string? DataFolder = null,
        string? Directory = null,
        string? Folder = null);

    private sealed record LocalPlayerSnapshot(
        DateTime TimestampUtc,
        string EventType,
        string EntityType,
        long CharacterId,
        string Name,
        string Race,
        string Class,
        int Level,
        float X,
        float Y,
        float Z,
        float HeadingY,
        TargetSnapshot? OffensiveTarget,
        TargetSnapshot? DefensiveTarget);
}
