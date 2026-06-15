using PantheonAddonFramework;
using PantheonAddonFramework.Configuration;
using PantheonAddonFramework.Models;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PantheonAddons.LootData;

[AddonMetadata("Loot Data", "Codex", "Exports loot and item acquisition data to JSONL files")]
public sealed class LootData : Addon
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ConfigJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _gameFolder = ResolveGameFolder();
    private IPlayer? _localPlayer;
    private string _outputFolder = DefaultOutputFolder;
    private string _eventsPath = Path.Combine(DefaultOutputFolder, "loot-events-current.jsonl");
    private StreamWriter? _eventsWriter;
    private bool _isEnabled;
    private bool _includeInventoryEvents = true;
    private bool _includeLootChat = true;
    private bool _includeRawDump;
    private int _maxFileMegabytes = 5;
    private int _inventoryEventCount;
    private int _lootChatCount;
    private readonly Queue<IInventoryItem> _pendingSnapshotItems = new();
    private int _pendingSnapshotTotal;
    private int _pendingSnapshotWritten;
    private bool _snapshotInProgress;
    private string _lastStatus = "Loot Data idle.";

    public override void OnCreate()
    {
        LoadPathConfig();
        CustomChatCommands.Add("/lootdata", HandleCommand);
        LocalPlayerEvents.LocalPlayerEntered.Subscribe(OnLocalPlayerEntered);
        LocalPlayerEvents.ItemAdded.Subscribe(OnItemAdded);
        LocalPlayerEvents.ItemRemoved.Subscribe(OnItemRemoved);
        ChatEvents.MessageReceived.Subscribe(OnMessageReceived);
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
            new BoolConfigurationValue("Inventory events", "Writes item added and removed events.", _includeInventoryEvents, value => _includeInventoryEvents = value),
            new BoolConfigurationValue("Loot chat", "Writes loot-like chat messages.", _includeLootChat, value => _includeLootChat = value),
            new BoolConfigurationValue("Raw dump", "Writes the full raw item/template object graph. Use only for small tests.", _includeRawDump, value => _includeRawDump = value),
            new IntConfigurationValue("Max file MB", "Rotates the live JSONL file when it reaches this size.", _maxFileMegabytes, 1, 100, 1, value => _maxFileMegabytes = value)
        };
    }

    public override void Dispose()
    {
        CustomChatCommands.Remove("/lootdata");
        LocalPlayerEvents.LocalPlayerEntered.Unsubscribe(OnLocalPlayerEntered);
        LocalPlayerEvents.ItemAdded.Unsubscribe(OnItemAdded);
        LocalPlayerEvents.ItemRemoved.Unsubscribe(OnItemRemoved);
        ChatEvents.MessageReceived.Unsubscribe(OnMessageReceived);
        LifecycleEvents.OnUpdate.Unsubscribe(OnUpdate);
        CloseLog();
    }

    private void HandleCommand(string[] args)
    {
        if (args.Length == 0 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage("Loot Data: /lootdata status, path, clear, reopen, inventory on|off, chat on|off, raw on|off, snapshot");
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "status":
                Chat.AddInfoMessage($"{_lastStatus} enabled={_isEnabled}; inventoryEvents={_inventoryEventCount}; lootChat={_lootChatCount}; cap={_maxFileMegabytes}MB");
                break;
            case "path":
                Chat.AddInfoMessage($"Loot Data events: {_eventsPath}");
                Chat.AddInfoMessage($"Loot Data config: {LocalPathConfigPath}");
                break;
            case "clear":
                ClearLog();
                Chat.AddInfoMessage("Loot Data log cleared.");
                break;
            case "reopen":
                OpenLog();
                Chat.AddInfoMessage("Loot Data log reopened.");
                break;
            case "inventory":
                HandleToggle(args, "Inventory events", value => _includeInventoryEvents = value, _includeInventoryEvents);
                break;
            case "chat":
                HandleToggle(args, "Loot chat", value => _includeLootChat = value, _includeLootChat);
                break;
            case "raw":
                HandleToggle(args, "Raw dump", value => _includeRawDump = value, _includeRawDump);
                break;
            case "snapshot":
                WriteInventorySnapshot();
                break;
            default:
                Chat.AddInfoMessage("Loot Data: unknown command. Try /lootdata help.");
                break;
        }
    }

    private void HandleToggle(string[] args, string label, Action<bool> setter, bool current)
    {
        if (args.Length < 2 || args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage($"Loot Data {label.ToLowerInvariant()} is {(current ? "on" : "off")}.");
            return;
        }

        if (args[1].Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            setter(true);
            SavePathConfig();
            Chat.AddInfoMessage($"Loot Data {label.ToLowerInvariant()} enabled.");
            return;
        }

        if (args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            setter(false);
            SavePathConfig();
            Chat.AddInfoMessage($"Loot Data {label.ToLowerInvariant()} disabled.");
            return;
        }

        Chat.AddInfoMessage($"Loot Data {label.ToLowerInvariant()}: on, off, status");
    }

    private void OnLocalPlayerEntered(IPlayer player)
    {
        if (player.IsLocalPlayer)
        {
            _localPlayer = player;
        }
    }

    private void OnItemAdded(IInventoryItem item)
    {
        if (!_isEnabled || !_includeInventoryEvents)
        {
            return;
        }

        _inventoryEventCount++;
        WritePayload("item_added", item);
    }

    private void OnItemRemoved(IInventoryItem item)
    {
        if (!_isEnabled || !_includeInventoryEvents)
        {
            return;
        }

        _inventoryEventCount++;
        WritePayload("item_removed", item);
    }

    private void OnMessageReceived(ChatMessage message)
    {
        if (!_isEnabled || !_includeLootChat || !LooksLikeLootMessage(message))
        {
            return;
        }

        _lootChatCount++;
        WriteEvent(new Dictionary<string, object?>
        {
            ["timestamp"] = DateTime.Now.ToString("O"),
            ["eventType"] = "loot_chat",
            ["character"] = _localPlayer?.Name,
            ["characterId"] = _localPlayer?.CharacterId,
            ["chatChannel"] = message.ChatChannelType,
            ["sender"] = message.Sender,
            ["message"] = message.Message,
            ["parsed"] = ParseLootMessage(message.Message)
        });
    }

    private void WritePayload(string eventType, IInventoryItem item)
    {
        ItemSnapshot? snapshot = null;
        try
        {
        snapshot = item.GetSnapshot(_includeRawDump);
        }
        catch (Exception ex)
        {
            Logger.Error($"Loot Data item snapshot failed: {ex}");
        }

        WriteEvent(new Dictionary<string, object?>
        {
            ["timestamp"] = DateTime.Now.ToString("O"),
            ["eventType"] = eventType,
            ["character"] = _localPlayer?.Name,
            ["characterId"] = _localPlayer?.CharacterId,
            ["itemInstanceId"] = item.Id,
            ["itemName"] = item.Name,
            ["item"] = snapshot
        });
    }

    private void WriteInventorySnapshot()
    {
        if (_localPlayer == null)
        {
            Chat.AddInfoMessage("Loot Data has not found the local player yet.");
            return;
        }

        _pendingSnapshotItems.Clear();
        foreach (var item in _localPlayer.Inventory.Items)
        {
            _pendingSnapshotItems.Enqueue(item);
        }

        _pendingSnapshotTotal = _pendingSnapshotItems.Count;
        _pendingSnapshotWritten = 0;
        _snapshotInProgress = _pendingSnapshotTotal > 0;

        if (!_snapshotInProgress)
        {
            Chat.AddInfoMessage("Loot Data snapshot had no inventory items to write.");
            return;
        }

        Chat.AddInfoMessage($"Loot Data queued {_pendingSnapshotTotal} inventory items for snapshot export.");
    }

    private void OnUpdate()
    {
        if (!_snapshotInProgress || _pendingSnapshotItems.Count == 0)
        {
            return;
        }

        var itemsPerTick = 1;
        while (itemsPerTick-- > 0 && _pendingSnapshotItems.Count > 0)
        {
            var item = _pendingSnapshotItems.Dequeue();
            WritePayload("inventory_snapshot", item);
            _pendingSnapshotWritten++;
        }

        if (_pendingSnapshotItems.Count == 0)
        {
            _snapshotInProgress = false;
            Chat.AddInfoMessage($"Loot Data wrote {_pendingSnapshotWritten} inventory snapshot items.");
        }
    }

    private void WriteEvent(Dictionary<string, object?> payload)
    {
        EnsureLogOpen();
        RotateLogIfNeeded();
        _eventsWriter?.WriteLine(JsonSerializer.Serialize(payload));
        _lastStatus = $"Loot Data writing to {_eventsPath}";
    }

    private static bool LooksLikeLootMessage(ChatMessage message)
    {
        var text = $"{message.Sender} {message.Message}";
        return text.Contains("loot", StringComparison.OrdinalIgnoreCase)
            || text.Contains("receive", StringComparison.OrdinalIgnoreCase)
            || text.Contains("obtained", StringComparison.OrdinalIgnoreCase)
            || text.Contains("acquired", StringComparison.OrdinalIgnoreCase)
            || text.Contains("won", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string?> ParseLootMessage(string message)
    {
        var parsed = new Dictionary<string, string?>();
        var received = Regex.Match(message, @"^(?<who>.+?) (?:receives|received|loots|looted|obtains|obtained|acquires|acquired|won) (?<item>.+?)(?: x(?<quantity>\d+))?\.?$", RegexOptions.IgnoreCase);
        if (received.Success)
        {
            parsed["who"] = received.Groups["who"].Value;
            parsed["item"] = received.Groups["item"].Value;
            parsed["quantity"] = received.Groups["quantity"].Success ? received.Groups["quantity"].Value : null;
        }

        return parsed;
    }

    private void OpenLog()
    {
        CloseLog();
        Directory.CreateDirectory(_outputFolder);
        _eventsPath = Path.Combine(_outputFolder, "loot-events-current.jsonl");
        _eventsWriter = new StreamWriter(new FileStream(_eventsPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        _lastStatus = $"Loot Data writing to {_eventsPath}";
    }

    private void EnsureLogOpen()
    {
        if (_eventsWriter == null)
        {
            OpenLog();
        }
    }

    private void CloseLog()
    {
        _eventsWriter?.Dispose();
        _eventsWriter = null;
    }

    private void ClearLog()
    {
        CloseLog();
        if (File.Exists(_eventsPath))
        {
            File.Delete(_eventsPath);
        }

        _inventoryEventCount = 0;
        _lootChatCount = 0;
        OpenLog();
    }

    private void RotateLogIfNeeded()
    {
        var maxBytes = Math.Max(1, _maxFileMegabytes) * 1024L * 1024L;
        if (!File.Exists(_eventsPath) || new FileInfo(_eventsPath).Length < maxBytes)
        {
            return;
        }

        CloseLog();
        var previousPath = $"{_eventsPath}.previous";
        if (File.Exists(previousPath))
        {
            File.Delete(previousPath);
        }

        File.Move(_eventsPath, previousPath);
        OpenLog();
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
                _eventsPath = Path.Combine(_outputFolder, "loot-events-current.jsonl");
            }

            _includeInventoryEvents = config?.IncludeInventoryEvents ?? _includeInventoryEvents;
            _includeLootChat = config?.IncludeLootChat ?? _includeLootChat;
            _includeRawDump = config?.IncludeRawDump ?? _includeRawDump;
            _maxFileMegabytes = Math.Clamp(config?.MaxFileMegabytes ?? _maxFileMegabytes, 1, 100);
        }
        catch (Exception ex)
        {
            Logger.Error($"Loot Data config failed: {ex}");
        }
    }

    private void SavePathConfig()
    {
        var config = new PathConfig(
            OutputFolder: _outputFolder,
            IncludeInventoryEvents: _includeInventoryEvents,
            IncludeLootChat: _includeLootChat,
            IncludeRawDump: _includeRawDump,
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

    private static string DefaultOutputFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PantheonLootData");

    private string LocalPathConfigPath => Path.Combine(_gameFolder, "Mods", "PantheonAddons", "LootDataConfig.json");

    private sealed record PathConfig(
        string? OutputFolder,
        bool? IncludeInventoryEvents = null,
        bool? IncludeLootChat = null,
        bool? IncludeRawDump = null,
        int? MaxFileMegabytes = null,
        string? DataFolder = null,
        string? Directory = null,
        string? Folder = null);
}
