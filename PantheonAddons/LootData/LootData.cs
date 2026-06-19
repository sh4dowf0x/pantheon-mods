using PantheonAddonFramework;
using PantheonAddonFramework.Configuration;
using PantheonAddonFramework.Models;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnityEngine;

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
    private bool _exportIcons = true;
    private bool _iconProbe;
    private string _iconOutputFolder = Path.Combine(DefaultOutputFolder, "icons");
    private string _iconManifestPath = Path.Combine(DefaultOutputFolder, "loot-icons-current.jsonl");
    private int _maxFileMegabytes = 5;
    private int _inventoryEventCount;
    private int _lootChatCount;
    private int _iconExportCount;
    private int _iconExportFailureCount;
    private readonly Queue<IInventoryItem> _pendingSnapshotItems = new();
    private readonly Dictionary<long, AcquisitionSource> _recentEntitiesByCharacterId = new();
    private readonly Queue<LootChatClue> _recentLootChatClues = new();
    private readonly HashSet<string> _observedIconKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _exportAttemptedIconKeys = new(StringComparer.OrdinalIgnoreCase);
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
            new BoolConfigurationValue("Export icons", "Writes one PNG per unique item icon key when the icon sprite can be resolved.", _exportIcons, value => _exportIcons = value),
            new BoolConfigurationValue("Icon probe", "Logs sprite lookup details for icon export troubleshooting.", _iconProbe, value => _iconProbe = value),
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
            Chat.AddInfoMessage("Loot Data: /lootdata status, path, clear, reopen, inventory on|off, chat on|off, raw on|off, icons on|off, iconprobe on|off, snapshot");
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "status":
                Chat.AddInfoMessage($"{_lastStatus} enabled={_isEnabled}; inventoryEvents={_inventoryEventCount}; lootChat={_lootChatCount}; icons={_iconExportCount}; iconFailures={_iconExportFailureCount}; cap={_maxFileMegabytes}MB");
                break;
            case "path":
                Chat.AddInfoMessage($"Loot Data events: {_eventsPath}");
                Chat.AddInfoMessage($"Loot Data icons: {_iconOutputFolder}");
                Chat.AddInfoMessage($"Loot Data icon manifest: {_iconManifestPath}");
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
            case "icons":
                HandleToggle(args, "Icon export", value => _exportIcons = value, _exportIcons);
                break;
            case "iconprobe":
                HandleToggle(args, "Icon probe", value => _iconProbe = value, _iconProbe);
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
        var iconPayload = BuildIconPayload(snapshot, resolvedItemName);
        WriteEvent(new Dictionary<string, object?>
        {
            ["timestamp"] = DateTime.Now.ToString("O"),
            ["eventType"] = eventType,
            ["character"] = _localPlayer?.Name,
            ["characterId"] = _localPlayer?.CharacterId,
            ["itemInstanceId"] = itemId == Guid.Empty ? snapshot?.InstanceId : itemId,
            ["itemName"] = resolvedItemName,
            ["acquisition"] = ResolveAcquisition(eventType, snapshot, resolvedItemName),
            ["icon"] = iconPayload,
            ["item"] = snapshot
        });
    }

    private Dictionary<string, object?>? BuildIconPayload(ItemSnapshot? snapshot, string? itemName)
    {
        var iconKey = GetTemplateField(snapshot, "iconKey");
        if (string.IsNullOrWhiteSpace(iconKey))
        {
            return null;
        }

        var iconFileName = $"{SanitizeFileName(iconKey)}.png";
        var iconPath = Path.Combine(_iconOutputFolder, iconFileName);
        var iconFileReference = BuildIconFileReference(iconPath);
        var payload = new Dictionary<string, object?>
        {
            ["iconKey"] = iconKey,
            ["iconFile"] = iconFileReference
        };

        if (!_exportIcons)
        {
            payload["exportStatus"] = "disabled";
            return payload;
        }

        var result = TryExportIcon(iconKey, iconPath);
        payload["exportStatus"] = result.Status;
        payload["exportMessage"] = result.Message;
        payload["spriteName"] = result.SpriteName;
        payload["textureName"] = result.TextureName;
        payload["width"] = result.Width;
        payload["height"] = result.Height;
        WriteIconManifest(iconKey, iconFileReference, result, snapshot, itemName);
        return payload;
    }

    private IconExportResult TryExportIcon(string iconKey, string iconPath)
    {
        if (File.Exists(iconPath))
        {
            return new IconExportResult("exists", null, null, null, null, null);
        }

        if (!_exportAttemptedIconKeys.Add(iconKey))
        {
            return new IconExportResult("pending_or_failed", "Icon export was already attempted this session.", null, null, null, null);
        }

        try
        {
            Directory.CreateDirectory(_iconOutputFolder);
            var sprite = FindIconSprite(iconKey);
            if (sprite == null)
            {
                _iconExportFailureCount++;
                LogIconProbe(iconKey, "No loaded sprite matched the icon key.");
                return new IconExportResult("unresolved", "No loaded sprite matched the icon key.", null, null, null, null);
            }

            var png = EncodeSpriteToPng(sprite);
            File.WriteAllBytes(iconPath, png);
            _iconExportCount++;
            return new IconExportResult("exported", null, sprite.name, sprite.texture?.name, Math.Round(sprite.rect.width), Math.Round(sprite.rect.height));
        }
        catch (Exception ex)
        {
            _iconExportFailureCount++;
            LogIconProbe(iconKey, $"{ex.GetType().Name}: {ex.Message}");
            return new IconExportResult("failed", $"{ex.GetType().Name}: {ex.Message}", null, null, null, null);
        }
    }

    private Sprite? FindIconSprite(string iconKey)
    {
        var normalizedIconKey = NormalizeIconKey(iconKey);

        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (sprite == null || string.IsNullOrWhiteSpace(sprite.name))
            {
                continue;
            }

            var normalizedSpriteName = NormalizeIconKey(sprite.name);
            if (normalizedSpriteName.Equals(normalizedIconKey, StringComparison.OrdinalIgnoreCase))
            {
                return sprite;
            }
        }

        return null;
    }

    private static byte[] EncodeSpriteToPng(Sprite sprite)
    {
        var texture = sprite.texture ?? throw new InvalidOperationException("Sprite has no texture.");
        var rect = ResolveSpriteTextureRect(sprite);
        var width = Math.Max(1, (int)Math.Round(rect.width));
        var height = Math.Max(1, (int)Math.Round(rect.height));

        try
        {
            var cropped = new Texture2D(width, height, TextureFormat.RGBA32, false);
            cropped.SetPixels(texture.GetPixels((int)Math.Round(rect.x), (int)Math.Round(rect.y), width, height));
            cropped.Apply();
            return ImageConversion.EncodeToPNG(cropped).ToArray();
        }
        catch
        {
            return EncodeSpriteToPngViaRenderTexture(texture, rect, width, height);
        }
    }

    private static Rect ResolveSpriteTextureRect(Sprite sprite)
    {
        try
        {
            return sprite.textureRect;
        }
        catch
        {
            return sprite.rect;
        }
    }

    private static byte[] EncodeSpriteToPngViaRenderTexture(Texture2D texture, Rect rect, int width, int height)
    {
        var previousActive = RenderTexture.active;
        var atlasCopy = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);

        try
        {
            Graphics.Blit(texture, atlasCopy);
            RenderTexture.active = atlasCopy;

            var cropped = new Texture2D(width, height, TextureFormat.RGBA32, false);
            cropped.ReadPixels(new Rect((int)Math.Round(rect.x), (int)Math.Round(rect.y), width, height), 0, 0);
            cropped.Apply();
            return ImageConversion.EncodeToPNG(cropped).ToArray();
        }
        finally
        {
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(atlasCopy);
        }
    }

    private void WriteIconManifest(string iconKey, string iconFileReference, IconExportResult result, ItemSnapshot? snapshot, string? itemName)
    {
        if (!_observedIconKeys.Add(iconKey) && !result.Status.Equals("exported", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var payload = new Dictionary<string, object?>
        {
            ["timestamp"] = DateTime.Now.ToString("O"),
            ["eventType"] = "icon_observed",
            ["iconKey"] = iconKey,
            ["iconFile"] = iconFileReference,
            ["exportStatus"] = result.Status,
            ["exportMessage"] = result.Message,
            ["itemId"] = snapshot?.ItemId,
            ["itemName"] = itemName ?? snapshot?.Name,
            ["templateItemId"] = GetTemplateField(snapshot, "itemId"),
            ["templateItemKey"] = GetTemplateField(snapshot, "itemKey"),
            ["spriteName"] = result.SpriteName,
            ["textureName"] = result.TextureName,
            ["width"] = result.Width,
            ["height"] = result.Height
        };

        try
        {
            Directory.CreateDirectory(_outputFolder);
            File.AppendAllText(_iconManifestPath, JsonSerializer.Serialize(payload) + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Logger.Error($"Loot Data icon manifest write failed: {ex}");
        }
    }

    private void LogIconProbe(string iconKey, string message)
    {
        if (_iconProbe)
        {
            Logger.Info($"Loot Data icon probe '{iconKey}': {message}");
        }
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
            _exportIcons = config?.ExportIcons ?? _exportIcons;
            _iconProbe = config?.IconProbe ?? _iconProbe;
            _maxFileMegabytes = Math.Clamp(config?.MaxFileMegabytes ?? _maxFileMegabytes, 1, 100);

            var configuredIconFolder = ExpandConfiguredPath(config?.IconOutputFolder);
            _iconOutputFolder = string.IsNullOrWhiteSpace(configuredIconFolder)
                ? Path.Combine(_outputFolder, "icons")
                : configuredIconFolder;
            _iconManifestPath = Path.Combine(_outputFolder, "loot-icons-current.jsonl");
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
            ExportIcons: _exportIcons,
            IconProbe: _iconProbe,
            MaxFileMegabytes: _maxFileMegabytes,
            IconOutputFolder: _iconOutputFolder);

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

    private static string? GetTemplateField(ItemSnapshot? snapshot, string key)
    {
        if (snapshot?.Template == null)
        {
            return null;
        }

        return snapshot.Template.TryGetValue(key, out var value) ? value : null;
    }

    private static string SanitizeFileName(string value)
    {
        var sanitized = Regex.Replace(value.Trim(), @"[^A-Za-z0-9._-]+", "_").Trim('_', '.', '-');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "icon";
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).Substring(0, 8).ToLowerInvariant();
        return $"{sanitized}_{hash}";
    }

    private static string NormalizeIconKey(string value)
    {
        return Regex.Replace(value.Trim(), @"[^A-Za-z0-9]+", "", RegexOptions.CultureInvariant);
    }

    private string BuildIconFileReference(string iconPath)
    {
        try
        {
            var outputRoot = Path.GetFullPath(_outputFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullIconPath = Path.GetFullPath(iconPath);
            if (fullIconPath.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetRelativePath(_outputFolder, fullIconPath);
            }

            return fullIconPath;
        }
        catch
        {
            return iconPath;
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

    private static string DefaultOutputFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PantheonLootData");

    private string LocalPathConfigPath => Path.Combine(_gameFolder, "Mods", "PantheonAddons", "LootDataConfig.json");

    private sealed record PathConfig(
        string? OutputFolder,
        bool? IncludeInventoryEvents = null,
        bool? IncludeLootChat = null,
        bool? IncludeRawDump = null,
        bool? ExportIcons = null,
        bool? IconProbe = null,
        int? MaxFileMegabytes = null,
        string? IconOutputFolder = null,
        string? DataFolder = null,
        string? Directory = null,
        string? Folder = null);

    private sealed record IconExportResult(
        string Status,
        string? Message,
        string? SpriteName,
        string? TextureName,
        double? Width,
        double? Height);

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
