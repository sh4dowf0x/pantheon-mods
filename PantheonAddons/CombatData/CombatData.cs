using PantheonAddonFramework;
using PantheonAddonFramework.Configuration;
using PantheonAddonFramework.Models;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PantheonAddons.CombatData;

[AddonMetadata("Combat Data", "Codex", "Exports Pantheon combat log data to text and JSONL files")]
public sealed class CombatData : Addon
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ConfigJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const int MaxBufferedLines = 2000;

    private readonly string _gameFolder = ResolveGameFolder();
    private readonly List<string> _bufferedLines = new();
    private string _outputFolder = DefaultOutputFolder;
    private string _textLogPath = Path.Combine(DefaultOutputFolder, "combat-live-current.txt");
    private string _jsonLogPath = Path.Combine(DefaultOutputFolder, "combat-live-current.jsonl");
    private StreamWriter? _textWriter;
    private StreamWriter? _jsonWriter;
    private IPlayer? _localPlayer;
    private PlayerExperience? _lastExperience;
    private bool _isEnabled;
    private bool _includeCombatMessages = true;
    private bool _includeStructuredResults = true;
    private bool _includeExperience = true;
    private int _maxFileMegabytes = 5;
    private DateTime _startedAt;
    private int _messageCount;
    private int _structuredCount;
    private int _experienceCount;
    private string _lastStatus = "Combat Data idle.";

    public override void OnCreate()
    {
        LoadPathConfig();
        CustomChatCommands.Add("/combatdata", HandleCommand);
        ChatEvents.MessageReceived.Subscribe(OnMessageReceived);
        CombatEvents.CombatResultApplied.Subscribe(OnCombatResultApplied);
        LocalPlayerEvents.LocalPlayerEntered.Subscribe(OnLocalPlayerEntered);
        LocalPlayerEvents.LocalPlayerLeft.Subscribe(OnLocalPlayerLeft);
        LocalPlayerEvents.ExperienceChanged.Subscribe(OnExperienceChanged);
        _startedAt = DateTime.Now;
        OpenLogs();
    }

    public override void Enable()
    {
        _isEnabled = true;
        EnsureLogsOpen();
    }

    public override void Disable()
    {
        _isEnabled = false;
    }

    public override IEnumerable<IConfigurationValue> GetConfiguration()
    {
        return new IConfigurationValue[]
        {
            new BoolConfigurationValue("Combat messages", "Writes rendered combat-log messages.", _includeCombatMessages, value => _includeCombatMessages = value),
            new BoolConfigurationValue("Structured results", "Writes structured combat-result events when available.", _includeStructuredResults, value => _includeStructuredResults = value),
            new BoolConfigurationValue("Experience changes", "Writes local player experience changes.", _includeExperience, value => _includeExperience = value),
            new IntConfigurationValue("Max file MB", "Rotates live files when either log reaches this size.", _maxFileMegabytes, 1, 100, 1, value => _maxFileMegabytes = value)
        };
    }

    public override void Dispose()
    {
        CustomChatCommands.Remove("/combatdata");
        ChatEvents.MessageReceived.Unsubscribe(OnMessageReceived);
        CombatEvents.CombatResultApplied.Unsubscribe(OnCombatResultApplied);
        LocalPlayerEvents.LocalPlayerEntered.Unsubscribe(OnLocalPlayerEntered);
        LocalPlayerEvents.LocalPlayerLeft.Unsubscribe(OnLocalPlayerLeft);
        LocalPlayerEvents.ExperienceChanged.Unsubscribe(OnExperienceChanged);
        CloseLogs();
    }

    private void HandleCommand(string[] args)
    {
        if (args.Length == 0 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage("Combat Data: /combatdata status, path, clear, save, reopen, messages on|off, structured on|off, xp on|off");
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "status":
                Chat.AddInfoMessage($"{_lastStatus} enabled={_isEnabled}; messages={_messageCount}; structured={_structuredCount}; xp={_experienceCount}; buffered={_bufferedLines.Count}; cap={_maxFileMegabytes}MB");
                break;
            case "path":
                Chat.AddInfoMessage($"Combat Data text: {_textLogPath}");
                Chat.AddInfoMessage($"Combat Data json: {_jsonLogPath}");
                Chat.AddInfoMessage($"Combat Data config: {LocalPathConfigPath}");
                break;
            case "clear":
                ClearLogs();
                Chat.AddInfoMessage("Combat Data live logs cleared.");
                break;
            case "save":
                SaveSnapshot();
                break;
            case "reopen":
                OpenLogs();
                Chat.AddInfoMessage("Combat Data logs reopened.");
                break;
            case "messages":
                HandleToggle(args, "Combat messages", value => _includeCombatMessages = value, _includeCombatMessages);
                break;
            case "structured":
                HandleToggle(args, "Structured results", value => _includeStructuredResults = value, _includeStructuredResults);
                break;
            case "xp":
            case "experience":
                HandleToggle(args, "Experience changes", value => _includeExperience = value, _includeExperience);
                break;
            default:
                Chat.AddInfoMessage("Combat Data: unknown command. Try /combatdata help.");
                break;
        }
    }

    private void HandleToggle(string[] args, string label, Action<bool> setter, bool current)
    {
        if (args.Length < 2 || args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage($"Combat Data {label.ToLowerInvariant()} is {(current ? "on" : "off")}.");
            return;
        }

        if (args[1].Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            setter(true);
            SavePathConfig();
            Chat.AddInfoMessage($"Combat Data {label.ToLowerInvariant()} enabled.");
            return;
        }

        if (args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            setter(false);
            SavePathConfig();
            Chat.AddInfoMessage($"Combat Data {label.ToLowerInvariant()} disabled.");
            return;
        }

        Chat.AddInfoMessage($"Combat Data {label.ToLowerInvariant()}: on, off, status");
    }

    private void OnMessageReceived(ChatMessage message)
    {
        if (!_isEnabled || !_includeCombatMessages || !IsCombatMessage(message))
        {
            return;
        }

        _messageCount++;
        var rendered = $"[{message.ChatChannelType}] {message.Sender}: {message.Message}";
        RecordLine("CombatMessage", rendered, CreateCombatMessagePayload(message));
    }

    private void OnCombatResultApplied(CombatResultApplied result)
    {
        if (!_isEnabled || !_includeStructuredResults)
        {
            return;
        }

        _structuredCount++;
        var ability = string.IsNullOrWhiteSpace(result.AbilityName) ? "" : $" ability=\"{result.AbilityName}\"";
        var buff = string.IsNullOrWhiteSpace(result.BuffName) ? "" : $" buff=\"{result.BuffName}\"";
        var rendered =
            $"{result.AttackerName} -> {result.DefenderName} damage={result.Damage:F1} beforeMitigation={result.BeforeMitigationDamage:F1} mitigated={result.MitigatedDamage:F1} result={result.CombatResultType} type={result.DamageType} style={result.DamageStyle} weapon={result.WeaponType} impact={result.ImpactType}{ability}{buff}";

        RecordLine("CombatResult", rendered, CreateCombatResultPayload(result, rendered));
    }

    private void OnExperienceChanged(PlayerExperience experience)
    {
        var previous = _lastExperience;
        _lastExperience = experience;

        if (!_isEnabled || !_includeExperience)
        {
            return;
        }

        _experienceCount++;
        var deltaCurrent = previous == null ? (double?)null : experience.Current - previous.Current;
        var deltaToNext = previous == null ? (double?)null : experience.ToNextLevel - previous.ToNextLevel;
        var deltaPercentage = previous == null ? (float?)null : experience.ExperiencePercentage - previous.ExperiencePercentage;
        var rendered = deltaCurrent == null
            ? $"XP current={experience.Current:F0} toNext={experience.ToNextLevel:F0} percent={experience.ExperiencePercentage:F4}"
            : $"XP current={experience.Current:F0} delta={deltaCurrent.Value:F0} toNext={experience.ToNextLevel:F0} percent={experience.ExperiencePercentage:F4} deltaPercent={deltaPercentage.GetValueOrDefault():F4}";

        RecordLine("ExperienceChanged", rendered, CreateExperiencePayload(experience, previous, rendered));
    }

    private void RecordLine(string category, string rendered, Dictionary<string, object?> payload)
    {
        EnsureLogsOpen();
        RotateLogsIfNeeded();
        AddSourceFields(payload);

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{category}] {rendered}";
        _bufferedLines.Add(line);
        TrimBuffer();

        _textWriter?.WriteLine(line);
        _jsonWriter?.WriteLine(JsonSerializer.Serialize(payload));
        _lastStatus = $"Combat Data writing to {_jsonLogPath}";
    }

    private void OnLocalPlayerEntered(IPlayer player)
    {
        _localPlayer = player;
        _lastExperience = player.GetExperience();
        OpenLogs();
    }

    private void OnLocalPlayerLeft(IPlayer player)
    {
        if (_localPlayer?.CharacterId == player.CharacterId)
        {
            _localPlayer = null;
            _lastExperience = null;
            OpenLogs();
        }
    }

    private void AddSourceFields(Dictionary<string, object?> payload)
    {
        if (_localPlayer == null)
        {
            return;
        }

        payload["sourceCharacterName"] = _localPlayer.Name;
        payload["sourceCharacterId"] = _localPlayer.CharacterId;
    }

    private static bool IsCombatMessage(ChatMessage message)
    {
        return message.Sender.Equals("Combat", StringComparison.OrdinalIgnoreCase)
            || message.Sender.Equals("Combat Damage", StringComparison.OrdinalIgnoreCase)
            || message.ChatChannelType.Contains("Combat", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object?> CreateCombatMessagePayload(ChatMessage message)
    {
        var payload = new Dictionary<string, object?>
        {
            ["timestamp"] = DateTime.Now.ToString("O"),
            ["category"] = "CombatMessage",
            ["chatChannel"] = message.ChatChannelType,
            ["sender"] = message.Sender,
            ["raw"] = message.Message
        };

        AddCombatMessageFields(payload, message.Message);
        return payload;
    }

    private static Dictionary<string, object?> CreateCombatResultPayload(CombatResultApplied result, string rendered)
    {
        return new Dictionary<string, object?>
        {
            ["timestamp"] = DateTime.Now.ToString("O"),
            ["category"] = "CombatResult",
            ["raw"] = rendered,
            ["gameTime"] = result.Time,
            ["attacker"] = result.AttackerName,
            ["defender"] = result.DefenderName,
            ["damage"] = result.Damage,
            ["beforeMitigationDamage"] = result.BeforeMitigationDamage,
            ["mitigatedDamage"] = result.MitigatedDamage,
            ["threat"] = result.ThreatToApply,
            ["damageType"] = result.DamageType,
            ["damageStyle"] = result.DamageStyle,
            ["weaponType"] = result.WeaponType,
            ["impactType"] = result.ImpactType,
            ["combatResultType"] = result.CombatResultType,
            ["ability"] = result.AbilityName,
            ["buff"] = result.BuffName
        };
    }

    private static Dictionary<string, object?> CreateExperiencePayload(PlayerExperience experience, PlayerExperience? previous, string rendered)
    {
        return new Dictionary<string, object?>
        {
            ["timestamp"] = DateTime.Now.ToString("O"),
            ["category"] = "ExperienceChanged",
            ["eventType"] = "experience",
            ["raw"] = rendered,
            ["current"] = experience.Current,
            ["toNextLevel"] = experience.ToNextLevel,
            ["experiencePercentage"] = experience.ExperiencePercentage,
            ["previousCurrent"] = previous?.Current,
            ["previousToNextLevel"] = previous?.ToNextLevel,
            ["previousExperiencePercentage"] = previous?.ExperiencePercentage,
            ["deltaCurrent"] = previous == null ? null : experience.Current - previous.Current,
            ["deltaToNextLevel"] = previous == null ? null : experience.ToNextLevel - previous.ToNextLevel,
            ["deltaExperiencePercentage"] = previous == null ? null : experience.ExperiencePercentage - previous.ExperiencePercentage
        };
    }

    private static void AddCombatMessageFields(Dictionary<string, object?> payload, string message)
    {
        var combatText = ExtractCombatText(payload, message);
        if (string.IsNullOrWhiteSpace(combatText))
        {
            return;
        }

        var damage = Regex.Match(combatText, @"^(?<source>.+?) dealt (?<amount>\d[\d,]*) (?<damageType>\w+) damage to (?<target>.+?) with (?<ability>.+?)\. \((?<mitigated>\d[\d,]*) mitigated\)(?<flags>.*)$");
        if (damage.Success)
        {
            payload["eventType"] = "damage";
            payload["source"] = damage.Groups["source"].Value;
            payload["target"] = damage.Groups["target"].Value;
            payload["amount"] = ParseCombatInteger(damage.Groups["amount"].Value);
            payload["damageType"] = damage.Groups["damageType"].Value;
            payload["ability"] = damage.Groups["ability"].Value;
            payload["mitigated"] = ParseCombatInteger(damage.Groups["mitigated"].Value);
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

        var sourceHealing = Regex.Match(combatText, @"^(?<source>.+?)'s (?<ability>.+?) healed (?<target>.+?) for (?<amount>\d[\d,]*)\.$");
        if (sourceHealing.Success)
        {
            payload["eventType"] = "healing";
            payload["source"] = sourceHealing.Groups["source"].Value;
            payload["target"] = sourceHealing.Groups["target"].Value;
            payload["ability"] = sourceHealing.Groups["ability"].Value;
            payload["amount"] = ParseCombatInteger(sourceHealing.Groups["amount"].Value);
            return;
        }

        var passiveHealing = Regex.Match(combatText, @"^(?<target>.+?) was healed for (?<amount>\d[\d,]*) by (?<ability>.+?)\.$");
        if (passiveHealing.Success)
        {
            payload["eventType"] = "healing";
            payload["target"] = passiveHealing.Groups["target"].Value;
            payload["ability"] = passiveHealing.Groups["ability"].Value;
            payload["amount"] = ParseCombatInteger(passiveHealing.Groups["amount"].Value);
        }
    }

    private static int ParseCombatInteger(string value)
    {
        return int.Parse(value.Replace(",", ""));
    }

    private static string ExtractCombatText(Dictionary<string, object?> payload, string message)
    {
        var match = Regex.Match(message, @"^\[(?<direction>[^\]/]+)/(?<filter>[^\]/]+)/(?<playerFilter>[^\]]+)\]\s*(?<text>.*)$");
        if (!match.Success)
        {
            return message;
        }

        payload["direction"] = match.Groups["direction"].Value;
        payload["combatFilter"] = match.Groups["filter"].Value;
        payload["playerFilter"] = match.Groups["playerFilter"].Value;
        return match.Groups["text"].Value;
    }

    private static void AddFlags(Dictionary<string, object?> payload, string flags)
    {
        payload["critical"] = flags.Contains("Critical", StringComparison.OrdinalIgnoreCase);
        payload["absorbed"] = flags.Contains("Absorbed", StringComparison.OrdinalIgnoreCase);
        payload["immune"] = flags.Contains("Immune", StringComparison.OrdinalIgnoreCase);
    }

    private void OpenLogs()
    {
        CloseLogs();
        Directory.CreateDirectory(_outputFolder);
        var fileSuffix = GetCurrentFileSuffix();
        _textLogPath = Path.Combine(_outputFolder, $"combat-live-{fileSuffix}.txt");
        _jsonLogPath = Path.Combine(_outputFolder, $"combat-live-{fileSuffix}.jsonl");
        _textWriter = new StreamWriter(new FileStream(_textLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        _jsonWriter = new StreamWriter(new FileStream(_jsonLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        _lastStatus = $"Combat Data writing to {_jsonLogPath}";
    }

    private void EnsureLogsOpen()
    {
        if (_textWriter == null || _jsonWriter == null)
        {
            OpenLogs();
        }
    }

    private void CloseLogs()
    {
        _textWriter?.Dispose();
        _jsonWriter?.Dispose();
        _textWriter = null;
        _jsonWriter = null;
    }

    private void ClearLogs()
    {
        _bufferedLines.Clear();
        CloseLogs();
        DeleteIfExists(_textLogPath);
        DeleteIfExists(_jsonLogPath);
        _messageCount = 0;
        _structuredCount = 0;
        _experienceCount = 0;
        _startedAt = DateTime.Now;
        OpenLogs();
    }

    private void RotateLogsIfNeeded()
    {
        var maxBytes = Math.Max(1, _maxFileMegabytes) * 1024L * 1024L;
        if (IsUnderSizeLimit(_textLogPath, maxBytes) && IsUnderSizeLimit(_jsonLogPath, maxBytes))
        {
            return;
        }

        CloseLogs();
        RotateFile(_textLogPath);
        RotateFile(_jsonLogPath);
        OpenLogs();
    }

    private static bool IsUnderSizeLimit(string? path, long maxBytes)
    {
        return string.IsNullOrWhiteSpace(path) || !File.Exists(path) || new FileInfo(path).Length < maxBytes;
    }

    private static void RotateFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        var previousPath = $"{path}.previous";
        DeleteIfExists(previousPath);
        File.Move(path, previousPath);
    }

    private static void DeleteIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void SaveSnapshot()
    {
        if (_bufferedLines.Count == 0)
        {
            Chat.AddInfoMessage("Combat Data buffer is empty.");
            return;
        }

        Directory.CreateDirectory(_outputFolder);
        var startedAt = _startedAt == default ? DateTime.Now : _startedAt;
        var filePath = Path.Combine(_outputFolder, $"combat-{startedAt:yyyyMMdd-HHmmss}.txt");
        File.WriteAllLines(filePath, _bufferedLines);
        Chat.AddInfoMessage($"Combat Data saved {_bufferedLines.Count} lines: {filePath}");
    }

    private void TrimBuffer()
    {
        if (_bufferedLines.Count <= MaxBufferedLines)
        {
            return;
        }

        _bufferedLines.RemoveRange(0, _bufferedLines.Count - MaxBufferedLines);
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
            var configuredOutputPath = ExpandConfiguredPath(config?.OutputPath);
            var configuredOutputFolder = ExpandConfiguredPath(FirstNonBlank(config?.OutputFolder, config?.DataFolder, config?.Directory, config?.Folder));

            if (!string.IsNullOrWhiteSpace(configuredOutputPath))
            {
                _jsonLogPath = configuredOutputPath;
                _outputFolder = Path.GetDirectoryName(_jsonLogPath) ?? DefaultOutputFolder;
                _textLogPath = Path.Combine(_outputFolder, $"combat-live-{GetCurrentFileSuffix()}.txt");
            }
            else if (!string.IsNullOrWhiteSpace(configuredOutputFolder))
            {
                _outputFolder = configuredOutputFolder;
                _textLogPath = Path.Combine(_outputFolder, $"combat-live-{GetCurrentFileSuffix()}.txt");
                _jsonLogPath = Path.Combine(_outputFolder, $"combat-live-{GetCurrentFileSuffix()}.jsonl");
            }

            _includeCombatMessages = config?.IncludeCombatMessages ?? _includeCombatMessages;
            _includeStructuredResults = config?.IncludeStructuredResults ?? _includeStructuredResults;
            _includeExperience = config?.IncludeExperience ?? _includeExperience;
            _maxFileMegabytes = Math.Clamp(config?.MaxFileMegabytes ?? _maxFileMegabytes, 1, 100);
        }
        catch (Exception ex)
        {
            Logger.Error($"Combat Data config failed: {ex}");
        }
    }

    private void SavePathConfig()
    {
        var config = new PathConfig(
            OutputFolder: _outputFolder,
            OutputPath: null,
            IncludeCombatMessages: _includeCombatMessages,
            IncludeStructuredResults: _includeStructuredResults,
            IncludeExperience: _includeExperience,
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

    private static string DefaultOutputFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PantheonCombatData");

    private string LocalPathConfigPath => Path.Combine(_gameFolder, "Mods", "PantheonAddons", "CombatDataConfig.json");

    private sealed record PathConfig(
        string? OutputFolder,
        string? OutputPath,
        bool? IncludeCombatMessages = null,
        bool? IncludeStructuredResults = null,
        bool? IncludeExperience = null,
        int? MaxFileMegabytes = null,
        string? DataFolder = null,
        string? Directory = null,
        string? Folder = null);
}
