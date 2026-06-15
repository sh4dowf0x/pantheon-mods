using PantheonAddonFramework;
using PantheonAddonFramework.Configuration;
using PantheonAddonFramework.Models;
using PantheonAddonFramework.UI;
using System.Diagnostics;
using System.Text.Json;

namespace PantheonAddons.TargetHealthBars;

[AddonMetadata("Target Health Bars", "Codex", "Adds numeric health text to offensive and defensive target bars")]
public sealed class TargetHealthBars : Addon
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ConfigJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly string[] FormatModes = { "both", "percent", "numbers" };

    private readonly string _gameFolder = ResolveGameFolder();
    private IAddonPoolBar? _offensiveBar;
    private IAddonPoolBar? _defensiveBar;
    private IAddonTextComponent? _offensiveText;
    private IAddonTextComponent? _defensiveText;
    private IPlayer? _localPlayer;
    private TargetHealthSnapshot? _lastOffensive;
    private TargetHealthSnapshot? _lastDefensive;
    private DateTime _nextPollAt = DateTime.MinValue;
    private bool _isEnabled = true;
    private bool _showOffensive = true;
    private bool _showDefensive = true;
    private bool _showMana = true;
    private string _format = "both";
    private float _fontSize = 17;
    private float _textWidth = 460;
    private float _textHeight = 44;
    private float _textYOffset;
    private string _lastStatus = "Target Health Bars idle.";

    public override void OnCreate()
    {
        LoadConfig();
        CustomChatCommands.Add("/targethealth", HandleCommand);
        CustomChatCommands.Add("/thb", HandleCommand);
        WindowPanelEvents.OffensiveTargetReady.Subscribe(OnOffensiveTargetReady);
        WindowPanelEvents.DefensiveTargetReady.Subscribe(OnDefensiveTargetReady);
        WindowPanelEvents.OffTargetHealthChange.Subscribe(OnOffensiveHealthChanged);
        WindowPanelEvents.DefTargetHealthChange.Subscribe(OnDefensiveHealthChanged);
        LocalPlayerEvents.OffensiveTargetHealthChanged.Subscribe(OnOffensiveHealthChanged);
        LocalPlayerEvents.DefensiveTargetHealthChanged.Subscribe(OnDefensiveHealthChanged);
        LocalPlayerEvents.LocalPlayerEntered.Subscribe(OnLocalPlayerEntered);
        LocalPlayerEvents.LocalPlayerLeft.Subscribe(OnLocalPlayerLeft);
        LifecycleEvents.OnUpdate.Subscribe(OnUpdate);
    }

    public override void Enable()
    {
        _isEnabled = true;
        RefreshVisibility();
        RefreshTexts();
    }

    public override void Disable()
    {
        _isEnabled = false;
        RefreshVisibility();
    }

    public override IEnumerable<IConfigurationValue> GetConfiguration()
    {
        return new IConfigurationValue[]
        {
            new BoolConfigurationValue("Offensive target", "Shows numeric health on the offensive target bar.", _showOffensive, value =>
            {
                _showOffensive = value;
                SaveConfig();
                RefreshVisibility();
            }),
            new BoolConfigurationValue("Defensive target", "Shows numeric health on the defensive target bar.", _showDefensive, value =>
            {
                _showDefensive = value;
                SaveConfig();
                RefreshVisibility();
            }),
            new BoolConfigurationValue("Mana", "Shows target mana when the target exposes a mana pool.", _showMana, value =>
            {
                _showMana = value;
                SaveConfig();
                RefreshTexts();
            }),
            new PicklistConfigurationValue("Format", "Health text format.", GetFormatIndex(_format), FormatModes, value =>
            {
                _format = FormatModes[Math.Clamp(value, 0, FormatModes.Length - 1)];
                SaveConfig();
                RefreshTexts();
            }),
            new FloatConfigurationValue("Font size", "Health text font size.", _fontSize, 10, 32, 1, value =>
            {
                _fontSize = value;
                SaveConfig();
                ApplyTextLayout();
            })
        };
    }

    public override void Dispose()
    {
        CustomChatCommands.Remove("/targethealth");
        CustomChatCommands.Remove("/thb");
        WindowPanelEvents.OffensiveTargetReady.Unsubscribe(OnOffensiveTargetReady);
        WindowPanelEvents.DefensiveTargetReady.Unsubscribe(OnDefensiveTargetReady);
        WindowPanelEvents.OffTargetHealthChange.Unsubscribe(OnOffensiveHealthChanged);
        WindowPanelEvents.DefTargetHealthChange.Unsubscribe(OnDefensiveHealthChanged);
        LocalPlayerEvents.OffensiveTargetHealthChanged.Unsubscribe(OnOffensiveHealthChanged);
        LocalPlayerEvents.DefensiveTargetHealthChanged.Unsubscribe(OnDefensiveHealthChanged);
        LocalPlayerEvents.LocalPlayerEntered.Unsubscribe(OnLocalPlayerEntered);
        LocalPlayerEvents.LocalPlayerLeft.Unsubscribe(OnLocalPlayerLeft);
        LifecycleEvents.OnUpdate.Unsubscribe(OnUpdate);
        TryDestroyText(_offensiveText);
        TryDestroyText(_defensiveText);
    }

    private void HandleCommand(string[] args)
    {
        if (args.Length == 0 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage("Target Health Bars: /targethealth status, on, off, offensive on|off, defensive on|off, mana on|off, format both|percent|numbers, fontsize <10-32>");
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "status":
                Chat.AddInfoMessage($"{_lastStatus} enabled={_isEnabled}; offensive={_showOffensive}; defensive={_showDefensive}; mana={_showMana}; format={_format}; font={_fontSize:F0}");
                break;
            case "on":
                _isEnabled = true;
                SaveConfig();
                RefreshVisibility();
                RefreshTexts();
                Chat.AddInfoMessage("Target Health Bars enabled.");
                break;
            case "off":
                _isEnabled = false;
                SaveConfig();
                RefreshVisibility();
                Chat.AddInfoMessage("Target Health Bars disabled.");
                break;
            case "offensive":
                HandleToggle(args, "offensive target", value => _showOffensive = value, _showOffensive);
                break;
            case "defensive":
                HandleToggle(args, "defensive target", value => _showDefensive = value, _showDefensive);
                break;
            case "mana":
            case "mp":
                HandleToggle(args, "mana", value => _showMana = value, _showMana);
                break;
            case "format":
                SetFormat(args);
                break;
            case "fontsize":
            case "font":
                SetFontSize(args);
                break;
            default:
                Chat.AddInfoMessage("Target Health Bars: unknown command. Try /targethealth help.");
                break;
        }
    }

    private void HandleToggle(string[] args, string label, Action<bool> setter, bool current)
    {
        if (args.Length < 2 || args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage($"Target Health Bars {label} is {(current ? "on" : "off")}.");
            return;
        }

        if (args[1].Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            setter(true);
            SaveConfig();
            RefreshVisibility();
            RefreshTexts();
            Chat.AddInfoMessage($"Target Health Bars {label} enabled.");
            return;
        }

        if (args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            setter(false);
            SaveConfig();
            RefreshVisibility();
            Chat.AddInfoMessage($"Target Health Bars {label} disabled.");
            return;
        }

        Chat.AddInfoMessage($"Target Health Bars {label}: on, off, status");
    }

    private void SetFormat(string[] args)
    {
        if (args.Length < 2 || !FormatModes.Contains(args[1], StringComparer.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage($"Target Health Bars format is {_format}. Use both, percent, or numbers.");
            return;
        }

        _format = args[1].ToLowerInvariant();
        SaveConfig();
        RefreshTexts();
        Chat.AddInfoMessage($"Target Health Bars format set to {_format}.");
    }

    private void SetFontSize(string[] args)
    {
        if (args.Length < 2 || !float.TryParse(args[1], out var size))
        {
            Chat.AddInfoMessage($"Target Health Bars font size is {_fontSize:F0}.");
            return;
        }

        _fontSize = Math.Clamp(size, 10, 32);
        SaveConfig();
        ApplyTextLayout();
        Chat.AddInfoMessage($"Target Health Bars font size set to {_fontSize:F0}.");
    }

    private void OnOffensiveTargetReady(IAddonPoolBar poolbar)
    {
        _offensiveBar = poolbar;
        _offensiveText = RecreateText(_offensiveText, _offensiveBar);
        ApplyTextLayout(_offensiveText);
        RefreshVisibility();
        RefreshText(_offensiveText, _lastOffensive);
    }

    private void OnDefensiveTargetReady(IAddonPoolBar poolbar)
    {
        _defensiveBar = poolbar;
        _defensiveText = RecreateText(_defensiveText, _defensiveBar);
        ApplyTextLayout(_defensiveText);
        RefreshVisibility();
        RefreshText(_defensiveText, _lastDefensive);
    }

    private void OnOffensiveHealthChanged(TargetHealthSnapshot health)
    {
        _lastOffensive = health;
        RefreshText(_offensiveText, health);
    }

    private void OnDefensiveHealthChanged(TargetHealthSnapshot health)
    {
        _lastDefensive = health;
        RefreshText(_defensiveText, health);
    }

    private void OnLocalPlayerEntered(IPlayer player)
    {
        if (player.IsLocalPlayer)
        {
            _localPlayer = player;
            PollTargetPools();
        }
    }

    private void OnLocalPlayerLeft(IPlayer player)
    {
        if (_localPlayer?.CharacterId == player.CharacterId)
        {
            _localPlayer = null;
            _lastOffensive = null;
            _lastDefensive = null;
            RefreshTexts();
        }
    }

    private void OnUpdate()
    {
        if (!_isEnabled || DateTime.UtcNow < _nextPollAt)
        {
            return;
        }

        _nextPollAt = DateTime.UtcNow.AddSeconds(0.25);
        PollTargetPools();
    }

    private void PollTargetPools()
    {
        if (_localPlayer == null)
        {
            return;
        }

        OnOffensiveHealthChanged(_localPlayer.GetOffensiveTargetHealth());
        OnDefensiveHealthChanged(_localPlayer.GetDefensiveTargetHealth());
    }

    private IAddonTextComponent? RecreateText(IAddonTextComponent? existing, IAddonPoolBar? poolbar)
    {
        TryDestroyText(existing);
        if (poolbar == null)
        {
            return null;
        }

        try
        {
            var text = poolbar.AddTextComponent("");
            text.SetFontColor(255, 255, 255, 245);
            return text;
        }
        catch (Exception ex)
        {
            _lastStatus = $"Could not create target health text: {ex.GetType().Name}";
            Logger.Error($"Target Health Bars text creation failed: {ex}");
            return null;
        }
    }

    private void RefreshTexts()
    {
        RefreshText(_offensiveText, _lastOffensive);
        RefreshText(_defensiveText, _lastDefensive);
    }

    private void RefreshText(IAddonTextComponent? text, TargetHealthSnapshot? health)
    {
        if (text == null)
        {
            return;
        }

        try
        {
            text.SetText(CreateText(health));
            _lastStatus = "Target Health Bars active.";
        }
        catch (Exception ex)
        {
            _lastStatus = $"Could not update target health text: {ex.GetType().Name}";
        }
    }

    private string CreateText(TargetHealthSnapshot? health)
    {
        if (health == null || !health.HasTarget)
        {
            return "";
        }

        var current = Math.Round(health.CurrentHealth);
        var max = Math.Round(health.MaxHealth);
        var healthLine = _format switch
        {
            "percent" => $"HP {health.HealthPercent:F0}%",
            "numbers" => $"HP {current:F0} / {max:F0}",
            _ => $"HP {health.HealthPercent:F0}%  {current:F0} / {max:F0}"
        };

        if (!_showMana || !health.HasMana)
        {
            return healthLine;
        }

        var manaCurrent = Math.Round(health.CurrentMana);
        var manaMax = Math.Round(health.MaxMana);
        var manaLine = _format switch
        {
            "percent" => $"MP {health.ManaPercent:F0}%",
            "numbers" => $"MP {manaCurrent:F0} / {manaMax:F0}",
            _ => $"MP {health.ManaPercent:F0}%  {manaCurrent:F0} / {manaMax:F0}"
        };

        return $"{healthLine}{Environment.NewLine}{manaLine}";
    }

    private void ApplyTextLayout()
    {
        ApplyTextLayout(_offensiveText);
        ApplyTextLayout(_defensiveText);
    }

    private void ApplyTextLayout(IAddonTextComponent? text)
    {
        if (text == null)
        {
            return;
        }

        try
        {
            text.SetSize(_textWidth, _textHeight);
            text.SetPosition(0, _textYOffset);
            text.SetFontSize(_fontSize);
        }
        catch
        {
        }
    }

    private void RefreshVisibility()
    {
        TryEnableText(_offensiveText, _isEnabled && _showOffensive);
        TryEnableText(_defensiveText, _isEnabled && _showDefensive);
    }

    private static void TryEnableText(IAddonTextComponent? text, bool enabled)
    {
        try
        {
            text?.Enable(enabled);
        }
        catch
        {
        }
    }

    private static void TryDestroyText(IAddonTextComponent? text)
    {
        try
        {
            text?.Destroy();
        }
        catch
        {
        }
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
            _isEnabled = config?.Enabled ?? _isEnabled;
            _showOffensive = config?.ShowOffensive ?? _showOffensive;
            _showDefensive = config?.ShowDefensive ?? _showDefensive;
            _showMana = config?.ShowMana ?? _showMana;
            _format = NormalizeFormat(config?.Format ?? _format);
            _fontSize = Math.Clamp(config?.FontSize ?? _fontSize, 10, 32);
            _textYOffset = Math.Clamp(config?.TextYOffset ?? _textYOffset, -40, 40);
        }
        catch (Exception ex)
        {
            Logger.Error($"Target Health Bars config failed: {ex}");
        }
    }

    private void SaveConfig()
    {
        var config = new PathConfig(
            Enabled: _isEnabled,
            ShowOffensive: _showOffensive,
            ShowDefensive: _showDefensive,
            ShowMana: _showMana,
            Format: _format,
            FontSize: _fontSize,
            TextYOffset: _textYOffset);

        Directory.CreateDirectory(Path.GetDirectoryName(LocalConfigPath) ?? _gameFolder);
        File.WriteAllText(LocalConfigPath, JsonSerializer.Serialize(config, JsonOptions));
    }

    private static string NormalizeFormat(string value)
    {
        return FormatModes.Contains(value, StringComparer.OrdinalIgnoreCase) ? value.ToLowerInvariant() : "both";
    }

    private static int GetFormatIndex(string format)
    {
        var index = Array.FindIndex(FormatModes, value => value.Equals(format, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 0 : index;
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

    private string LocalConfigPath => Path.Combine(_gameFolder, "Mods", "PantheonAddons", "TargetHealthBarsConfig.json");

    private sealed record PathConfig(
        bool? Enabled = null,
        bool? ShowOffensive = null,
        bool? ShowDefensive = null,
        bool? ShowMana = null,
        string? Format = null,
        float? FontSize = null,
        float? TextYOffset = null);
}
