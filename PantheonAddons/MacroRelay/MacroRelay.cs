using PantheonAddonFramework;
using PantheonAddonFramework.Configuration;
using PantheonAddonFramework.Models;
using PantheonAddonFramework.UI;
using System.Diagnostics;
using System.Text.Json;

namespace PantheonAddons.MacroRelay;

[AddonMetadata("Macro Relay", "Codex", "Relays macro activation requests between local Pantheon clients")]
public sealed class MacroRelay : Addon
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ConfigJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly int[] AssignableKeys =
    {
        KeyCodes.A, KeyCodes.B, KeyCodes.C, KeyCodes.D, KeyCodes.E, KeyCodes.F, KeyCodes.G, KeyCodes.H, KeyCodes.I, KeyCodes.J, KeyCodes.K, KeyCodes.L, KeyCodes.M,
        KeyCodes.N, KeyCodes.O, KeyCodes.P, KeyCodes.Q, KeyCodes.R, KeyCodes.S, KeyCodes.T, KeyCodes.U, KeyCodes.V, KeyCodes.W, KeyCodes.X, KeyCodes.Y, KeyCodes.Z,
        KeyCodes.Alpha0, KeyCodes.Alpha1, KeyCodes.Alpha2, KeyCodes.Alpha3, KeyCodes.Alpha4, KeyCodes.Alpha5, KeyCodes.Alpha6, KeyCodes.Alpha7, KeyCodes.Alpha8, KeyCodes.Alpha9,
        KeyCodes.F1, KeyCodes.F2, KeyCodes.F3, KeyCodes.F4, KeyCodes.F5, KeyCodes.F6, KeyCodes.F7, KeyCodes.F8, KeyCodes.F9, KeyCodes.F10, KeyCodes.F11, KeyCodes.F12, KeyCodes.F13, KeyCodes.F14, KeyCodes.F15,
        KeyCodes.Keypad0, KeyCodes.Keypad1, KeyCodes.Keypad2, KeyCodes.Keypad3, KeyCodes.Keypad4, KeyCodes.Keypad5, KeyCodes.Keypad6, KeyCodes.Keypad7, KeyCodes.Keypad8, KeyCodes.Keypad9,
        KeyCodes.Space, KeyCodes.Tab, KeyCodes.Return, KeyCodes.Escape, KeyCodes.BackQuote, KeyCodes.Minus, KeyCodes.Equals, KeyCodes.LeftBracket, KeyCodes.RightBracket, KeyCodes.Backslash,
        KeyCodes.Semicolon, KeyCodes.Quote, KeyCodes.Comma, KeyCodes.Period, KeyCodes.Slash
    };

    private readonly string _gameFolder = ResolveGameFolder();
    private string _relayFolder = DefaultRelayFolder;
    private string _requestPath = Path.Combine(DefaultRelayFolder, "macro-request.json");
    private string _pathConfigStatus = "Using default Macro Relay paths.";
    private IAddonWindow? _hotbarWindow;
    private IAddonWindow? _slotEditorWindow;
    private IAddonTextComponent? _slotEditorTitle;
    private IAddonTextInputComponent? _slotLabelInput;
    private IAddonTextInputComponent? _slotMacroInput;
    private IAddonButtonComponent? _slotHotkeyButton;
    private IAddonButtonComponent? _receiverToggleButton;
    private IPlayer? _localPlayer;
    private readonly List<IAddonButtonComponent> _hotbarButtons = new();
    private HotbarSlot[] _slots = CreateDefaultSlots();
    private int _visibleSlotCount = 6;
    private bool _isEnabled;
    private bool _receiverEnabled;
    private bool _targetSyncEnabled = true;
    private bool _wantsHotbar = true;
    private bool _reportedHotbarFailure;
    private bool _isCapturingHotkey;
    private int _editingSlotIndex = -1;
    private DateTime _lastRead = DateTime.MinValue;
    private DateTime _nextHotbarAttemptAt = DateTime.MinValue;
    private DateTime _nextReceiverButtonRefreshAt = DateTime.MinValue;
    private double _pollIntervalSeconds = 0.25;
    private string _lastRequestId = "";
    private string _lastStatus = "Macro Relay idle.";
    private string _lastReceiverButtonText = "";

    public override void OnCreate()
    {
        LoadPathConfig();
        CustomChatCommands.Add("/macrorelay", HandleCommand);
        LocalPlayerEvents.LocalPlayerEntered.Subscribe(OnLocalPlayerEntered);
        LifecycleEvents.OnUpdate.Subscribe(OnUpdate);
    }

    public override void Enable()
    {
        _isEnabled = true;
    }

    public override void Disable()
    {
        _isEnabled = false;
        TryEnableWindow(_hotbarWindow, false);
        TryEnableWindow(_slotEditorWindow, false);
    }

    public override IEnumerable<IConfigurationValue> GetConfiguration()
    {
        return new IConfigurationValue[]
        {
            new BoolConfigurationValue("Receiver", "Reads macro relay requests from the shared file.", _receiverEnabled, value =>
            {
                _receiverEnabled = value;
                RefreshReceiverButton();
            }),
            new FloatConfigurationValue("Poll interval", "Seconds between receiver file reads.", 0.25f, 0.10f, 1.0f, 0.05f, value => _pollIntervalSeconds = value)
        };
    }

    public override void Dispose()
    {
        CustomChatCommands.Remove("/macrorelay");
        LocalPlayerEvents.LocalPlayerEntered.Unsubscribe(OnLocalPlayerEntered);
        LifecycleEvents.OnUpdate.Unsubscribe(OnUpdate);
        foreach (var button in _hotbarButtons)
        {
            TryDestroyButton(button);
        }

        TryDestroyWindow(_hotbarWindow);
        TryDestroyWindow(_slotEditorWindow);
    }

    private void HandleCommand(string[] args)
    {
        if (args.Length == 0 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage("Macro Relay: /macrorelay list, run <macro>, send <macro>, hotbar show|hide|reload, receiver on|off, target, targetsync on|off, status, path");
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "list":
                ListMacros();
                break;
            case "run":
                RunMacroFromArgs(args, 1, true);
                break;
            case "send":
                SendMacroFromArgs(args, 1);
                break;
            case "receiver":
                HandleReceiverCommand(args);
                break;
            case "hotbar":
                HandleHotbarCommand(args);
                break;
            case "target":
            case "targets":
                ReportTargets();
                break;
            case "targetsync":
                HandleTargetSyncCommand(args);
                break;
            case "status":
                Chat.AddInfoMessage($"{_lastStatus} Receiver: {(_receiverEnabled ? "on" : "off")}. Target sync: {(_targetSyncEnabled ? "on" : "off")}.");
                ReportPathConfiguration();
                break;
            case "path":
                ReportPathConfiguration();
                break;
            default:
                Chat.AddInfoMessage("Macro Relay: unknown command. Try /macrorelay help.");
                break;
        }
    }

    private void OnUpdate()
    {
        if (!_isEnabled)
        {
            return;
        }

        if (_wantsHotbar)
        {
            TryEnsureHotbar(false);
        }

        UpdateHotkeyCapture();
        UpdateHotkeys();
        RefreshReceiverButton();

        if (_receiverEnabled && Due(_lastRead))
        {
            ReadRequest();
        }
    }

    private bool Due(DateTime lastRun)
    {
        return (DateTime.UtcNow - lastRun).TotalSeconds >= _pollIntervalSeconds;
    }

    private void OnLocalPlayerEntered(IPlayer player)
    {
        if (player.IsLocalPlayer)
        {
            _localPlayer = player;
        }
    }

    private void ListMacros()
    {
        var macros = Macros.GetAll().Select(macro => macro.Name).Where(name => !string.IsNullOrWhiteSpace(name)).OrderBy(name => name).ToArray();
        if (macros.Length == 0)
        {
            Chat.AddInfoMessage("Macro Relay found no macros. Open Pantheon's macro window and try again.");
            return;
        }

        Chat.AddInfoMessage($"Macro Relay macros: {string.Join(", ", macros)}");
    }

    private void RunMacroFromArgs(string[] args, int startIndex, bool notify)
    {
        var macroName = JoinArgs(args, startIndex);
        if (string.IsNullOrWhiteSpace(macroName))
        {
            Chat.AddInfoMessage("Macro Relay run requires a macro name.");
            return;
        }

        TryActivateMacro(macroName, notify);
    }

    private bool TryActivateMacro(string macroName, bool notify)
    {
        var macro = Macros.GetByName(macroName);
        if (macro == null)
        {
            _lastStatus = $"Macro not found: {macroName}";
            if (notify)
            {
                Chat.AddInfoMessage($"{_lastStatus}. Open Pantheon's macro window and confirm the macro name.");
            }

            return false;
        }

        macro.Activate();
        _lastStatus = $"Activated macro: {macro.Name}";
        if (notify)
        {
            Chat.AddInfoMessage(_lastStatus);
        }

        return true;
    }

    private void SendMacroFromArgs(string[] args, int startIndex)
    {
        var macroName = JoinArgs(args, startIndex);
        if (string.IsNullOrWhiteSpace(macroName))
        {
            Chat.AddInfoMessage("Macro Relay send requires a macro name.");
            return;
        }

        SendMacro(macroName, true);
    }

    private void SendMacro(string macroName, bool notify)
    {
        var payload = new MacroRequest(
            SchemaVersion: 2,
            RequestId: Guid.NewGuid().ToString("N"),
            MacroName: macroName,
            SenderProcessId: Process.GetCurrentProcess().Id,
            TimestampUtc: DateTime.UtcNow,
            SyncTargets: _targetSyncEnabled,
            OffensiveTarget: CreateTargetPayload(_localPlayer?.GetOffensiveTarget()),
            DefensiveTarget: CreateTargetPayload(_localPlayer?.GetDefensiveTarget()));

        try
        {
            Directory.CreateDirectory(_relayFolder);
            var tempPath = _requestPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(payload, JsonOptions));
            File.Move(tempPath, _requestPath, true);
            _lastStatus = $"Sent macro request: {macroName}";
            if (notify)
            {
                Chat.AddInfoMessage(_lastStatus);
            }
        }
        catch (Exception ex)
        {
            _lastStatus = $"Could not send macro request: {ex.GetType().Name}";
            if (notify)
            {
                Chat.AddInfoMessage($"{_lastStatus}: {ex.Message}");
            }
        }
    }

    private void HandleReceiverCommand(string[] args)
    {
        if (args.Length < 2 || args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage($"Macro Relay receiver is {(_receiverEnabled ? "on" : "off")}.");
            return;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "on":
                _receiverEnabled = true;
                _lastStatus = "Receiver enabled.";
                SavePathConfig();
                RefreshReceiverButton(true);
                Chat.AddInfoMessage(_lastStatus);
                break;
            case "off":
                _receiverEnabled = false;
                _lastStatus = "Receiver disabled.";
                SavePathConfig();
                RefreshReceiverButton(true);
                Chat.AddInfoMessage(_lastStatus);
                break;
            default:
                Chat.AddInfoMessage("Macro Relay receiver: on, off, status");
                break;
        }
    }

    private void HandleTargetSyncCommand(string[] args)
    {
        if (args.Length < 2 || args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage($"Macro Relay target sync is {(_targetSyncEnabled ? "on" : "off")}.");
            return;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "on":
                _targetSyncEnabled = true;
                SavePathConfig();
                Chat.AddInfoMessage("Macro Relay target sync enabled.");
                break;
            case "off":
                _targetSyncEnabled = false;
                SavePathConfig();
                Chat.AddInfoMessage("Macro Relay target sync disabled.");
                break;
            default:
                Chat.AddInfoMessage("Macro Relay target sync: on, off, status");
                break;
        }
    }

    private void ReportTargets()
    {
        if (_localPlayer == null)
        {
            Chat.AddInfoMessage("Macro Relay has not found the local player yet.");
            return;
        }

        Chat.AddInfoMessage($"Macro Relay offensive target: {FormatTarget(_localPlayer.GetOffensiveTarget())}");
        Chat.AddInfoMessage($"Macro Relay defensive target: {FormatTarget(_localPlayer.GetDefensiveTarget())}");
    }

    private void HandleHotbarCommand(string[] args)
    {
        if (args.Length < 2 || args[1].Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            _wantsHotbar = true;
            TryEnsureHotbar(true);
            return;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "hide":
                _wantsHotbar = false;
                TryEnableWindow(_hotbarWindow, false);
                Chat.AddInfoMessage("Macro Relay hotbar hidden. Use /macrorelay hotbar show to reopen.");
                break;
            case "reload":
                LoadPathConfig();
                RebuildHotbar();
                Chat.AddInfoMessage($"Macro Relay hotbar reloaded from {LocalPathConfigPath}");
                break;
            default:
                Chat.AddInfoMessage("Macro Relay hotbar: show, hide, reload");
                break;
        }
    }

    private bool TryEnsureHotbar(bool notifyChat)
    {
        if (_hotbarWindow != null)
        {
            if (TryEnableHotbar())
            {
                return true;
            }

            ClearHotbarReferences();
            _nextHotbarAttemptAt = DateTime.UtcNow.AddSeconds(1);
            return false;
        }

        if (!notifyChat && DateTime.UtcNow < _nextHotbarAttemptAt)
        {
            return false;
        }

        try
        {
            _hotbarWindow = CustomUI.CreateWindow("Macro Relay", 145, 385);
            _hotbarWindow.AddResizeHandle(640, 760, 120, 160);
            BuildHotbarButtons();
            if (!TryEnableHotbar())
            {
                ClearHotbarReferences();
                _nextHotbarAttemptAt = DateTime.UtcNow.AddSeconds(1);
                return false;
            }

            return true;
        }
        catch (NullReferenceException)
        {
            _nextHotbarAttemptAt = DateTime.UtcNow.AddSeconds(1);
            return false;
        }
        catch (Exception ex)
        {
            _nextHotbarAttemptAt = DateTime.UtcNow.AddSeconds(1);
            if (notifyChat)
            {
                Chat.AddInfoMessage($"Macro Relay could not create hotbar: {ex.GetType().Name}: {ex.Message}");
            }

            if (!_reportedHotbarFailure)
            {
                _reportedHotbarFailure = true;
                Logger.Error($"Macro Relay hotbar creation failed: {ex}");
            }

            return false;
        }
    }

    private void RebuildHotbar()
    {
        foreach (var button in _hotbarButtons)
        {
            TryDestroyButton(button);
        }

        _hotbarButtons.Clear();
        BuildHotbarButtons();
    }

    private bool TryEnableHotbar()
    {
        if (!TryEnableWindow(_hotbarWindow, true))
        {
            return false;
        }

        foreach (var button in _hotbarButtons)
        {
            if (!TryEnableButton(button, true))
            {
                return false;
            }
        }

        return true;
    }

    private void ClearHotbarReferences()
    {
        foreach (var button in _hotbarButtons)
        {
            TryDestroyButton(button);
        }

        _hotbarButtons.Clear();
        TryDestroyWindow(_hotbarWindow);
        _hotbarWindow = null;
    }

    private void ClearSlotEditorReferences()
    {
        TryDestroyWindow(_slotEditorWindow);
        _slotEditorWindow = null;
        _slotEditorTitle = null;
        _slotLabelInput = null;
        _slotMacroInput = null;
        _slotHotkeyButton = null;
    }

    private static bool TryEnableWindow(IAddonWindow? window, bool enabled)
    {
        if (window == null)
        {
            return true;
        }

        try
        {
            window.Enable(enabled);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryEnableButton(IAddonButtonComponent? button, bool enabled)
    {
        if (button == null)
        {
            return true;
        }

        try
        {
            button.Enable(enabled);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryEnableText(IAddonTextComponent? text, bool enabled)
    {
        if (text == null)
        {
            return true;
        }

        try
        {
            text.Enable(enabled);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryEnableTextInput(IAddonTextInputComponent? input, bool enabled)
    {
        if (input == null)
        {
            return true;
        }

        try
        {
            input.Enable(enabled);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDestroyButton(IAddonButtonComponent? button)
    {
        try
        {
            button?.Destroy();
        }
        catch
        {
        }
    }

    private static void TryDestroyWindow(IAddonWindow? window)
    {
        try
        {
            window?.Destroy();
        }
        catch
        {
        }
    }

    private void BuildHotbarButtons()
    {
        if (_hotbarWindow == null)
        {
            return;
        }

        const float buttonWidth = 94;
        const float buttonHeight = 34;
        const float gap = 5;
        const int rowsPerColumn = 12;
        var visibleCount = Math.Clamp(_visibleSlotCount, 1, Math.Max(1, _slots.Length));
        _visibleSlotCount = visibleCount;
        var columns = (int)Math.Ceiling(visibleCount / (double)rowsPerColumn);
        var rows = Math.Min(rowsPerColumn, visibleCount);
        var hotbarWidth = 22 + (columns * buttonWidth) + ((columns - 1) * gap);
        var hotbarHeight = 68 + (rows * (buttonHeight + gap));
        _hotbarWindow.SetWidth(hotbarWidth);
        _hotbarWindow.SetHeight(hotbarHeight);

        var topY = (hotbarHeight / 2.0f) - 31.0f;
        _receiverToggleButton = _hotbarWindow.AddButtonComponent("", ToggleReceiver);
        _receiverToggleButton.SetSize(34, 22);
        _receiverToggleButton.SetFontSize(10);
        _receiverToggleButton.SetPosition(-42, topY);
        _hotbarButtons.Add(_receiverToggleButton);
        RefreshReceiverButton(true);

        var removeButton = _hotbarWindow.AddButtonComponent("-", RemoveHotbarSlot);
        removeButton.SetSize(34, 22);
        removeButton.SetFontSize(15);
        removeButton.SetPosition(0, topY);
        _hotbarButtons.Add(removeButton);

        var addButton = _hotbarWindow.AddButtonComponent("+", AddHotbarSlot);
        addButton.SetSize(34, 22);
        addButton.SetFontSize(15);
        addButton.SetPosition(42, topY);
        _hotbarButtons.Add(addButton);

        var startX = -((columns - 1) * (buttonWidth + gap)) / 2.0f;
        var startY = topY - 32.0f;

        for (var i = 0; i < visibleCount; i++)
        {
            var slot = _slots[i];
            var slotIndex = i;
            var column = i / rowsPerColumn;
            var row = i % rowsPerColumn;
            var button = _hotbarWindow.AddButtonComponent(GetSlotButtonText(slot), () => ActivateSlot(slotIndex, true), () => OpenSlotEditor(slotIndex));
            button.SetSize(buttonWidth, buttonHeight);
            button.SetFontSize(10.5f);
            button.SetPosition(startX + (column * (buttonWidth + gap)), startY - (row * (buttonHeight + gap)));
            _hotbarButtons.Add(button);
        }
    }

    private void OpenSlotEditor(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Length)
        {
            return;
        }

        _editingSlotIndex = slotIndex;
        var slot = _slots[slotIndex];

        if (_slotEditorWindow != null && !TryEnableWindow(_slotEditorWindow, true))
        {
            ClearSlotEditorReferences();
        }

        if (_slotEditorWindow == null)
        {
            try
            {
                _slotEditorWindow = CustomUI.CreateWindow("Macro Slot", 330, 285);
                _slotEditorWindow.AddResizeHandle(520, 400, 280, 250);

                _slotEditorTitle = _slotEditorWindow.AddTextComponent("");
                _slotEditorTitle.SetSize(290, 36);
                _slotEditorTitle.SetPosition(0, 92);
                _slotEditorTitle.SetFontSize(15);

                _slotLabelInput = _slotEditorWindow.AddTextInputComponent("");
                _slotLabelInput.SetSize(250, 32);
                _slotLabelInput.SetPosition(0, 48);
                _slotLabelInput.SetFontSize(14);

                _slotMacroInput = _slotEditorWindow.AddTextInputComponent("");
                _slotMacroInput.SetSize(250, 32);
                _slotMacroInput.SetPosition(0, 2);
                _slotMacroInput.SetFontSize(14);

                _slotHotkeyButton = _slotEditorWindow.AddButtonComponent("", BeginHotkeyCapture);
                _slotHotkeyButton.SetSize(250, 30);
                _slotHotkeyButton.SetPosition(0, -40);
                _slotHotkeyButton.SetFontSize(13);

                var saveButton = _slotEditorWindow.AddButtonComponent("Save", SaveSlotEditor);
                saveButton.SetSize(78, 30);
                saveButton.SetPosition(-86, -88);
                saveButton.SetFontSize(13);

                var testButton = _slotEditorWindow.AddButtonComponent("Test", TestSlotEditor);
                testButton.SetSize(78, 30);
                testButton.SetPosition(0, -88);
                testButton.SetFontSize(13);

                var cancelButton = _slotEditorWindow.AddButtonComponent("Close", () => TryEnableWindow(_slotEditorWindow, false));
                cancelButton.SetSize(78, 30);
                cancelButton.SetPosition(86, -88);
                cancelButton.SetFontSize(13);
            }
            catch (Exception ex)
            {
                Chat.AddInfoMessage($"Macro Relay could not open slot editor: {ex.GetType().Name}: {ex.Message}");
                return;
            }
        }

        _slotEditorTitle?.SetText($"Slot {slotIndex + 1} - {slot.Shortcut}{Environment.NewLine}Top: button label | Bottom: macro name");
        _slotLabelInput?.SetText(slot.Label);
        _slotMacroInput?.SetText(slot.Macro);
        _slotHotkeyButton?.SetText($"Hotkey: {slot.Shortcut}");
        TryEnableWindow(_slotEditorWindow, true);
        TryEnableText(_slotEditorTitle, true);
        TryEnableTextInput(_slotLabelInput, true);
        TryEnableTextInput(_slotMacroInput, true);
        TryEnableButton(_slotHotkeyButton, true);
    }

    private void SaveSlotEditor()
    {
        if (_editingSlotIndex < 0 || _editingSlotIndex >= _slots.Length)
        {
            return;
        }

        var current = _slots[_editingSlotIndex];
        _slots[_editingSlotIndex] = current with
        {
            Label = NormalizeSlotText(_slotLabelInput?.GetText(), current.Label),
            Macro = _slotMacroInput?.GetText()?.Trim() ?? ""
        };

        SavePathConfig();
        RebuildHotbar();
        TryEnableWindow(_slotEditorWindow, false);
        Chat.AddInfoMessage($"Macro Relay slot {_editingSlotIndex + 1} saved.");
    }

    private void TestSlotEditor()
    {
        var macro = _slotMacroInput?.GetText()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(macro))
        {
            Chat.AddInfoMessage("Macro Relay test requires a macro name.");
            return;
        }

        SendMacro(macro, true);
    }

    private void BeginHotkeyCapture()
    {
        _isCapturingHotkey = true;
        _slotHotkeyButton?.SetText("Press a key...");
    }

    private void UpdateHotkeyCapture()
    {
        if (!_isCapturingHotkey || _editingSlotIndex < 0 || _editingSlotIndex >= _slots.Length)
        {
            return;
        }

        var key = GetPressedAssignableKey();
        if (key == KeyCodes.None)
        {
            return;
        }

        var ctrl = Keyboard.IsHeld(KeyCodes.LeftControl) || Keyboard.IsHeld(KeyCodes.RightControl);
        var alt = Keyboard.IsHeld(KeyCodes.LeftAlt) || Keyboard.IsHeld(KeyCodes.RightAlt);
        var shift = Keyboard.IsHeld(KeyCodes.LeftShift) || Keyboard.IsHeld(KeyCodes.RightShift);
        var shortcut = FormatShortcut(key, ctrl, alt, shift);
        var current = _slots[_editingSlotIndex];
        _slots[_editingSlotIndex] = current with
        {
            KeyCode = key,
            Ctrl = ctrl,
            Alt = alt,
            Shift = shift,
            Shortcut = shortcut
        };

        _isCapturingHotkey = false;
        _slotHotkeyButton?.SetText($"Hotkey: {shortcut}");
    }

    private void UpdateHotkeys()
    {
        var visibleCount = Math.Clamp(_visibleSlotCount, 1, Math.Max(1, _slots.Length));
        for (var i = 0; i < visibleCount; i++)
        {
            var slot = _slots[i];
            if (IsSlotHotkeyPressed(slot))
            {
                ActivateSlot(i, true);
            }
        }
    }

    private void ActivateSlot(int slotIndex, bool notify)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Length)
        {
            return;
        }

        var slot = _slots[slotIndex];
        if (string.IsNullOrWhiteSpace(slot.Macro))
        {
            if (notify)
            {
                Chat.AddInfoMessage($"Macro Relay slot {slotIndex + 1} has no macro configured.");
            }

            return;
        }

        SendMacro(slot.Macro, notify);
    }

    private void ToggleReceiver()
    {
        _receiverEnabled = !_receiverEnabled;
        _lastStatus = _receiverEnabled ? "Receiver enabled." : "Receiver disabled.";
        SavePathConfig();
        RefreshReceiverButton(true);
        Chat.AddInfoMessage(_lastStatus);
    }

    private void RefreshReceiverButton(bool force = false)
    {
        if (_receiverToggleButton == null)
        {
            return;
        }

        if (!force && DateTime.UtcNow < _nextReceiverButtonRefreshAt)
        {
            return;
        }

        _nextReceiverButtonRefreshAt = DateTime.UtcNow.AddSeconds(_receiverEnabled ? 0.5 : 2.0);
        var text = _receiverEnabled
            ? ((DateTime.UtcNow.Millisecond / 500) % 2 == 0 ? "ON*" : "ON")
            : "OFF";

        if (!force && text.Equals(_lastReceiverButtonText, StringComparison.Ordinal))
        {
            return;
        }

        _lastReceiverButtonText = text;
        try
        {
            _receiverToggleButton.SetText(text);
        }
        catch
        {
            _receiverToggleButton = null;
        }
    }

    private void AddHotbarSlot()
    {
        if (_visibleSlotCount < _slots.Length)
        {
            _visibleSlotCount++;
            SavePathConfig();
            RebuildHotbar();
            return;
        }

        _slots = _slots.Append(CreateDefaultSlot(_slots.Length + 1)).ToArray();
        _visibleSlotCount = _slots.Length;
        SavePathConfig();
        RebuildHotbar();
    }

    private void RemoveHotbarSlot()
    {
        if (_visibleSlotCount <= 1)
        {
            Chat.AddInfoMessage("Macro Relay hotbar needs at least one slot.");
            return;
        }

        _visibleSlotCount--;
        SavePathConfig();
        RebuildHotbar();
    }

    private void ReadRequest()
    {
        _lastRead = DateTime.UtcNow;
        if (!File.Exists(_requestPath))
        {
            _lastStatus = $"Waiting for macro request file: {_requestPath}";
            return;
        }

        MacroRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<MacroRequest>(File.ReadAllText(_requestPath), ConfigJsonOptions);
        }
        catch (Exception ex)
        {
            _lastStatus = $"Could not read macro request: {ex.GetType().Name}";
            return;
        }

        if (request == null || string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.MacroName))
        {
            _lastStatus = "Macro request file is empty or invalid.";
            return;
        }

        if (request.RequestId == _lastRequestId || request.SenderProcessId == Process.GetCurrentProcess().Id)
        {
            return;
        }

        var ageSeconds = (DateTime.UtcNow - request.TimestampUtc).TotalSeconds;
        if (ageSeconds > 5)
        {
            _lastStatus = $"Ignored stale macro request: {request.MacroName}";
            return;
        }

        _lastRequestId = request.RequestId;
        TryApplyTargets(request);
        TryActivateMacro(request.MacroName, true);
    }

    private void TryApplyTargets(MacroRequest request)
    {
        if (!request.SyncTargets || _localPlayer == null)
        {
            return;
        }

        var applied = new List<string>();
        var failed = new List<string>();
        if (request.OffensiveTarget != null)
        {
            if (_localPlayer.TrySetOffensiveTarget(request.OffensiveTarget.ToSnapshot()))
            {
                applied.Add($"offensive {request.OffensiveTarget.Name}");
            }
            else
            {
                failed.Add($"offensive {request.OffensiveTarget.Name}");
            }
        }

        if (request.DefensiveTarget != null)
        {
            if (_localPlayer.TrySetDefensiveTarget(request.DefensiveTarget.ToSnapshot()))
            {
                applied.Add($"defensive {request.DefensiveTarget.Name}");
            }
            else
            {
                failed.Add($"defensive {request.DefensiveTarget.Name}");
            }
        }

        if (applied.Count > 0)
        {
            _lastStatus = $"Applied relayed target: {string.Join(", ", applied)}";
        }

        if (failed.Count > 0)
        {
            Chat.AddInfoMessage($"Macro Relay could not apply target: {string.Join(", ", failed)}");
        }
    }

    private static TargetPayload? CreateTargetPayload(TargetSnapshot? target)
    {
        return target == null || target.NetworkId == 0
            ? null
            : new TargetPayload(target.Name, target.CharacterId, target.NetworkId);
    }

    private static string FormatTarget(TargetSnapshot? target)
    {
        return target == null || target.NetworkId == 0
            ? "none"
            : $"{target.Name} characterId={target.CharacterId} networkId={target.NetworkId}";
    }

    private static string JoinArgs(string[] args, int startIndex)
    {
        return startIndex >= args.Length ? "" : string.Join(" ", args.Skip(startIndex)).Trim();
    }

    private void LoadPathConfig()
    {
        _relayFolder = DefaultRelayFolder;
        _requestPath = Path.Combine(_relayFolder, "macro-request.json");
        _pathConfigStatus = "Using default Macro Relay paths.";

        var configPath = LocalPathConfigPath;
        if (!File.Exists(configPath))
        {
            TryWriteDefaultPathConfig(configPath);
            return;
        }

        try
        {
            var config = JsonSerializer.Deserialize<PathConfig>(File.ReadAllText(configPath), ConfigJsonOptions);
            var configuredRequestPath = ExpandConfiguredPath(FirstNonBlank(config?.RequestPath, config?.RelayPath, config?.RequestFile));
            var configuredRelayFolder = ExpandConfiguredPath(
                FirstNonBlank(
                    config?.RelayFolder,
                    config?.DataFolder,
                    config?.SharedFolder,
                    config?.Directory,
                    config?.Folder));

            if (!string.IsNullOrWhiteSpace(configuredRequestPath))
            {
                _requestPath = configuredRequestPath;
                _relayFolder = Path.GetDirectoryName(_requestPath) ?? DefaultRelayFolder;
                _pathConfigStatus = $"Loaded RequestPath from {configPath}.";
            }
            else if (!string.IsNullOrWhiteSpace(configuredRelayFolder))
            {
                _relayFolder = configuredRelayFolder;
                _requestPath = Path.Combine(_relayFolder, "macro-request.json");
                _pathConfigStatus = $"Loaded RelayFolder from {configPath}.";
            }
            else
            {
                _pathConfigStatus = $"Config at {configPath} did not set RelayFolder or RequestPath; using defaults.";
            }

            _slots = BuildSlots(config?.Slots);
            _visibleSlotCount = Math.Clamp(config?.VisibleSlots ?? _slots.Length, 1, Math.Max(1, _slots.Length));
            _receiverEnabled = config?.ReceiverEnabled ?? _receiverEnabled;
            _targetSyncEnabled = config?.TargetSyncEnabled ?? _targetSyncEnabled;
        }
        catch (Exception ex)
        {
            _pathConfigStatus = $"Could not read config at {configPath}; using defaults.";
            Logger.Error($"Macro Relay path config failed: {ex}");
        }
    }

    private void ReportPathConfiguration()
    {
        Chat.AddInfoMessage($"Macro Relay config: {LocalPathConfigPath}");
        Chat.AddInfoMessage($"Macro Relay config status: {_pathConfigStatus}");
        Chat.AddInfoMessage($"Macro Relay folder: {_relayFolder}");
        Chat.AddInfoMessage($"Macro Relay request file: {_requestPath}");
    }

    private void TryWriteDefaultPathConfig(string configPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath) ?? _gameFolder);
            var config = new PathConfig(
                RelayFolder: DefaultRelayFolder,
                RequestPath: null,
                Slots: CreateDefaultSlots().Select(slot => new HotbarSlotConfig(slot.Label, slot.Macro, slot.Shortcut, slot.KeyCode, slot.Ctrl, slot.Alt, slot.Shift)).ToArray(),
                VisibleSlots: _visibleSlotCount,
                ReceiverEnabled: _receiverEnabled,
                TargetSyncEnabled: _targetSyncEnabled);
            File.WriteAllText(configPath, JsonSerializer.Serialize(config, JsonOptions));
            _pathConfigStatus = $"Created default config at {configPath}.";
        }
        catch (Exception ex)
        {
            _pathConfigStatus = $"Config missing at {configPath}; using defaults.";
            Logger.Error($"Macro Relay default config creation failed: {ex}");
        }
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

    private static HotbarSlot[] BuildSlots(HotbarSlotConfig[]? configuredSlots)
    {
        var defaultSlots = CreateDefaultSlots();
        if (configuredSlots == null || configuredSlots.Length == 0)
        {
            return defaultSlots;
        }

        var count = Math.Max(1, configuredSlots.Length);
        var slots = new HotbarSlot[count];
        for (var i = 0; i < count; i++)
        {
            var configured = configuredSlots[i];
            var fallback = i < defaultSlots.Length ? defaultSlots[i] : CreateDefaultSlot(i + 1);
            var parsed = ParseShortcut(configured.Shortcut);
            var keyCode = configured.KeyCode > 0 ? configured.KeyCode : parsed.KeyCode > 0 ? parsed.KeyCode : fallback.KeyCode;
            var ctrl = configured.Ctrl ?? parsed.Ctrl ?? fallback.Ctrl;
            var alt = configured.Alt ?? parsed.Alt ?? fallback.Alt;
            var shift = configured.Shift ?? parsed.Shift ?? fallback.Shift;

            slots[i] = new HotbarSlot(
                Label: string.IsNullOrWhiteSpace(configured.Label) ? fallback.Label : configured.Label.Trim(),
                Macro: configured.Macro?.Trim() ?? "",
                Shortcut: FormatShortcut(keyCode, ctrl, alt, shift),
                KeyCode: keyCode,
                Ctrl: ctrl,
                Alt: alt,
                Shift: shift);
        }

        return slots;
    }

    private void SavePathConfig()
    {
        var config = new PathConfig(
            RelayFolder: _relayFolder,
            RequestPath: null,
            Slots: _slots.Select(slot => new HotbarSlotConfig(slot.Label, slot.Macro, slot.Shortcut, slot.KeyCode, slot.Ctrl, slot.Alt, slot.Shift)).ToArray(),
            VisibleSlots: _visibleSlotCount,
            ReceiverEnabled: _receiverEnabled,
            TargetSyncEnabled: _targetSyncEnabled);

        Directory.CreateDirectory(Path.GetDirectoryName(LocalPathConfigPath) ?? _gameFolder);
        File.WriteAllText(LocalPathConfigPath, JsonSerializer.Serialize(config, JsonOptions));
    }

    private static string NormalizeSlotText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static HotbarSlot[] CreateDefaultSlots()
    {
        return new[]
        {
            new HotbarSlot("Slot 1", "", "Ctrl+F1", KeyCodes.F1, true, false, false),
            new HotbarSlot("Slot 2", "", "Ctrl+F2", KeyCodes.F2, true, false, false),
            new HotbarSlot("Slot 3", "", "Ctrl+F3", KeyCodes.F3, true, false, false),
            new HotbarSlot("Slot 4", "", "Ctrl+F4", KeyCodes.F4, true, false, false),
            new HotbarSlot("Slot 5", "", "Ctrl+F5", KeyCodes.F5, true, false, false),
            new HotbarSlot("Slot 6", "", "Ctrl+F6", KeyCodes.F6, true, false, false)
        };
    }

    private static HotbarSlot CreateDefaultSlot(int slotNumber)
    {
        var keyCode = GetDefaultKeyCode(slotNumber);
        return new HotbarSlot($"Slot {slotNumber}", "", $"Ctrl+{GetKeyName(keyCode)}", keyCode, true, false, false);
    }

    private static string GetSlotButtonText(HotbarSlot slot)
    {
        var label = string.IsNullOrWhiteSpace(slot.Label) ? "Macro" : slot.Label;
        return $"{label}{Environment.NewLine}{CompactShortcut(slot.Shortcut)}";
    }

    private static string CompactShortcut(string shortcut)
    {
        return shortcut
            .Replace("Ctrl", "C", StringComparison.OrdinalIgnoreCase)
            .Replace("Alt", "A", StringComparison.OrdinalIgnoreCase)
            .Replace("Shift", "S", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSlotHotkeyPressed(HotbarSlot slot)
    {
        if (!Keyboard.IsKeyDown(slot.KeyCode))
        {
            return false;
        }

        return IsModifierStateMatched(slot.Ctrl, KeyCodes.LeftControl, KeyCodes.RightControl)
            && IsModifierStateMatched(slot.Alt, KeyCodes.LeftAlt, KeyCodes.RightAlt)
            && IsModifierStateMatched(slot.Shift, KeyCodes.LeftShift, KeyCodes.RightShift);
    }

    private bool IsModifierStateMatched(bool required, int leftKey, int rightKey)
    {
        var held = Keyboard.IsHeld(leftKey) || Keyboard.IsHeld(rightKey);
        return required == held;
    }

    private int GetPressedAssignableKey()
    {
        foreach (var key in AssignableKeys)
        {
            if (Keyboard.IsKeyDown(key))
            {
                return key;
            }
        }

        return KeyCodes.None;
    }

    private static int GetDefaultKeyCode(int slotNumber)
    {
        return slotNumber switch
        {
            1 => KeyCodes.F1,
            2 => KeyCodes.F2,
            3 => KeyCodes.F3,
            4 => KeyCodes.F4,
            5 => KeyCodes.F5,
            6 => KeyCodes.F6,
            7 => KeyCodes.F7,
            8 => KeyCodes.F8,
            9 => KeyCodes.F9,
            10 => KeyCodes.F10,
            11 => KeyCodes.F11,
            _ => KeyCodes.F12
        };
    }

    private static string FormatShortcut(int keyCode, bool ctrl, bool alt, bool shift)
    {
        var parts = new List<string>();
        if (ctrl)
        {
            parts.Add("Ctrl");
        }

        if (alt)
        {
            parts.Add("Alt");
        }

        if (shift)
        {
            parts.Add("Shift");
        }

        parts.Add(GetKeyName(keyCode));
        return string.Join("+", parts);
    }

    private static (int KeyCode, bool? Ctrl, bool? Alt, bool? Shift) ParseShortcut(string? shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return (KeyCodes.None, null, null, null);
        }

        var ctrl = false;
        var alt = false;
        var shift = false;
        var key = KeyCodes.None;
        foreach (var part in shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                ctrl = true;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                alt = true;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                shift = true;
            }
            else
            {
                key = GetKeyCode(part);
            }
        }

        return (key, ctrl, alt, shift);
    }

    private static string GetKeyName(int keyCode)
    {
        if (keyCode >= KeyCodes.A && keyCode <= KeyCodes.Z)
        {
            return ((char)('A' + keyCode - KeyCodes.A)).ToString();
        }

        if (keyCode >= KeyCodes.Alpha0 && keyCode <= KeyCodes.Alpha9)
        {
            return ((char)('0' + keyCode - KeyCodes.Alpha0)).ToString();
        }

        if (keyCode >= KeyCodes.F1 && keyCode <= KeyCodes.F15)
        {
            return $"F{keyCode - KeyCodes.F1 + 1}";
        }

        if (keyCode >= KeyCodes.Keypad0 && keyCode <= KeyCodes.Keypad9)
        {
            return $"Num{keyCode - KeyCodes.Keypad0}";
        }

        return keyCode switch
        {
            KeyCodes.Space => "Space",
            KeyCodes.Tab => "Tab",
            KeyCodes.Return => "Enter",
            KeyCodes.Escape => "Esc",
            KeyCodes.BackQuote => "`",
            KeyCodes.Minus => "-",
            KeyCodes.Equals => "=",
            KeyCodes.LeftBracket => "[",
            KeyCodes.RightBracket => "]",
            KeyCodes.Backslash => "\\",
            KeyCodes.Semicolon => ";",
            KeyCodes.Quote => "'",
            KeyCodes.Comma => ",",
            KeyCodes.Period => ".",
            KeyCodes.Slash => "/",
            _ => $"Key{keyCode}"
        };
    }

    private static int GetKeyCode(string keyName)
    {
        if (keyName.Length == 1)
        {
            var character = char.ToUpperInvariant(keyName[0]);
            if (character >= 'A' && character <= 'Z')
            {
                return KeyCodes.A + character - 'A';
            }

            if (character >= '0' && character <= '9')
            {
                return KeyCodes.Alpha0 + character - '0';
            }
        }

        if (keyName.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(keyName[1..], out var functionNumber))
        {
            return functionNumber is >= 1 and <= 15 ? KeyCodes.F1 + functionNumber - 1 : KeyCodes.None;
        }

        if (keyName.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && int.TryParse(keyName[3..], out var numpadNumber))
        {
            return numpadNumber is >= 0 and <= 9 ? KeyCodes.Keypad0 + numpadNumber : KeyCodes.None;
        }

        return keyName.ToLowerInvariant() switch
        {
            "space" => KeyCodes.Space,
            "tab" => KeyCodes.Tab,
            "enter" or "return" => KeyCodes.Return,
            "esc" or "escape" => KeyCodes.Escape,
            "`" => KeyCodes.BackQuote,
            "-" => KeyCodes.Minus,
            "=" => KeyCodes.Equals,
            "[" => KeyCodes.LeftBracket,
            "]" => KeyCodes.RightBracket,
            "\\" => KeyCodes.Backslash,
            ";" => KeyCodes.Semicolon,
            "'" => KeyCodes.Quote,
            "," => KeyCodes.Comma,
            "." => KeyCodes.Period,
            "/" => KeyCodes.Slash,
            _ => KeyCodes.None
        };
    }

    private static string DefaultRelayFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PantheonMacroRelay");

    private string LocalPathConfigPath => Path.Combine(_gameFolder, "Mods", "PantheonAddons", "MacroRelayConfig.json");

    private sealed record PathConfig(
        string? RelayFolder,
        string? RequestPath,
        HotbarSlotConfig[]? Slots,
        int? VisibleSlots = null,
        bool? ReceiverEnabled = null,
        bool? TargetSyncEnabled = null,
        string? RelayPath = null,
        string? RequestFile = null,
        string? DataFolder = null,
        string? SharedFolder = null,
        string? Directory = null,
        string? Folder = null);

    private sealed record HotbarSlotConfig(string? Label, string? Macro, string? Shortcut, int KeyCode = 0, bool? Ctrl = null, bool? Alt = null, bool? Shift = null);

    private sealed record HotbarSlot(string Label, string Macro, string Shortcut, int KeyCode, bool Ctrl, bool Alt, bool Shift);

    private sealed record TargetPayload(string Name, long CharacterId, uint NetworkId)
    {
        public TargetSnapshot ToSnapshot()
        {
            return new TargetSnapshot(Name, CharacterId, NetworkId);
        }
    }

    private sealed record MacroRequest(
        int SchemaVersion,
        string RequestId,
        string MacroName,
        int SenderProcessId,
        DateTime TimestampUtc,
        bool SyncTargets = false,
        TargetPayload? OffensiveTarget = null,
        TargetPayload? DefensiveTarget = null);
}
