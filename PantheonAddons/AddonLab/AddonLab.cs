using PantheonAddonFramework;
using PantheonAddonFramework.Configuration;
using PantheonAddonFramework.Models;
using PantheonAddonFramework.UI;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PantheonAddons.AddonLab;

[AddonMetadata("Addon Lab", "Codex", "A lightweight probe for exploring addon framework events")]
public sealed class AddonLab : Addon
{
    private const long MaxLiveLogBytes = 5 * 1024 * 1024;
    private const int MaxBufferedCombatLines = 2000;

    private readonly Queue<string> _lines = new();
    private readonly List<string> _combatLines = new();
    private readonly Action<float> _offensiveTargetChanged;
    private readonly Action<float> _defensiveTargetChanged;
    private readonly Action<IInventoryItem> _itemAdded;
    private readonly Action<IInventoryItem> _itemRemoved;
    private readonly Action<IPlayer> _playerAdded;
    private IAddonWindow? _window;
    private IAddonTextComponent? _text;
    private bool _isEnabled;
    private bool _wantsWindow;
    private bool _showChatMessages;
    private bool _combatLoggingEnabled = true;
    private bool _reportedWindowFailure;
    private int _maxLines = 12;
    private DateTime _combatStartedAt;
    private StreamWriter? _liveCombatWriter;
    private StreamWriter? _liveCombatJsonWriter;
    private string? _liveCombatLogPath;
    private string? _liveCombatJsonPath;

    public AddonLab()
    {
        _offensiveTargetChanged = percent => RecordEvent("Target", $"Offensive target health {percent:F0}%");
        _defensiveTargetChanged = percent => RecordEvent("Target", $"Defensive target health {percent:F0}%");
        _itemAdded = item => RecordEvent("Inventory", $"Item added: {item.Name} ({item.Id})");
        _itemRemoved = item => RecordEvent("Inventory", $"Item removed: {item.Name} ({item.Id})");
        _playerAdded = player => RecordEvent("Player", $"Player seen: {player.Name} L{player.Level} {player.Race} {player.Class} Local={player.IsLocalPlayer}");
    }

    public override void OnCreate()
    {
        CustomChatCommands.Add("/addonlab", HandleCommand);

        LocalPlayerEvents.LocalPlayerEntered.Subscribe(OnLocalPlayerEntered);
        LocalPlayerEvents.ExperienceChanged.Subscribe(OnExperienceChanged);
        LocalPlayerEvents.OffensiveTargetChanged.Subscribe(_offensiveTargetChanged);
        LocalPlayerEvents.DefensiveTargetChanged.Subscribe(_defensiveTargetChanged);
        LocalPlayerEvents.ItemAdded.Subscribe(_itemAdded);
        LocalPlayerEvents.ItemRemoved.Subscribe(_itemRemoved);
        PlayerEvents.PlayerAdded.Subscribe(_playerAdded);
        ChatEvents.MessageReceived.Subscribe(OnMessageReceived);
        CombatEvents.CombatResultApplied.Subscribe(OnCombatResultApplied);
        LifecycleEvents.OnUpdate.Subscribe(OnUpdate);

        _combatStartedAt = DateTime.Now;
        OpenLiveCombatLog();
    }

    public override void Enable()
    {
        _isEnabled = true;
        AddLine("Addon Lab enabled. Type /addonlab help.");
    }

    public override void Disable()
    {
        _isEnabled = false;
        _window?.Enable(false);
        _text?.Enable(false);
    }

    public override IEnumerable<IConfigurationValue> GetConfiguration()
    {
        return new IConfigurationValue[]
        {
            new BoolConfigurationValue("Show chat events", "Shows observed chat messages in the lab window.", false, value => _showChatMessages = value),
            new IntConfigurationValue("Max lines", "How many recent lab lines to show.", 12, 4, 24, 1, value =>
            {
                _maxLines = value;
                TrimLines();
                RefreshText();
            })
        };
    }

    public override void Dispose()
    {
        CustomChatCommands.Remove("/addonlab");

        LocalPlayerEvents.LocalPlayerEntered.Unsubscribe(OnLocalPlayerEntered);
        LocalPlayerEvents.ExperienceChanged.Unsubscribe(OnExperienceChanged);
        LocalPlayerEvents.OffensiveTargetChanged.Unsubscribe(_offensiveTargetChanged);
        LocalPlayerEvents.DefensiveTargetChanged.Unsubscribe(_defensiveTargetChanged);
        LocalPlayerEvents.ItemAdded.Unsubscribe(_itemAdded);
        LocalPlayerEvents.ItemRemoved.Unsubscribe(_itemRemoved);
        PlayerEvents.PlayerAdded.Unsubscribe(_playerAdded);
        ChatEvents.MessageReceived.Unsubscribe(OnMessageReceived);
        CombatEvents.CombatResultApplied.Unsubscribe(OnCombatResultApplied);
        LifecycleEvents.OnUpdate.Unsubscribe(OnUpdate);

        CloseLiveCombatLog();
        _text?.Destroy();
        _window?.Destroy();
    }

    private void HandleCommand(string[] args)
    {
        if (args.Length == 0 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage("Addon Lab: /addonlab show, hide, clear, player, macros, chat on|off, combat status|clear|path|save");
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "show":
                _wantsWindow = true;
                if (TryEnsureWindow(true))
                {
                    _window?.Enable(true);
                    _text?.Enable(true);
                    AddLine("Window shown.");
                }
                break;
            case "hide":
                _wantsWindow = false;
                _window?.Enable(false);
                _text?.Enable(false);
                Chat.AddInfoMessage("Addon Lab window hidden. Use /addonlab show to reopen.");
                break;
            case "clear":
                _lines.Clear();
                RefreshText();
                break;
            case "player":
                AddPlayerSnapshot();
                break;
            case "macros":
                AddMacroSnapshot();
                break;
            case "chat":
                HandleChatCommand(args);
                break;
            case "combat":
                HandleCombatCommand(args);
                break;
            default:
                Chat.AddInfoMessage("Addon Lab: unknown command. Try /addonlab help.");
                break;
        }
    }

    private void OnUpdate()
    {
        if (_isEnabled && _wantsWindow && _window == null)
        {
            TryEnsureWindow();
        }
    }

    private bool TryEnsureWindow(bool notifyChat = false)
    {
        if (_window != null)
        {
            return true;
        }

        try
        {
            _window = CustomUI.CreateWindow("Addon Lab", 520, 260);
            _text = _window.AddTextComponent("");
            _text.SetSize(500, 240);
            _text.SetFontSize(14);
            RefreshText();
            return true;
        }
        catch (Exception ex)
        {
            if (notifyChat)
            {
                Chat.AddInfoMessage($"Addon Lab could not create its window: {ex.GetType().Name}: {ex.Message}");
            }

            if (!_reportedWindowFailure)
            {
                _reportedWindowFailure = true;
                Logger.Error($"Addon Lab window creation failed: {ex}");
            }

            return false;
        }
    }

    private void OnLocalPlayerEntered(IPlayer player)
    {
        if (!player.IsLocalPlayer)
        {
            return;
        }

        RecordEvent("LocalPlayer", $"Local: {player.Name} L{player.Level} {player.Race} {player.Class} CharacterId={player.CharacterId}");
    }

    private void OnExperienceChanged(PlayerExperience experience)
    {
        RecordEvent("Experience", $"XP: {experience.Current:N0}/{experience.ToNextLevel:N0} ({experience.ExperiencePercentage * 100:F1}%)");
    }

    private void OnMessageReceived(ChatMessage message)
    {
        RecordCombatLine("Chat", $"[{message.ChatChannelType}] {message.Sender}: {message.Message}");

        if (_showChatMessages)
        {
            AddLine($"Chat[{message.ChatChannelType}] {message.Sender}: {message.Message}");
        }
    }

    private void OnCombatResultApplied(CombatResultApplied result)
    {
        var ability = string.IsNullOrWhiteSpace(result.AbilityName) ? "" : $" ability=\"{result.AbilityName}\"";
        var buff = string.IsNullOrWhiteSpace(result.BuffName) ? "" : $" buff=\"{result.BuffName}\"";
        RecordCombatLine(
            "CombatResult",
            $"{result.AttackerName} -> {result.DefenderName} damage={result.Damage:F1} beforeMitigation={result.BeforeMitigationDamage:F1} mitigated={result.MitigatedDamage:F1} result={result.CombatResultType} type={result.DamageType} style={result.DamageStyle} weapon={result.WeaponType} impact={result.ImpactType}{ability}{buff}");
    }

    private void AddPlayerSnapshot()
    {
        AddLine("Player snapshot is event-driven; target, XP, and inventory updates will appear as they fire.");
    }

    private void AddMacroSnapshot()
    {
        var macros = Macros.GetAll().Select(macro => macro.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();

        if (macros.Length == 0)
        {
            AddLine("No visible macro buttons found.");
            return;
        }

        AddLine($"Macros: {string.Join(", ", macros)}");
    }

    private void HandleChatCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Chat.AddInfoMessage($"Addon Lab chat display is {(_showChatMessages ? "on" : "off")}.");
            return;
        }

        _showChatMessages = args[1].Equals("on", StringComparison.OrdinalIgnoreCase);
        AddLine($"Chat display {(_showChatMessages ? "enabled" : "disabled")}.");
    }

    private void HandleCombatCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Chat.AddInfoMessage("Addon Lab combat: status, clear, path, save");
            return;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "start":
                _combatLoggingEnabled = true;
                OpenLiveCombatLog();
                Chat.AddInfoMessage($"Addon Lab combat logging is always on; live={_liveCombatLogPath}; json={_liveCombatJsonPath}");
                break;
            case "stop":
                Chat.AddInfoMessage("Addon Lab combat logging is always on. Use /addonlab combat clear to reset the live files.");
                break;
            case "save":
                SaveCombatLog();
                break;
            case "status":
                Chat.AddInfoMessage($"Combat logging {(_combatLoggingEnabled ? "running" : "stopped")}; buffered={_combatLines.Count}; cap={MaxLiveLogBytes / 1024 / 1024}MB; live={_liveCombatLogPath ?? "none"}; json={_liveCombatJsonPath ?? "none"}");
                break;
            case "path":
                Chat.AddInfoMessage($"Addon Lab live combat log: {_liveCombatLogPath ?? "none"}; json={_liveCombatJsonPath ?? "none"}");
                break;
            case "clear":
                _combatLines.Clear();
                ResetLiveCombatLog();
                AddLine("Combat log cleared.");
                break;
            default:
                Chat.AddInfoMessage("Addon Lab combat: status, clear, path, save");
                break;
        }
    }

    private void RecordEvent(string category, string message)
    {
        AddLine($"{category}: {message}");
        RecordCombatLine(category, message);
    }

    private void RecordCombatLine(string category, string message)
    {
        if (!_combatLoggingEnabled)
        {
            return;
        }

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{category}] {message}";
        _combatLines.Add(line);
        TrimCombatLines();
        EnsureLiveCombatLog();
        RotateLiveCombatLogsIfNeeded();
        _liveCombatWriter?.WriteLine(line);
        _liveCombatJsonWriter?.WriteLine(ToCombatJson(category, message));
        Logger.Info($"CombatProbe {line}");
    }

    private void OpenLiveCombatLog()
    {
        CloseLiveCombatLog();

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PantheonAddons", "AddonLabLogs");
        Directory.CreateDirectory(folder);

        _liveCombatLogPath = Path.Combine(folder, "combat-live-current.txt");
        _liveCombatJsonPath = Path.Combine(folder, "combat-live-current.jsonl");

        var stream = new FileStream(_liveCombatLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _liveCombatWriter = new StreamWriter(stream) { AutoFlush = true };

        var jsonStream = new FileStream(_liveCombatJsonPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _liveCombatJsonWriter = new StreamWriter(jsonStream) { AutoFlush = true };
    }

    private void CloseLiveCombatLog()
    {
        _liveCombatWriter?.Dispose();
        _liveCombatWriter = null;
        _liveCombatJsonWriter?.Dispose();
        _liveCombatJsonWriter = null;
    }

    private void EnsureLiveCombatLog()
    {
        if (_liveCombatWriter == null || _liveCombatJsonWriter == null)
        {
            OpenLiveCombatLog();
        }
    }

    private void ResetLiveCombatLog()
    {
        CloseLiveCombatLog();

        if (!string.IsNullOrWhiteSpace(_liveCombatLogPath))
        {
            File.Delete(_liveCombatLogPath);
        }

        if (!string.IsNullOrWhiteSpace(_liveCombatJsonPath))
        {
            File.Delete(_liveCombatJsonPath);
        }

        OpenLiveCombatLog();
    }

    private void RotateLiveCombatLogsIfNeeded()
    {
        if (IsUnderSizeLimit(_liveCombatLogPath) && IsUnderSizeLimit(_liveCombatJsonPath))
        {
            return;
        }

        CloseLiveCombatLog();
        RotateFile(_liveCombatLogPath);
        RotateFile(_liveCombatJsonPath);
        OpenLiveCombatLog();
    }

    private static bool IsUnderSizeLimit(string? path)
    {
        return string.IsNullOrWhiteSpace(path) || !File.Exists(path) || new FileInfo(path).Length < MaxLiveLogBytes;
    }

    private static void RotateFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        var previousPath = $"{path}.previous";
        File.Delete(previousPath);
        File.Move(path, previousPath);
    }

    private static string ToCombatJson(string category, string message)
    {
        var payload = new Dictionary<string, object?>
        {
            ["timestamp"] = DateTime.Now.ToString("O"),
            ["category"] = category,
            ["raw"] = message
        };

        AddCombatMessageFields(payload, message);
        return JsonSerializer.Serialize(payload);
    }

    private static void AddCombatMessageFields(Dictionary<string, object?> payload, string message)
    {
        var combatText = ExtractCombatText(payload, message);
        if (string.IsNullOrWhiteSpace(combatText))
        {
            return;
        }

        var damage = Regex.Match(combatText, @"^(?<source>.+?) dealt (?<amount>\d+) (?<damageType>\w+) damage to (?<target>.+?) with (?<ability>.+?)\. \((?<mitigated>\d+) mitigated\)(?<flags>.*)$");
        if (damage.Success)
        {
            payload["eventType"] = "damage";
            payload["source"] = damage.Groups["source"].Value;
            payload["target"] = damage.Groups["target"].Value;
            payload["amount"] = int.Parse(damage.Groups["amount"].Value);
            payload["damageType"] = damage.Groups["damageType"].Value;
            payload["ability"] = damage.Groups["ability"].Value;
            payload["mitigated"] = int.Parse(damage.Groups["mitigated"].Value);
            AddFlags(payload, damage.Groups["flags"].Value);
            return;
        }

        var resisted = Regex.Match(combatText, @"^(?<source>.+?)'s (?<ability>.+?) was fully resisted by (?<target>.+?)\.$");
        if (resisted.Success)
        {
            payload["eventType"] = "resist";
            payload["source"] = resisted.Groups["source"].Value;
            payload["target"] = resisted.Groups["target"].Value;
            payload["ability"] = resisted.Groups["ability"].Value;
            payload["resisted"] = true;
            return;
        }

        var missed = Regex.Match(combatText, @"^(?<source>.+?)'s (?<ability>.+?) missed (?<target>.+?)\.$");
        if (missed.Success)
        {
            payload["eventType"] = "miss";
            payload["source"] = missed.Groups["source"].Value;
            payload["target"] = missed.Groups["target"].Value;
            payload["ability"] = missed.Groups["ability"].Value;
            payload["missed"] = true;
            return;
        }

        var sourceHealing = Regex.Match(combatText, @"^(?<source>.+?)'s (?<ability>.+?) healed (?<target>.+?) for (?<amount>\d+)\.$");
        if (sourceHealing.Success)
        {
            payload["eventType"] = "healing";
            payload["source"] = sourceHealing.Groups["source"].Value;
            payload["target"] = sourceHealing.Groups["target"].Value;
            payload["ability"] = sourceHealing.Groups["ability"].Value;
            payload["amount"] = int.Parse(sourceHealing.Groups["amount"].Value);
            return;
        }

        var passiveHealing = Regex.Match(combatText, @"^(?<target>.+?) was healed for (?<amount>\d+) by (?<ability>.+?)\.$");
        if (passiveHealing.Success)
        {
            payload["eventType"] = "healing";
            payload["target"] = passiveHealing.Groups["target"].Value;
            payload["ability"] = passiveHealing.Groups["ability"].Value;
            payload["amount"] = int.Parse(passiveHealing.Groups["amount"].Value);
        }
    }

    private static string ExtractCombatText(Dictionary<string, object?> payload, string message)
    {
        var match = Regex.Match(message, @"^\[(?<channel>[^\]]+)\]\s*(?<sender>.*?):\s*(?<metadata>\[[^\]]+\])?\s*(?<text>.*)$");
        if (!match.Success)
        {
            return "";
        }

        payload["chatChannel"] = match.Groups["channel"].Value;
        payload["sender"] = match.Groups["sender"].Value;

        var metadata = match.Groups["metadata"].Value;
        if (!string.IsNullOrWhiteSpace(metadata))
        {
            payload["combatFilter"] = metadata.Trim('[', ']');
        }

        return match.Groups["text"].Value;
    }

    private static void AddFlags(Dictionary<string, object?> payload, string flags)
    {
        payload["critical"] = flags.Contains("Critical", StringComparison.OrdinalIgnoreCase);
        payload["absorbed"] = flags.Contains("Absorbed", StringComparison.OrdinalIgnoreCase);
        payload["immune"] = flags.Contains("Immune", StringComparison.OrdinalIgnoreCase);
    }

    private void SaveCombatLog()
    {
        if (_combatLines.Count == 0)
        {
            Chat.AddInfoMessage("Addon Lab combat log is empty.");
            return;
        }

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PantheonAddons", "AddonLabLogs");
        Directory.CreateDirectory(folder);

        var startedAt = _combatStartedAt == default ? DateTime.Now : _combatStartedAt;
        var filePath = Path.Combine(folder, $"combat-{startedAt:yyyyMMdd-HHmmss}.txt");
        File.WriteAllLines(filePath, _combatLines);

        Chat.AddInfoMessage($"Addon Lab saved {_combatLines.Count} combat lines: {filePath}");
        AddLine($"Saved combat log: {Path.GetFileName(filePath)}");
    }

    private void AddLine(string line)
    {
        _lines.Enqueue($"{DateTime.Now:HH:mm:ss} {line}");
        TrimLines();
        if (_wantsWindow && _window != null)
        {
            RefreshText();
        }

        Logger.Info(line);
    }

    private void TrimLines()
    {
        while (_lines.Count > _maxLines)
        {
            _lines.Dequeue();
        }
    }

    private void TrimCombatLines()
    {
        if (_combatLines.Count <= MaxBufferedCombatLines)
        {
            return;
        }

        _combatLines.RemoveRange(0, _combatLines.Count - MaxBufferedCombatLines);
    }

    private void RefreshText()
    {
        _text?.SetText(string.Join(Environment.NewLine, _lines));
    }
}
