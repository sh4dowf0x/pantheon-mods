using PantheonAddonFramework;
using PantheonAddonFramework.Configuration;
using PantheonAddonFramework.Models;
using PantheonAddonFramework.UI;
using System.Diagnostics;
using System.Text.Json;

namespace PantheonAddons.FollowBeacon;

[AddonMetadata("Follow Beacon", "Codex", "Shares local player position for a second client to read as a follow-assist beacon")]
public sealed class FollowBeacon : Addon
{
    private const string DisabledMode = "Off";
    private const string LeaderMode = "Leader";
    private const string FollowerMode = "Follower";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions JsonLineOptions = new();
    private static readonly JsonSerializerOptions PathConfigJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _gameFolder = ResolveGameFolder();
    private string _beaconFolder = DefaultBeaconFolder;
    private string _beaconPath = Path.Combine(DefaultBeaconFolder, "leader-location.json");
    private string _inputLogPath = Path.Combine(DefaultBeaconFolder, "input-events.jsonl");
    private IPlayer? _localPlayer;
    private IAddonWindow? _window;
    private IAddonWindow? _controlsWindow;
    private IAddonTextComponent? _text;
    private IAddonButtonComponent? _leaderButton;
    private IAddonButtonComponent? _followerButton;
    private IAddonButtonComponent? _assistButton;
    private string _mode = DisabledMode;
    private bool _isEnabled;
    private bool _inputAwarenessEnabled = true;
    private bool _manualInputActive;
    private bool _assistEnabled;
    private bool _assistSprintActive;
    private bool _assistSprintLockedOut;
    private bool _wantsWindow;
    private bool _wantsControlsWindow = true;
    private bool _debugEnabled;
    private bool _reportedWindowFailure;
    private bool _reportedControlsWindowFailure;
    private DateTime _nextWindowAttemptAt = DateTime.MinValue;
    private DateTime _nextControlsWindowAttemptAt = DateTime.MinValue;
    private DateTime _lastWrite = DateTime.MinValue;
    private DateTime _lastRead = DateTime.MinValue;
    private DateTime _lastManualInputAt = DateTime.MinValue;
    private DateTime _manualInputUntil = DateTime.MinValue;
    private DateTime _movementProbeUntil = DateTime.MinValue;
    private DateTime _assistPulseUntil = DateTime.MinValue;
    private DateTime _assistNextPulseAt = DateTime.MinValue;
    private PlayerMovementInput _movementProbeInput = PlayerMovementInput.None;
    private PlayerMovementInput _assistPulseInput = PlayerMovementInput.None;
    private PlayerMovementInput _lastAssistTurnInput = PlayerMovementInput.None;
    private double _updateIntervalSeconds = 0.25;
    private double _manualInputPauseSeconds = 0.75;
    private double _followDistance = 3.0;
    private double _assistTurnDeadZoneDegrees = 70.0;
    private double _assistTurnStopDeadZoneDegrees = 8.0;
    private double _assistMoveDeadZoneDegrees = 10.0;
    private double _assistCurveDeadZoneDegrees = 70.0;
    private double _assistSprintStartDistance = 8.0;
    private double _assistSprintStopDistance = 5.0;
    private double _assistSprintStopEndurancePercent = 5.0;
    private double _assistSprintResumeEndurancePercent = 50.0;
    private double _assistPulseScale = 1.0;
    private double _staleWarningSeconds = 2.0;
    private double _smoothingAlpha = 0.35;
    private SmoothedVector? _smoothedDelta;
    private BeaconPayload? _lastBeacon;
    private string? _activeLeaderKey;
    private string _statusLine = "Follow Beacon is off.";
    private string _guidanceLine = "";
    private string _primaryLine = "FOLLOW BEACON OFF";
    private string _detailLine = "";
    private string _debugLine = "";
    private string _lastManualInputNames = "";
    private string _manualInputLine = "Manual input: none | Assist eligible: yes";
    private string _movementProbeLine = "Movement probe: idle";
    private string _assistLine = "Assist: off";
    private int _manualInputEventCount;
    private double _lastLeaderHorizontalDistance;
    private double _lastLeaderRelativeBearing;
    private double _lastLeaderAgeSeconds = double.PositiveInfinity;

    public override void OnCreate()
    {
        LoadPathConfig();
        CustomChatCommands.Add("/followbeacon", HandleCommand);
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
        _window?.Enable(false);
        _text?.Enable(false);
        _controlsWindow?.Enable(false);
    }

    public override IEnumerable<IConfigurationValue> GetConfiguration()
    {
        return new IConfigurationValue[]
        {
            new PicklistConfigurationValue("Mode", "Choose whether this client writes or reads the shared beacon file.", 0, new[] { DisabledMode, LeaderMode, FollowerMode }, value => SetMode(value, false)),
            new FloatConfigurationValue("Update interval", "Seconds between beacon file updates.", 0.25f, 0.1f, 2.0f, 0.05f, value => _updateIntervalSeconds = value),
            new FloatConfigurationValue("Follow distance", "Desired follower distance from the leader.", 3.0f, 0.5f, 15.0f, 0.5f, value => _followDistance = value),
            new FloatConfigurationValue("Assist turn deadzone", "Degrees off target before assist turns in place toward the leader.", 70.0f, 3.0f, 120.0f, 1.0f, value => _assistTurnDeadZoneDegrees = value),
            new FloatConfigurationValue("Assist turn stop deadzone", "Degrees off target where a held assist turn releases.", 8.0f, 2.0f, 30.0f, 1.0f, value => _assistTurnStopDeadZoneDegrees = value),
            new FloatConfigurationValue("Assist move deadzone", "Degrees off target allowed before assist moves straight forward.", 10.0f, 2.0f, 60.0f, 1.0f, value => _assistMoveDeadZoneDegrees = value),
            new FloatConfigurationValue("Assist curve deadzone", "Degrees off target where assist can move forward while turning.", 70.0f, 5.0f, 100.0f, 1.0f, value => _assistCurveDeadZoneDegrees = value),
            new FloatConfigurationValue("Assist sprint start distance", "Follower distance where assist starts sprinting to catch up.", 8.0f, 2.0f, 30.0f, 0.5f, value => _assistSprintStartDistance = value),
            new FloatConfigurationValue("Assist sprint stop distance", "Follower distance where assist stops catch-up sprinting.", 5.0f, 1.0f, 30.0f, 0.5f, value => _assistSprintStopDistance = value),
            new FloatConfigurationValue("Assist hold scale", "Lower values use shorter assist holds; higher values are more aggressive.", 1.0f, 0.25f, 2.0f, 0.05f, value => _assistPulseScale = value),
            new FloatConfigurationValue("Smoothing", "Higher values react faster; lower values reduce jitter.", 0.35f, 0.05f, 1.0f, 0.05f, value => _smoothingAlpha = value),
            new FloatConfigurationValue("Stale warning", "Seconds before the follower marks the leader signal stale.", 2.0f, 0.5f, 10.0f, 0.5f, value => _staleWarningSeconds = value),
            new BoolConfigurationValue("Input awareness", "Shows when manual movement input is detected.", true, value =>
            {
                _inputAwarenessEnabled = value;
                UpdateManualInputLine();
                RefreshText();
            }),
            new BoolConfigurationValue("Debug readout", "Shows raw bearing and delta values in the Follow Beacon window.", false, value =>
            {
                _debugEnabled = value;
                RefreshText();
            })
        };
    }

    public override void Dispose()
    {
        CustomChatCommands.Remove("/followbeacon");
        LocalPlayerEvents.LocalPlayerEntered.Unsubscribe(OnLocalPlayerEntered);
        LifecycleEvents.OnUpdate.Unsubscribe(OnUpdate);
        _text?.Destroy();
        _window?.Destroy();
        _controlsWindow?.Destroy();
    }

    private void HandleCommand(string[] args)
    {
        if (args.Length == 0 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage("Follow Beacon: /followbeacon leader, follower, off, resetleader, show, hide, controls show|hide, status, path, once, distance <meters>, input on|off, debug on|off, probe <input> <seconds>, assist on|off");
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "leader":
                SetMode(1);
                break;
            case "follower":
                SetMode(2);
                break;
            case "off":
                SetMode(0);
                break;
            case "resetleader":
            case "clearleader":
                ResetLeaderState("Leader state reset. The next valid beacon will be accepted.");
                break;
            case "show":
                _wantsWindow = true;
                TryEnsureWindow(true);
                RefreshText();
                break;
            case "hide":
                _wantsWindow = false;
                _window?.Enable(false);
                _text?.Enable(false);
                Chat.AddInfoMessage("Follow Beacon window hidden. Use /followbeacon show to reopen.");
                break;
            case "controls":
                HandleControlsCommand(args);
                break;
            case "status":
                Chat.AddInfoMessage($"{_statusLine} File: {_beaconPath}");
                break;
            case "path":
                Chat.AddInfoMessage($"Follow Beacon file: {_beaconPath}");
                Chat.AddInfoMessage($"Follow Beacon config: {LocalPathConfigPath}");
                break;
            case "once":
                WriteBeacon(true);
                break;
            case "distance":
                HandleDistanceCommand(args);
                break;
            case "input":
                HandleInputCommand(args);
                break;
            case "debug":
                HandleDebugCommand(args);
                break;
            case "probe":
                HandleProbeCommand(args);
                break;
            case "assist":
                HandleAssistCommand(args);
                break;
            default:
                Chat.AddInfoMessage("Follow Beacon: unknown command. Try /followbeacon help.");
                break;
        }
    }

    private void OnLocalPlayerEntered(IPlayer player)
    {
        if (!player.IsLocalPlayer)
        {
            return;
        }

        _localPlayer = player;
        _statusLine = $"Local player: {player.Name}. Mode: {_mode}.";
        _detailLine = "";
        RefreshText();
    }

    private void OnUpdate()
    {
        if (!_isEnabled)
        {
            return;
        }

        if (_wantsWindow && (_mode == LeaderMode || _mode == FollowerMode))
        {
            TryEnsureWindow(false);
        }

        if (_wantsControlsWindow)
        {
            TryEnsureControlsWindow(false);
        }

        UpdateManualInputState();
        UpdateMovementProbe();

        if (_mode == LeaderMode && Due(_lastWrite))
        {
            WriteBeacon(false);
        }
        else if (_mode == FollowerMode && Due(_lastRead))
        {
            ReadBeacon();
        }

        UpdateAssist();
    }

    private void SetMode(int index, bool notifyChat = true)
    {
        _mode = index switch
        {
            1 => LeaderMode,
            2 => FollowerMode,
            _ => DisabledMode
        };

        _statusLine = $"Follow Beacon mode: {_mode}.";
        _guidanceLine = "";
        _primaryLine = _mode switch
        {
            LeaderMode => "WRITING LEADER BEACON",
            FollowerMode => "WAITING FOR LEADER",
            _ => "FOLLOW BEACON OFF"
        };
        _detailLine = "";
        _debugLine = "";
        SetAssistEnabled(false, false);
        UpdateManualInputLine();
        RefreshControlButtons();
        _smoothedDelta = null;
        if (_mode != FollowerMode)
        {
            _activeLeaderKey = null;
            _lastBeacon = null;
            _lastLeaderAgeSeconds = double.PositiveInfinity;
        }

        if (notifyChat)
        {
            SafeAddInfoMessage($"{_statusLine} Shared file: {_beaconPath}");
        }

        if (_mode == DisabledMode)
        {
            _window?.Enable(false);
            _text?.Enable(false);
        }
        else if (_wantsWindow)
        {
            TryEnsureWindow(true);
        }

        RefreshText();
        RefreshControlButtons();
    }

    private bool Due(DateTime lastRun)
    {
        return (DateTime.UtcNow - lastRun).TotalSeconds >= _updateIntervalSeconds;
    }

    private void WriteBeacon(bool notify)
    {
        _lastWrite = DateTime.UtcNow;

        if (_localPlayer == null)
        {
            _statusLine = "Waiting for local player before writing beacon.";
            _primaryLine = "WAITING FOR PLAYER";
            _detailLine = "";
            RefreshText();
            return;
        }

        var position = _localPlayer.GetPosition();
        if (position == null)
        {
            _statusLine = "Local player position is not available yet.";
            _primaryLine = "POSITION UNAVAILABLE";
            _detailLine = "";
            RefreshText();
            return;
        }

        var payload = new BeaconPayload(
            SchemaVersion: 1,
            CharacterName: _localPlayer.Name,
            CharacterId: _localPlayer.CharacterId,
            ProcessId: Process.GetCurrentProcess().Id,
            TimestampUtc: DateTime.UtcNow,
            X: position.X,
            Y: position.Y,
            Z: position.Z,
            HeadingY: position.HeadingY);

        Directory.CreateDirectory(_beaconFolder);
        var tempPath = _beaconPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(payload, JsonOptions));
        File.Move(tempPath, _beaconPath, true);

        _lastBeacon = payload;
        _statusLine = $"Leader beacon written: {payload.CharacterName} X={payload.X:F2} Y={payload.Y:F2} Z={payload.Z:F2}";
        _primaryLine = "LEADER BEACON ACTIVE";
        _detailLine = $"{payload.CharacterName} broadcasting position";
        _debugLine = $"X={payload.X:F2} Y={payload.Y:F2} Z={payload.Z:F2} heading={payload.HeadingY:F0}";
        if (notify)
        {
            Chat.AddInfoMessage(_statusLine);
        }

        RefreshText();
    }

    private void ReadBeacon()
    {
        _lastRead = DateTime.UtcNow;

        if (!File.Exists(_beaconPath))
        {
            _activeLeaderKey = null;
            _lastBeacon = null;
            ResetFollowerTrackingState();
            _statusLine = $"Waiting for leader beacon file: {_beaconPath}";
            _primaryLine = "WAITING FOR LEADER";
            _detailLine = "No beacon file yet";
            _debugLine = _beaconPath;
            RefreshText();
            return;
        }

        BeaconPayload? beacon;
        try
        {
            beacon = JsonSerializer.Deserialize<BeaconPayload>(File.ReadAllText(_beaconPath));
        }
        catch (Exception ex)
        {
            _statusLine = $"Could not read beacon: {ex.GetType().Name}";
            _primaryLine = "BEACON READ ERROR";
            _detailLine = ex.GetType().Name;
            _debugLine = ex.Message;
            RefreshText();
            return;
        }

        if (beacon == null)
        {
            _statusLine = "Beacon file was empty.";
            _primaryLine = "EMPTY BEACON";
            _detailLine = "";
            _debugLine = "";
            RefreshText();
            return;
        }

        var leaderKey = GetLeaderKey(beacon);
        if (_activeLeaderKey != null && _activeLeaderKey != leaderKey)
        {
            ResetFollowerTrackingState();
            Chat.AddInfoMessage($"Follow Beacon leader changed to {beacon.CharacterName}.");
        }

        _activeLeaderKey = leaderKey;
        _lastBeacon = beacon;

        if (_localPlayer == null)
        {
            _statusLine = $"Leader beacon found for {beacon.CharacterName}; waiting for local player.";
            _primaryLine = "WAITING FOR PLAYER";
            _detailLine = $"Leader {beacon.CharacterName} found";
            _debugLine = "";
            RefreshText();
            return;
        }

        var localPosition = _localPlayer.GetPosition();
        if (localPosition == null)
        {
            _statusLine = "Follower position is not available yet.";
            _primaryLine = "POSITION UNAVAILABLE";
            _detailLine = "";
            _debugLine = "";
            RefreshText();
            return;
        }

        var dx = beacon.X - localPosition.X;
        var dy = beacon.Y - localPosition.Y;
        var dz = beacon.Z - localPosition.Z;

        _smoothedDelta = SmoothDelta(dx, dy, dz);

        var horizontalDistance = Math.Sqrt(_smoothedDelta.X * _smoothedDelta.X + _smoothedDelta.Z * _smoothedDelta.Z);
        var distance = Math.Sqrt(_smoothedDelta.X * _smoothedDelta.X + _smoothedDelta.Y * _smoothedDelta.Y + _smoothedDelta.Z * _smoothedDelta.Z);
        var ageSeconds = (DateTime.UtcNow - beacon.TimestampUtc).TotalSeconds;
        var bearingDegrees = NormalizeDegrees(Math.Atan2(_smoothedDelta.X, _smoothedDelta.Z) * 180.0 / Math.PI);
        var relativeBearing = NormalizeSignedDegrees(bearingDegrees - localPosition.HeadingY);
        var turnInstruction = CreateTurnInstruction(relativeBearing);
        var moveInstruction = CreateMoveInstruction(horizontalDistance, relativeBearing, ageSeconds);
        var stale = ageSeconds > _staleWarningSeconds ? " stale" : "";
        _lastLeaderHorizontalDistance = horizontalDistance;
        _lastLeaderRelativeBearing = relativeBearing;
        _lastLeaderAgeSeconds = ageSeconds;

        _statusLine = $"Leader {beacon.CharacterName}: distance={distance:F1}m horizontal={horizontalDistance:F1}m age={ageSeconds:F1}s{stale}";
        _primaryLine = moveInstruction.ToUpperInvariant();
        _detailLine = $"{turnInstruction} | {horizontalDistance:F1}m to {beacon.CharacterName}";
        _guidanceLine = $"{moveInstruction}; {turnInstruction}; bearing={relativeBearing:+0;-0;0} deg; delta X={_smoothedDelta.X:F1} Y={_smoothedDelta.Y:F1} Z={_smoothedDelta.Z:F1}";
        _debugLine = $"bearing={relativeBearing:+0;-0;0} deg | delta X={_smoothedDelta.X:F1} Y={_smoothedDelta.Y:F1} Z={_smoothedDelta.Z:F1} | age={ageSeconds:F1}s";
        UpdateManualInputLine();
        UpdateAssistLine(PlayerMovementInput.None, GetAssistStopReason());
        RefreshText();
    }

    private void ResetLeaderState(string? chatMessage = null)
    {
        ResetFollowerTrackingState();
        _activeLeaderKey = null;
        _lastBeacon = null;
        _statusLine = "Waiting for leader beacon.";
        _primaryLine = "WAITING FOR LEADER";
        _detailLine = "";
        _debugLine = "";
        UpdateAssistLine(PlayerMovementInput.None, GetAssistStopReason());

        if (!string.IsNullOrWhiteSpace(chatMessage))
        {
            Chat.AddInfoMessage(chatMessage);
        }

        RefreshText();
    }

    private void ResetFollowerTrackingState()
    {
        _smoothedDelta = null;
        _lastLeaderHorizontalDistance = 0;
        _lastLeaderRelativeBearing = 0;
        _lastLeaderAgeSeconds = double.PositiveInfinity;
        _localPlayer?.TryApplyMovementInput(PlayerMovementInput.None);
        ResetAssistPulse();
        ResetAssistSprint();
    }

    private void HandleDistanceCommand(string[] args)
    {
        if (args.Length < 2 || !double.TryParse(args[1], out var distance))
        {
            Chat.AddInfoMessage($"Follow Beacon follow distance is {_followDistance:F1}m.");
            return;
        }

        _followDistance = Math.Clamp(distance, 0.5, 15.0);
        Chat.AddInfoMessage($"Follow Beacon follow distance set to {_followDistance:F1}m.");
        RefreshText();
    }

    private void HandleInputCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Chat.AddInfoMessage($"Follow Beacon input awareness is {(_inputAwarenessEnabled ? "on" : "off")}.");
            return;
        }

        if (args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage($"{_manualInputLine}; last={FormatLastManualInput()}; events={_manualInputEventCount}; log={_inputLogPath}");
            return;
        }

        if (args[1].Equals("path", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage($"Follow Beacon input log: {_inputLogPath}");
            return;
        }

        _inputAwarenessEnabled = args[1].Equals("on", StringComparison.OrdinalIgnoreCase);
        _manualInputActive = false;
        _manualInputUntil = DateTime.MinValue;
        _lastManualInputNames = "";
        UpdateManualInputLine();
        Chat.AddInfoMessage($"Follow Beacon input awareness {(_inputAwarenessEnabled ? "enabled" : "disabled")}.");
        RefreshText();
    }

    private void HandleDebugCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Chat.AddInfoMessage($"Follow Beacon debug readout is {(_debugEnabled ? "on" : "off")}.");
            return;
        }

        _debugEnabled = args[1].Equals("on", StringComparison.OrdinalIgnoreCase);
        Chat.AddInfoMessage($"Follow Beacon debug readout {(_debugEnabled ? "enabled" : "disabled")}.");
        RefreshText();
    }

    private SmoothedVector SmoothDelta(double dx, double dy, double dz)
    {
        if (_smoothedDelta == null)
        {
            return new SmoothedVector(dx, dy, dz);
        }

        var alpha = Math.Clamp(_smoothingAlpha, 0.05, 1.0);
        return new SmoothedVector(
            Lerp(_smoothedDelta.X, dx, alpha),
            Lerp(_smoothedDelta.Y, dy, alpha),
            Lerp(_smoothedDelta.Z, dz, alpha));
    }

    private void UpdateManualInputState()
    {
        if (!_inputAwarenessEnabled)
        {
            _manualInputActive = false;
            _lastManualInputNames = "";
            UpdateManualInputLine();
            return;
        }

        var pressed = GetHeldMovementInputs();
        if (pressed.Length > 0)
        {
            var inputNames = string.Join("+", pressed);
            if (!_manualInputActive || !inputNames.Equals(_lastManualInputNames, StringComparison.Ordinal))
            {
                RecordManualInputEvent("active", inputNames);
            }

            _lastManualInputNames = inputNames;
            _lastManualInputAt = DateTime.UtcNow;
            _manualInputUntil = DateTime.UtcNow.AddSeconds(_manualInputPauseSeconds);
            _manualInputActive = true;
            _manualInputLine = $"Manual input: {inputNames} | Assist eligible: no";
            return;
        }

        var wasActive = _manualInputActive;
        _manualInputActive = DateTime.UtcNow < _manualInputUntil;
        if (wasActive && !_manualInputActive)
        {
            RecordManualInputEvent("clear", _lastManualInputNames);
        }

        UpdateManualInputLine();
    }

    private string[] GetHeldMovementInputs()
    {
        var held = new List<string>();
        AddIfHeld(held, KeyCodes.W, "W");
        AddIfHeld(held, KeyCodes.A, "A");
        AddIfHeld(held, KeyCodes.S, "S");
        AddIfHeld(held, KeyCodes.D, "D");
        AddIfHeld(held, KeyCodes.Q, "Q");
        AddIfHeld(held, KeyCodes.E, "E");
        return held.ToArray();
    }

    private void AddIfHeld(List<string> held, int keyCode, string name)
    {
        if (Keyboard.IsHeld(keyCode))
        {
            held.Add(name);
        }
    }

    private void UpdateManualInputLine()
    {
        if (!_inputAwarenessEnabled)
        {
            _manualInputLine = "Manual input: off | Assist eligible: unknown";
            return;
        }

        _manualInputLine = _manualInputActive
            ? $"Manual input: recent {FormatLastManualInput()} | Assist eligible: no"
            : "Manual input: none | Assist eligible: yes";
    }

    private void HandleProbeCommand(string[] args)
    {
        if (args.Length < 2 || args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage($"{_movementProbeLine}. Inputs: forward, backward, left, right, sprint, turnleft, turnright, stop");
            return;
        }

        if (args[1].Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            StopMovementProbe("Movement probe stopped.");
            return;
        }

        if (_localPlayer == null)
        {
            Chat.AddInfoMessage("Movement probe cannot run yet: no local player.");
            return;
        }

        UpdateManualInputState();
        if (_manualInputActive)
        {
            Chat.AddInfoMessage("Movement probe refused: manual input is active/recent.");
            return;
        }

        var input = ParseProbeInput(args[1]);
        if (input == PlayerMovementInput.None)
        {
            Chat.AddInfoMessage("Movement probe input must be: forward, backward, left, right, sprint, turnleft, turnright.");
            return;
        }

        var seconds = 0.25;
        if (args.Length >= 3 && double.TryParse(args[2], out var parsedSeconds))
        {
            seconds = Math.Clamp(parsedSeconds, 0.05, 0.5);
        }

        _movementProbeInput = input;
        _movementProbeUntil = DateTime.UtcNow.AddSeconds(seconds);
        _movementProbeLine = $"Movement probe: {input} for {seconds:F2}s";
        Chat.AddInfoMessage(_movementProbeLine);
        RefreshText();
    }

    private void HandleAssistCommand(string[] args)
    {
        if (args.Length < 2 || args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            Chat.AddInfoMessage(_assistLine);
            return;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "on":
                if (_mode != FollowerMode)
                {
                    Chat.AddInfoMessage("Assist refused: set this client to /followbeacon follower first.");
                    return;
                }

                SetAssistEnabled(true, true);
                break;
            case "off":
            case "stop":
                SetAssistEnabled(false, true);
                break;
            default:
                Chat.AddInfoMessage("Follow Beacon assist: on, off, status");
                break;
        }
    }

    private void HandleControlsCommand(string[] args)
    {
        if (args.Length < 2 || args[1].Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            _wantsControlsWindow = true;
            TryEnsureControlsWindow(true);
            RefreshControlButtons();
            return;
        }

        if (args[1].Equals("hide", StringComparison.OrdinalIgnoreCase))
        {
            _wantsControlsWindow = false;
            _controlsWindow?.Enable(false);
            Chat.AddInfoMessage("Follow Beacon controls hidden. Use /followbeacon controls show to reopen.");
            return;
        }

        Chat.AddInfoMessage("Follow Beacon controls: show, hide");
    }

    private void SetAssistEnabled(bool enabled, bool notify)
    {
        _assistEnabled = enabled;
        if (!enabled)
        {
            _localPlayer?.TryApplyMovementInput(PlayerMovementInput.None);
            ResetAssistPulse();
            ResetAssistSprint();
        }

        UpdateAssistLine(PlayerMovementInput.None, enabled ? GetAssistStopReason() : "off");
        if (notify)
        {
            Chat.AddInfoMessage(_assistLine);
        }

        RefreshText();
        RefreshControlButtons();
    }

    private void UpdateAssist()
    {
        if (!_assistEnabled)
        {
            return;
        }

        RefreshAssistMetricsFromCurrentPosition();
        UpdateAssistSprintLockout();
        var stopReason = GetAssistStopReason();
        if (!string.IsNullOrWhiteSpace(stopReason))
        {
            _localPlayer?.TryApplyMovementInput(PlayerMovementInput.None);
            ResetAssistPulse();
            UpdateAssistLine(PlayerMovementInput.None, stopReason);
            return;
        }

        var now = DateTime.UtcNow;
        if (now < _assistNextPulseAt)
        {
            UpdateAssistLine(PlayerMovementInput.None, "settling");
            return;
        }

        var input = ChooseAssistInput();
        if (input == PlayerMovementInput.None)
        {
            if (_assistPulseInput != PlayerMovementInput.None)
            {
                _localPlayer?.TryApplyMovementInput(PlayerMovementInput.None);
            }

            _assistPulseInput = PlayerMovementInput.None;
            _assistSprintActive = false;
            UpdateAssistLine(PlayerMovementInput.None, "aligned");
            return;
        }

        input = AddSprintIfUseful(input);
        var holdSeconds = GetAssistHoldSeconds(input);
        _assistPulseInput = input;
        _assistPulseUntil = now.AddSeconds(holdSeconds);
        var turnInput = GetTurnInput(input);
        if (turnInput != PlayerMovementInput.None)
        {
            _lastAssistTurnInput = turnInput;
        }

        if (_localPlayer == null || !_localPlayer.TryApplyMovementInput(input, holdSeconds))
        {
            ResetAssistPulse();
            UpdateAssistLine(PlayerMovementInput.None, "movement bridge unavailable");
            return;
        }

        UpdateAssistLine(input, "hold");
    }

    private string GetAssistStopReason()
    {
        if (!_assistEnabled)
        {
            return "off";
        }

        if (_mode != FollowerMode)
        {
            return "not follower mode";
        }

        if (_localPlayer == null)
        {
            return "waiting for local player";
        }

        if (_manualInputActive)
        {
            return "manual input";
        }

        if (_lastBeacon == null || double.IsPositiveInfinity(_lastLeaderAgeSeconds))
        {
            return "waiting for leader";
        }

        if (_lastLeaderAgeSeconds > _staleWarningSeconds)
        {
            return "leader signal stale";
        }

        if (_lastLeaderHorizontalDistance <= _followDistance)
        {
            return "within follow distance";
        }

        if (_movementProbeInput != PlayerMovementInput.None)
        {
            return "movement probe active";
        }

        return "";
    }

    private PlayerMovementInput ChooseAssistInput()
    {
        var bearing = _lastLeaderRelativeBearing;
        var absBearing = Math.Abs(bearing);
        var turnStartDegrees = Math.Max(_assistTurnDeadZoneDegrees, _assistTurnStopDeadZoneDegrees + 2.0);
        var curveDegrees = Math.Max(_assistCurveDeadZoneDegrees, _assistMoveDeadZoneDegrees);
        var stopDegrees = Math.Clamp(_assistTurnStopDeadZoneDegrees, 2.0, Math.Max(2.0, turnStartDegrees - 1.0));
        var turnInput = bearing > 0 ? PlayerMovementInput.TurnRight : PlayerMovementInput.TurnLeft;

        if (absBearing > turnStartDegrees)
        {
            if (_lastAssistTurnInput != PlayerMovementInput.None && turnInput != _lastAssistTurnInput && absBearing < _assistTurnDeadZoneDegrees + 10.0)
            {
                return PlayerMovementInput.None;
            }

            return turnInput;
        }

        if (absBearing <= Math.Max(_assistMoveDeadZoneDegrees, stopDegrees))
        {
            _lastAssistTurnInput = PlayerMovementInput.None;
            return PlayerMovementInput.Forward;
        }

        if (absBearing <= curveDegrees && _lastLeaderHorizontalDistance > _followDistance + 1.5)
        {
            return PlayerMovementInput.Forward | turnInput;
        }

        return PlayerMovementInput.None;
    }

    private void UpdateAssistLine(PlayerMovementInput input, string reason)
    {
        if (!_assistEnabled)
        {
            _assistLine = "Assist: off";
            return;
        }

        _assistLine = input == PlayerMovementInput.None
            ? $"Assist: paused ({reason})"
            : $"Assist: {input} {reason} | bearing={_lastLeaderRelativeBearing:+0;-0;0} deg | distance={_lastLeaderHorizontalDistance:F1}m | {GetAssistSprintStatus()}";
    }

    private double GetAssistHoldSeconds(PlayerMovementInput input)
    {
        if (HasForwardInput(input))
        {
            return GetAssistForwardHoldSeconds();
        }

        if (GetTurnInput(input) != PlayerMovementInput.None)
        {
            return GetAssistTurnHoldSeconds();
        }

        return 0.05;
    }

    private double GetAssistTurnHoldSeconds()
    {
        var scale = Math.Clamp(_assistPulseScale, 0.25, 2.0);
        return Math.Clamp(0.12 * scale, 0.05, 0.24);
    }

    private double GetAssistForwardHoldSeconds()
    {
        var scale = Math.Clamp(_assistPulseScale, 0.25, 2.0);
        var distancePastGoal = Math.Max(0.0, _lastLeaderHorizontalDistance - _followDistance);
        var seconds = distancePastGoal > 8.0 ? 0.75 : distancePastGoal > 4.0 ? 0.55 : 0.35;
        return Math.Clamp(seconds * scale, 0.15, 1.0);
    }

    private PlayerMovementInput AddSprintIfUseful(PlayerMovementInput input)
    {
        if (!HasForwardInput(input))
        {
            _assistSprintActive = false;
            return input;
        }

        if (_assistSprintLockedOut)
        {
            _assistSprintActive = false;
            return input;
        }

        var startDistance = Math.Max(_assistSprintStartDistance, _followDistance + 0.5);
        var stopDistance = Math.Min(_assistSprintStopDistance, startDistance - 0.5);
        if (stopDistance <= _followDistance)
        {
            stopDistance = _followDistance + 0.5;
        }

        if (_assistSprintActive)
        {
            _assistSprintActive = _lastLeaderHorizontalDistance > stopDistance;
        }
        else
        {
            _assistSprintActive = _lastLeaderHorizontalDistance >= startDistance;
        }

        return _assistSprintActive ? input | PlayerMovementInput.Sprint : input;
    }

    private void UpdateAssistSprintLockout()
    {
        var endurancePercent = GetEndurancePercent();
        if (endurancePercent == null)
        {
            _assistSprintLockedOut = false;
            return;
        }

        if (endurancePercent <= _assistSprintStopEndurancePercent)
        {
            _assistSprintLockedOut = true;
            _assistSprintActive = false;
            return;
        }

        if (endurancePercent >= _assistSprintResumeEndurancePercent)
        {
            _assistSprintLockedOut = false;
        }
    }

    private double? GetEndurancePercent()
    {
        if (_localPlayer == null || _localPlayer.Stats.MaxEndurance <= 0)
        {
            return null;
        }

        return Math.Clamp((_localPlayer.Stats.CurrentEndurance / _localPlayer.Stats.MaxEndurance) * 100.0, 0.0, 100.0);
    }

    private string GetAssistSprintStatus()
    {
        var endurancePercent = GetEndurancePercent();
        var endurance = endurancePercent == null ? "endurance n/a" : $"endurance={endurancePercent:F0}%";
        if (_assistSprintLockedOut)
        {
            return $"sprint locked ({endurance})";
        }

        return _assistSprintActive ? $"sprint on ({endurance})" : $"sprint off ({endurance})";
    }

    private void RefreshAssistMetricsFromCurrentPosition()
    {
        if (_lastBeacon == null || _localPlayer == null)
        {
            return;
        }

        var localPosition = _localPlayer.GetPosition();
        if (localPosition == null)
        {
            return;
        }

        var dx = _lastBeacon.X - localPosition.X;
        var dy = _lastBeacon.Y - localPosition.Y;
        var dz = _lastBeacon.Z - localPosition.Z;
        var horizontalDistance = Math.Sqrt((dx * dx) + (dz * dz));
        var bearingDegrees = NormalizeDegrees(Math.Atan2(dx, dz) * 180.0 / Math.PI);

        _lastLeaderHorizontalDistance = horizontalDistance;
        _lastLeaderRelativeBearing = NormalizeSignedDegrees(bearingDegrees - localPosition.HeadingY);
        _lastLeaderAgeSeconds = (DateTime.UtcNow - _lastBeacon.TimestampUtc).TotalSeconds;
    }

    private static bool HasForwardInput(PlayerMovementInput input)
    {
        return input.HasFlag(PlayerMovementInput.Forward);
    }

    private static PlayerMovementInput GetTurnInput(PlayerMovementInput input)
    {
        if (input.HasFlag(PlayerMovementInput.TurnLeft))
        {
            return PlayerMovementInput.TurnLeft;
        }

        if (input.HasFlag(PlayerMovementInput.TurnRight))
        {
            return PlayerMovementInput.TurnRight;
        }

        return PlayerMovementInput.None;
    }

    private double GetAssistPulseGapSeconds(PlayerMovementInput input)
    {
        if (HasForwardInput(input))
        {
            return 0.0;
        }

        return Math.Clamp(0.08 / Math.Clamp(_assistPulseScale, 0.25, 2.0), 0.04, 0.18);
    }

    private void ResetAssistPulse()
    {
        _assistPulseInput = PlayerMovementInput.None;
        _assistPulseUntil = DateTime.MinValue;
        _assistNextPulseAt = DateTime.MinValue;
        _lastAssistTurnInput = PlayerMovementInput.None;
    }

    private void ResetAssistSprint()
    {
        _assistSprintActive = false;
        _assistSprintLockedOut = false;
    }

    private void UpdateMovementProbe()
    {
        if (_movementProbeInput == PlayerMovementInput.None)
        {
            return;
        }

        if (_manualInputActive)
        {
            StopMovementProbe("Movement probe stopped: manual input detected.");
            return;
        }

        if (DateTime.UtcNow > _movementProbeUntil)
        {
            StopMovementProbe("Movement probe complete.");
            return;
        }

        if (_localPlayer == null || !_localPlayer.TryApplyMovementInput(_movementProbeInput))
        {
            StopMovementProbe("Movement probe failed: movement input bridge unavailable.");
        }
    }

    private void StopMovementProbe(string reason)
    {
        if (_localPlayer != null)
        {
            _localPlayer.TryApplyMovementInput(PlayerMovementInput.None);
        }

        _movementProbeInput = PlayerMovementInput.None;
        _movementProbeUntil = DateTime.MinValue;
        _movementProbeLine = reason;
        Chat.AddInfoMessage(reason);
        RefreshText();
    }

    private static PlayerMovementInput ParseProbeInput(string input)
    {
        return input.ToLowerInvariant() switch
        {
            "forward" or "w" => PlayerMovementInput.Forward,
            "backward" or "back" or "s" => PlayerMovementInput.Backward,
            "left" or "a" => PlayerMovementInput.Left,
            "right" or "d" => PlayerMovementInput.Right,
            "sprint" or "shift" => PlayerMovementInput.Sprint | PlayerMovementInput.Forward,
            "turnleft" or "turn-left" or "q" => PlayerMovementInput.TurnLeft,
            "turnright" or "turn-right" or "e" => PlayerMovementInput.TurnRight,
            _ => PlayerMovementInput.None
        };
    }

    private string FormatLastManualInput()
    {
        if (_lastManualInputAt == DateTime.MinValue || string.IsNullOrWhiteSpace(_lastManualInputNames))
        {
            return "none";
        }

        var ageSeconds = Math.Max(0, (DateTime.UtcNow - _lastManualInputAt).TotalSeconds);
        return $"{_lastManualInputNames} {ageSeconds:F1}s ago";
    }

    private void RecordManualInputEvent(string state, string keys)
    {
        _manualInputEventCount++;

        try
        {
            Directory.CreateDirectory(_beaconFolder);
            var payload = new Dictionary<string, object?>
            {
                ["timestampUtc"] = DateTime.UtcNow.ToString("O"),
                ["state"] = state,
                ["keys"] = keys,
                ["characterName"] = _localPlayer?.Name,
                ["characterId"] = _localPlayer?.CharacterId,
                ["mode"] = _mode,
                ["eventCount"] = _manualInputEventCount
            };

            File.AppendAllText(_inputLogPath, JsonSerializer.Serialize(payload, JsonLineOptions) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Logger.Error($"Follow Beacon input event log failed: {ex}");
        }
    }

    private string CreateMoveInstruction(double horizontalDistance, double relativeBearing, double ageSeconds)
    {
        if (ageSeconds > _staleWarningSeconds)
        {
            return "Signal stale";
        }

        if (horizontalDistance <= _followDistance)
        {
            return "Hold position";
        }

        var absBearing = Math.Abs(relativeBearing);
        if (absBearing > 135)
        {
            return "Turn around";
        }

        if (absBearing > 70)
        {
            return relativeBearing > 0 ? "Strafe/turn right" : "Strafe/turn left";
        }

        return "Move forward";
    }

    private static string CreateTurnInstruction(double relativeBearing)
    {
        var absBearing = Math.Abs(relativeBearing);
        if (absBearing < 8)
        {
            return "Facing leader";
        }

        return relativeBearing > 0 ? $"Turn right {absBearing:F0} deg" : $"Turn left {absBearing:F0} deg";
    }

    private static double Lerp(double from, double to, double alpha)
    {
        return from + ((to - from) * alpha);
    }

    private static double NormalizeDegrees(double degrees)
    {
        degrees %= 360.0;
        return degrees < 0 ? degrees + 360.0 : degrees;
    }

    private static double NormalizeSignedDegrees(double degrees)
    {
        degrees = NormalizeDegrees(degrees);
        return degrees > 180.0 ? degrees - 360.0 : degrees;
    }

    private bool TryEnsureControlsWindow(bool notifyChat = false)
    {
        if (_controlsWindow != null)
        {
            try
            {
                _controlsWindow.Enable(true);
                _leaderButton?.Enable(true);
                _followerButton?.Enable(true);
                _assistButton?.Enable(true);
                RefreshControlButtons();
                return true;
            }
            catch (Exception ex)
            {
                _controlsWindow = null;
                _leaderButton = null;
                _followerButton = null;
                _assistButton = null;
                _nextControlsWindowAttemptAt = DateTime.UtcNow.AddSeconds(1);
                if (!_reportedControlsWindowFailure)
                {
                    _reportedControlsWindowFailure = true;
                    Logger.Error($"Follow Beacon controls became unavailable: {ex}");
                }
            }
        }

        if (!notifyChat && DateTime.UtcNow < _nextControlsWindowAttemptAt)
        {
            return false;
        }

        try
        {
            _controlsWindow = CustomUI.CreateWindow("Follow Controls", 290, 175);
            _controlsWindow.AddResizeHandle(520, 260, 250, 130);

            _leaderButton = _controlsWindow.AddButtonComponent("Leader", () => SetMode(1));
            _leaderButton.SetSize(220, 32);
            _leaderButton.SetPosition(0, 32);

            _followerButton = _controlsWindow.AddButtonComponent("Follower", () => SetMode(2));
            _followerButton.SetSize(220, 32);
            _followerButton.SetPosition(0, -4);

            _assistButton = _controlsWindow.AddButtonComponent("Auto-Follow", ToggleAssistFromButton);
            _assistButton.SetSize(220, 32);
            _assistButton.SetPosition(0, -40);

            _controlsWindow.Enable(true);
            RefreshControlButtons();
            return true;
        }
        catch (NullReferenceException ex)
        {
            _nextControlsWindowAttemptAt = DateTime.UtcNow.AddSeconds(1);
            if (notifyChat)
            {
                SafeAddInfoMessage($"Follow Beacon controls are not ready yet: {ex.GetType().Name}. Retrying shortly.");
            }

            return false;
        }
        catch (Exception ex)
        {
            _nextControlsWindowAttemptAt = DateTime.UtcNow.AddSeconds(1);
            if (notifyChat)
            {
                SafeAddInfoMessage($"Follow Beacon could not create its controls: {ex.GetType().Name}: {ex.Message}");
            }

            if (!_reportedControlsWindowFailure)
            {
                _reportedControlsWindowFailure = true;
                Logger.Error($"Follow Beacon controls creation failed: {ex}");
            }

            return false;
        }
    }

    private void ToggleAssistFromButton()
    {
        if (_assistEnabled)
        {
            SetAssistEnabled(false, true);
            return;
        }

        if (_mode != FollowerMode)
        {
            SetMode(2);
        }

        SetAssistEnabled(true, true);
    }

    private void RefreshControlButtons()
    {
        _leaderButton?.SetText(_mode == LeaderMode ? "Leader: ON" : "Leader");
        _followerButton?.SetText(_mode == FollowerMode ? "Follower: ON" : "Follower");
        _assistButton?.SetText(_assistEnabled ? "Auto-Follow: ON" : "Auto-Follow");
    }

    private void SafeAddInfoMessage(string message)
    {
        try
        {
            Chat.AddInfoMessage(message);
        }
        catch (NullReferenceException)
        {
        }
    }

    private bool TryEnsureWindow(bool notifyChat = false)
    {
        if (_window != null)
        {
            try
            {
                _window.Enable(true);
                _text?.Enable(true);
                return true;
            }
            catch (Exception ex)
            {
                _window = null;
                _text = null;
                _nextWindowAttemptAt = DateTime.UtcNow.AddSeconds(1);
                if (!_reportedWindowFailure)
                {
                    _reportedWindowFailure = true;
                    Logger.Error($"Follow Beacon window became unavailable: {ex}");
                }
            }
        }

        if (!notifyChat && DateTime.UtcNow < _nextWindowAttemptAt)
        {
            return false;
        }

        try
        {
            _window = CustomUI.CreateWindow("Follow Beacon", 620, 235);
            _text = _window.AddTextComponent("");
            _text.SetSize(600, 215);
            _text.SetFontSize(16);
            _window.Enable(true);
            _text.Enable(true);
            RefreshText();
            return true;
        }
        catch (NullReferenceException ex)
        {
            _nextWindowAttemptAt = DateTime.UtcNow.AddSeconds(1);
            if (notifyChat)
            {
                SafeAddInfoMessage($"Follow Beacon window is not ready yet: {ex.GetType().Name}. Retrying shortly.");
            }

            return false;
        }
        catch (Exception ex)
        {
            _nextWindowAttemptAt = DateTime.UtcNow.AddSeconds(1);
            if (notifyChat)
            {
                SafeAddInfoMessage($"Follow Beacon could not create its window: {ex.GetType().Name}: {ex.Message}");
            }

            if (!_reportedWindowFailure)
            {
                _reportedWindowFailure = true;
                Logger.Error($"Follow Beacon window creation failed: {ex}");
            }

            return false;
        }
    }

    private void RefreshText()
    {
        if (_text == null)
        {
            return;
        }

        var player = _localPlayer == null ? "No local player yet" : $"Local: {_localPlayer.Name}";
        var beacon = _lastBeacon == null ? "No beacon yet" : $"Leader: {_lastBeacon.CharacterName} @ {_lastBeacon.TimestampUtc:HH:mm:ss} UTC";

        if (_debugEnabled)
        {
            var debug = string.IsNullOrWhiteSpace(_debugLine) ? _guidanceLine : _debugLine;
            _text.SetText($"{_primaryLine}{Environment.NewLine}{_detailLine}{Environment.NewLine}{_manualInputLine}{Environment.NewLine}{_assistLine}{Environment.NewLine}{_movementProbeLine}{Environment.NewLine}{player} | Mode: {_mode}{Environment.NewLine}{beacon}{Environment.NewLine}{debug}");
            return;
        }

        _text.SetText($"{_primaryLine}{Environment.NewLine}{_detailLine}{Environment.NewLine}{_manualInputLine}{Environment.NewLine}{_assistLine}{Environment.NewLine}{_movementProbeLine}{Environment.NewLine}{player} | {beacon}");
    }

    private static string DefaultBeaconFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PantheonAddons", "FollowBeacon");

    private string LocalPathConfigPath => Path.Combine(_gameFolder, "Mods", "PantheonAddons", "FollowBeaconConfig.json");

    private void LoadPathConfig()
    {
        var configPath = LocalPathConfigPath;
        if (!File.Exists(configPath))
        {
            return;
        }

        try
        {
            var config = JsonSerializer.Deserialize<PathConfig>(File.ReadAllText(configPath), PathConfigJsonOptions);
            var configuredBeaconPath = ExpandConfiguredPath(config?.BeaconPath);
            var configuredBeaconFolder = ExpandConfiguredPath(config?.BeaconFolder);

            if (!string.IsNullOrWhiteSpace(configuredBeaconPath))
            {
                _beaconPath = configuredBeaconPath;
                _beaconFolder = Path.GetDirectoryName(_beaconPath) ?? DefaultBeaconFolder;
            }
            else if (!string.IsNullOrWhiteSpace(configuredBeaconFolder))
            {
                _beaconFolder = configuredBeaconFolder;
                _beaconPath = Path.Combine(_beaconFolder, "leader-location.json");
            }

            _inputLogPath = Path.Combine(_beaconFolder, "input-events.jsonl");
        }
        catch (Exception ex)
        {
            Logger.Error($"Follow Beacon path config failed: {ex}");
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

    private static string GetLeaderKey(BeaconPayload beacon)
    {
        return $"{beacon.CharacterId}:{beacon.ProcessId}:{beacon.CharacterName}";
    }

    private sealed record PathConfig(string? BeaconFolder, string? BeaconPath);

    private sealed record BeaconPayload(
        int SchemaVersion,
        string CharacterName,
        long CharacterId,
        int ProcessId,
        DateTime TimestampUtc,
        float X,
        float Y,
        float Z,
        float HeadingY);

    private sealed record SmoothedVector(double X, double Y, double Z);
}
