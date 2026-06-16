using PantheonAddonFramework;
using PantheonAddonFramework.Configuration;
using PantheonAddonFramework.Models;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    private bool _isEnabled;
    private bool _includeInventoryEvents = true;
    private bool _includeLootChat = true;
    private bool _includeRawDump;
    private int _maxFileMegabytes = 5;
    private int _inventoryEventCount;
    private int _lootChatCount;
    private readonly Queue<IInventoryItem> _pendingSnapshotItems = new();
    private readonly Dictionary<long, AcquisitionSource> _recentEntitiesByCharacterId = new();
    private readonly Queue<LootChatClue> _recentLootChatClues = new();
    private int _pendingSnapshotTotal;
    private int _pendingSnapshotWritten;
    private bool _snapshotInProgress;
    private string _lastStatus = "Loot Data idle.";
    private AcquisitionSource? _lastOffensiveTarget;
    private DateTime _lastOffensiveTargetUtc = DateTime.MinValue;

    public override void OnCreate()
    {
        LoadPathConfig();
        CustomChatCommands.Add("/lootdata", HandleCommand);
        LocalPlayerEvents.LocalPlayerEntered.Subscribe(OnLocalPlayerEntered);
        LocalPlayerEvents.ItemAdded.Subscribe(OnItemAdded);
        LocalPlayerEvents.ItemRemoved.Subscribe(OnItemRemoved);
        LocalPlayerEvents.OffensiveTargetChanged.Subscribe(OnOffensiveTargetChanged);
        LocalPlayerEvents.OffensiveTargetHealthChanged.Subscribe(OnOffensiveTargetHealthChanged);
        EntityEvents.EntitySeen.Subscribe(OnEntitySeen);
        EntityEvents.EntityUpdated.Subscribe(OnEntityUpdated);
        EntityEvents.EntityRemoved.Subscribe(OnEntityRemoved);
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
        LocalPlayerEvents.OffensiveTargetChanged.Unsubscribe(OnOffensiveTargetChanged);
        LocalPlayerEvents.OffensiveTargetHealthChanged.Unsubscribe(OnOffensiveTargetHealthChanged);
        EntityEvents.EntitySeen.Unsubscribe(OnEntitySeen);
        EntityEvents.EntityUpdated.Unsubscribe(OnEntityUpdated);
        EntityEvents.EntityRemoved.Unsubscribe(OnEntityRemoved);
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

    private void OnOffensiveTargetChanged(float _)
    {
        CaptureOffensiveTarget();
    }

    private void OnOffensiveTargetHealthChanged(TargetHealthSnapshot _)
    {
        CaptureOffensiveTarget();
    }

    private void OnEntitySeen(EntitySnapshot entity)
    {
        TrackEntity(entity);
    }

    private void OnEntityUpdated(EntitySnapshot entity)
    {
        TrackEntity(entity);
    }

    private void OnEntityRemoved(EntitySnapshot entity)
    {
        TrackEntity(entity);
    }

    private void OnMessageReceived(ChatMessage message)
    {
        if (!_isEnabled || !_includeLootChat || !LooksLikeLootMessage(message))
        {
            return;
        }

        _lootChatCount++;
        var parsed = ParseLootMessage(message.Message);
        var itemName = parsed.TryGetValue("item", out var parsedItem) ? parsedItem : null;
        if (!string.IsNullOrWhiteSpace(itemName))
        {
            _recentLootChatClues.Enqueue(new LootChatClue(DateTime.UtcNow, message.Sender, message.Message, parsed));
            PruneLootChatClues();
        }

        WriteEvent(new Dictionary<string, object?>
        {
            ["timestamp"] = DateTime.Now.ToString("O"),
            ["eventType"] = "loot_chat",
            ["character"] = _localPlayer?.Name,
            ["characterId"] = _localPlayer?.CharacterId,
            ["chatChannel"] = message.ChatChannelType,
            ["sender"] = message.Sender,
            ["message"] = message.Message,
            ["parsed"] = parsed,
            ["acquisitionContext"] = BuildCurrentAcquisitionContext()
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

        var itemId = SafeRead(() => item.Id);
        var itemName = SafeRead(() => item.Name);
        var resolvedItemName = string.IsNullOrWhiteSpace(itemName) ? snapshot?.Name : itemName;
        WriteEvent(new Dictionary<string, object?>
        {
            ["timestamp"] = DateTime.Now.ToString("O"),
            ["eventType"] = eventType,
            ["character"] = _localPlayer?.Name,
            ["characterId"] = _localPlayer?.CharacterId,
            ["itemInstanceId"] = itemId == Guid.Empty ? snapshot?.InstanceId : itemId,
            ["itemName"] = resolvedItemName,
            ["acquisition"] = ResolveAcquisition(eventType, snapshot, resolvedItemName),
            ["item"] = snapshot
        });
    }

    private void TrackEntity(EntitySnapshot entity)
    {
        if (entity.CharacterId == 0)
        {
            return;
        }

        _recentEntitiesByCharacterId[entity.CharacterId] = AcquisitionSource.FromEntity(entity, DateTime.UtcNow);
        PruneEntityMemory();
    }

    private void CaptureOffensiveTarget()
    {
        var target = _localPlayer?.GetOffensiveTarget();
        if (target == null || target.CharacterId == 0)
        {
            return;
        }

        _lastOffensiveTargetUtc = DateTime.UtcNow;
        if (_recentEntitiesByCharacterId.TryGetValue(target.CharacterId, out var entitySource))
        {
            _lastOffensiveTarget = entitySource with
            {
                Name = string.IsNullOrWhiteSpace(target.Name) ? entitySource.Name : target.Name,
                NetworkId = target.NetworkId == 0 ? entitySource.NetworkId : target.NetworkId,
                LastSeenUtc = _lastOffensiveTargetUtc
            };
            return;
        }

        _lastOffensiveTarget = new AcquisitionSource(
            Name: target.Name,
            CharacterId: target.CharacterId,
            NetworkId: target.NetworkId,
            EntityType: "OffensiveTarget",
            Level: 0,
            X: null,
            Y: null,
            Z: null,
            DistanceFromLocal: null,
            HealthPercent: null,
            LastSeenUtc: _lastOffensiveTargetUtc);
    }

    private Dictionary<string, object?>? ResolveAcquisition(string eventType, ItemSnapshot? item, string? itemName)
    {
        if (!eventType.Equals("item_added", StringComparison.OrdinalIgnoreCase) || item == null)
        {
            return null;
        }

        PruneEntityMemory();
        PruneLootChatClues();

        var evidence = new List<string>();
        AcquisitionSource? source = null;
        var method = "unknown";
        var confidence = "none";

        if (item.CorpseId != 0 && _recentEntitiesByCharacterId.TryGetValue(item.CorpseId, out var corpseSource))
        {
            source = corpseSource;
            method = "corpse_id_entity_match";
            confidence = "high";
            evidence.Add($"item.corpseId matched recent entity characterId {item.CorpseId}");
        }

        var lootChatClue = FindMatchingLootChatClue(itemName);
        if (lootChatClue != null)
        {
            evidence.Add($"recent loot chat matched item '{lootChatClue.ItemName}'");

            if (source == null && !string.IsNullOrWhiteSpace(lootChatClue.SourceName))
            {
                source = FindRecentEntityByName(lootChatClue.SourceName);
                method = source == null ? "loot_chat_source_name" : "loot_chat_source_entity_match";
                confidence = source == null ? "medium" : "high";
                evidence.Add($"loot chat source '{lootChatClue.SourceName}'");
            }
            else if (source == null)
            {
                method = "loot_chat_item_match";
                confidence = "medium";
            }
        }

        if (source == null && _lastOffensiveTarget != null && DateTime.UtcNow - _lastOffensiveTargetUtc <= TimeSpan.FromSeconds(30))
        {
            source = _lastOffensiveTarget;
            method = "recent_offensive_target";
            confidence = "low";
            evidence.Add("used offensive target seen within 30 seconds of item add");
        }

        if (source == null && item.CorpseId != 0)
        {
            method = "corpse_id_only";
            confidence = "low";
            evidence.Add($"item carried corpseId {item.CorpseId}, but no matching entity was still remembered");
        }

        return new Dictionary<string, object?>
        {
            ["method"] = method,
            ["confidence"] = confidence,
            ["corpseId"] = item.CorpseId == 0 ? null : item.CorpseId,
            ["source"] = source == null ? null : SourceToPayload(source),
            ["lootChat"] = lootChatClue == null ? null : lootChatClue.ToPayload(),
            ["evidence"] = evidence
        };
    }

    private Dictionary<string, object?> BuildCurrentAcquisitionContext()
    {
        return new Dictionary<string, object?>
        {
            ["offensiveTarget"] = _lastOffensiveTarget == null ? null : SourceToPayload(_lastOffensiveTarget),
            ["offensiveTargetAgeSeconds"] = _lastOffensiveTarget == null ? null : Math.Round((DateTime.UtcNow - _lastOffensiveTargetUtc).TotalSeconds, 1)
        };
    }

    private ResolvedLootChatClue? FindMatchingLootChatClue(string? itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return null;
        }

        foreach (var clue in _recentLootChatClues.Reverse())
        {
            if (DateTime.UtcNow - clue.TimestampUtc > TimeSpan.FromSeconds(12))
            {
                continue;
            }

            clue.Parsed.TryGetValue("item", out var parsedItem);
            if (!IsSameItemName(itemName, parsedItem))
            {
                continue;
            }

            clue.Parsed.TryGetValue("source", out var sourceName);
            return new ResolvedLootChatClue(clue.TimestampUtc, clue.Sender, clue.Message, parsedItem, sourceName);
        }

        return null;
    }

    private AcquisitionSource? FindRecentEntityByName(string sourceName)
    {
        return _recentEntitiesByCharacterId.Values
            .Where(source => source.Name.Equals(sourceName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(source => Math.Abs(source.DistanceFromLocal ?? float.MaxValue))
            .ThenByDescending(source => source.LastSeenUtc)
            .FirstOrDefault();
    }

    private static bool IsSameItemName(string? left, string? right)
    {
        var normalizedLeft = NormalizeLootText(left);
        var normalizedRight = NormalizeLootText(right);
        return !string.IsNullOrWhiteSpace(normalizedLeft)
            && !string.IsNullOrWhiteSpace(normalizedRight)
            && (normalizedLeft.Equals(normalizedRight, StringComparison.OrdinalIgnoreCase)
                || normalizedLeft.Contains(normalizedRight, StringComparison.OrdinalIgnoreCase)
                || normalizedRight.Contains(normalizedLeft, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeLootText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var trimmed = Regex.Replace(value, @"^\s*(?:an?|the)\s+", "", RegexOptions.IgnoreCase).Trim();
        trimmed = Regex.Replace(trimmed, @"\s+x\d+\s*$", "", RegexOptions.IgnoreCase).Trim();
        return trimmed.Trim('.', '!', '"', '\'');
    }

    private void PruneEntityMemory()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        foreach (var key in _recentEntitiesByCharacterId.Where(pair => pair.Value.LastSeenUtc < cutoff).Select(pair => pair.Key).ToArray())
        {
            _recentEntitiesByCharacterId.Remove(key);
        }
    }

    private void PruneLootChatClues()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-30);
        while (_recentLootChatClues.Count > 0 && _recentLootChatClues.Peek().TimestampUtc < cutoff)
        {
            _recentLootChatClues.Dequeue();
        }
    }

    private static Dictionary<string, object?> SourceToPayload(AcquisitionSource source)
    {
        return new Dictionary<string, object?>
        {
            ["name"] = source.Name,
            ["characterId"] = source.CharacterId == 0 ? null : source.CharacterId,
            ["networkId"] = source.NetworkId == 0 ? null : source.NetworkId,
            ["entityType"] = source.EntityType,
            ["level"] = source.Level == 0 ? null : source.Level,
            ["x"] = source.X,
            ["y"] = source.Y,
            ["z"] = source.Z,
            ["distanceFromLocal"] = source.DistanceFromLocal,
            ["healthPercent"] = source.HealthPercent,
            ["lastSeenUtc"] = source.LastSeenUtc.ToString("O")
        };
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
        try
        {
            EnsureLogOpen();
            var line = JsonSerializer.Serialize(payload);
            WriteEventLine(line);
            _lastStatus = $"Loot Data writing to {_eventsPath}";
        }
        catch (Exception ex)
        {
            Logger.Error($"Loot Data write failed: {ex}");
            _lastStatus = $"Loot Data write failed: {ex.GetType().Name}";
        }
    }

    private static T? SafeRead<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return default;
        }
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
        var received = Regex.Match(message, @"^(?<who>.+?) (?:receives|received|loots|looted|obtains|obtained|acquires|acquired|won) (?<item>.+?)(?:\s+(?:from|off|on)\s+(?<source>.+?))?(?: x(?<quantity>\d+))?\.?$", RegexOptions.IgnoreCase);
        if (received.Success)
        {
            parsed["who"] = received.Groups["who"].Value;
            parsed["item"] = NormalizeLootText(received.Groups["item"].Value);
            parsed["source"] = received.Groups["source"].Success ? NormalizeLootText(received.Groups["source"].Value) : null;
            parsed["quantity"] = received.Groups["quantity"].Success ? received.Groups["quantity"].Value : null;
        }

        return parsed;
    }

    private void OpenLog()
    {
        CloseLog();
        Directory.CreateDirectory(_outputFolder);
        _eventsPath = Path.Combine(_outputFolder, "loot-events-current.jsonl");
        WithLogMutex(() =>
        {
            if (!File.Exists(_eventsPath))
            {
                using var _ = new FileStream(_eventsPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            }
        });
        _lastStatus = $"Loot Data writing to {_eventsPath}";
    }

    private void EnsureLogOpen()
    {
        Directory.CreateDirectory(_outputFolder);
    }

    private void CloseLog()
    {
    }

    private void ClearLog()
    {
        WithLogMutex(() =>
        {
            if (File.Exists(_eventsPath))
            {
                File.Delete(_eventsPath);
            }

            using var _ = new FileStream(_eventsPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        });

        _inventoryEventCount = 0;
        _lootChatCount = 0;
        _lastStatus = $"Loot Data writing to {_eventsPath}";
    }

    private void RotateLogIfNeeded()
    {
        var maxBytes = Math.Max(1, _maxFileMegabytes) * 1024L * 1024L;
        if (!File.Exists(_eventsPath) || new FileInfo(_eventsPath).Length < maxBytes)
        {
            return;
        }

        var previousPath = $"{_eventsPath}.previous";
        if (File.Exists(previousPath))
        {
            File.Delete(previousPath);
        }

        File.Move(_eventsPath, previousPath);
    }

    private void WriteEventLine(string line)
    {
        WithLogMutex(() =>
        {
            RotateLogIfNeeded();
            using var stream = new FileStream(_eventsPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.WriteLine(line);
        });
    }

    private void WithLogMutex(Action action)
    {
        var mutexName = BuildLogMutexName(_eventsPath);
        using var mutex = new Mutex(false, mutexName);
        var hasHandle = false;

        try
        {
            try
            {
                hasHandle = mutex.WaitOne(TimeSpan.FromSeconds(10));
                if (!hasHandle)
                {
                    throw new TimeoutException($"Timed out waiting for Loot Data log lock: {mutexName}");
                }
            }
            catch (AbandonedMutexException)
            {
                hasHandle = true;
            }

            action();
        }
        finally
        {
            if (hasHandle)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private static string BuildLogMutexName(string path)
    {
        var fullPath = Path.GetFullPath(path).ToUpperInvariant();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath));
        var hash = Convert.ToHexString(hashBytes);
        return $"PantheonLootData-{hash}";
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

    private sealed record AcquisitionSource(
        string Name,
        long CharacterId,
        uint NetworkId,
        string EntityType,
        int Level,
        float? X,
        float? Y,
        float? Z,
        float? DistanceFromLocal,
        float? HealthPercent,
        DateTime LastSeenUtc)
    {
        public static AcquisitionSource FromEntity(EntitySnapshot entity, DateTime seenUtc)
        {
            return new AcquisitionSource(
                Name: entity.Name,
                CharacterId: entity.CharacterId,
                NetworkId: entity.NetworkId,
                EntityType: entity.EntityType,
                Level: entity.Level,
                X: entity.X,
                Y: entity.Y,
                Z: entity.Z,
                DistanceFromLocal: entity.DistanceFromLocal,
                HealthPercent: entity.HealthPercent,
                LastSeenUtc: seenUtc);
        }
    }

    private sealed record LootChatClue(DateTime TimestampUtc, string Sender, string Message, Dictionary<string, string?> Parsed);

    private sealed record ResolvedLootChatClue(DateTime TimestampUtc, string Sender, string Message, string? ItemName, string? SourceName)
    {
        public Dictionary<string, object?> ToPayload()
        {
            return new Dictionary<string, object?>
            {
                ["timestampUtc"] = TimestampUtc.ToString("O"),
                ["sender"] = Sender,
                ["message"] = Message,
                ["itemName"] = ItemName,
                ["sourceName"] = SourceName
            };
        }
    }
}
