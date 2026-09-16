using System;
using Godot;

public partial class CombatEncounter : Node3D
{
    /// <summary>Development-only action that ends an encounter from any active phase.</summary>
    private const string DebugEndEncounterAction = "debug_end_encounter";

    public enum EncounterState
    {
        Exploration,
        Setup,
        Ready,
        Combat,

        /// <summary>The player or enemy has fallen: combat stops and the result is on screen.</summary>
        Resolving,

        /// <summary>Blending back to the exploration view. Movement stays held until it lands.</summary>
        Returning,
    }

    [Export] private CameraRig _cameraRig = null!;
    [Export] private Player _player = null!;
    [Export] private Area3D _combatBoundary = null!;
    [Export] private Marker3D _combatCenter = null!;
    [Export] private Area3D _battleTrigger = null!;
    [Export] private GameHud _gameHud = null!;

    /// <summary>The two explicit opponents. A one-versus-one prototype needs no faction lookup.</summary>
    [Export] private SpiritBase _allySpirit = null!;

    [Export] private EnemyBase _enemySpirit = null!;

    [ExportGroup("Help Me")]
    [Export(PropertyHint.Range, "1,15,0.5")] private float _helpRadius = 6.0f;
    [Export(PropertyHint.Range, "1,30,0.5")] private float _helpCooldownSeconds = 8.0f;
    private float _helpCooldownRemaining, _helpNoticeRemaining;
    private string _helpNotice = "";
    public float HelpCooldownRemaining => _helpCooldownRemaining;
    public bool IsHelpInProgress => _allySpirit?.IsRespondingToHelp == true
        || _enemySpirit?.HasPendingDistraction == true || _enemySpirit?.IsDistracted == true;

    [ExportGroup("Encounter Timing")]
    /// <summary>How long the Ready banner is held before Combat begins.</summary>
    [Export(PropertyHint.Range, "0.1,5,0.1")] private float _readyDurationSeconds = 1.5f;

    /// <summary>How long the result is shown before the return to exploration starts.</summary>
    [Export(PropertyHint.Range, "0.2,5,0.1")] private float _resultDurationSeconds = 1.6f;

    /// <summary>Current encounter phase. Exploration means a new encounter may be triggered.</summary>
    public EncounterState State { get; private set; } = EncounterState.Exploration;

    /// <summary>
    /// Raised once exploration has been restored and combat runtime state has been cleared, so
    /// Game can put the combatants back on their authored markers. Encounter owns the timing;
    /// placement stays Game's responsibility.
    /// </summary>
    public event Action EncounterReset;

    /// <summary>
    /// Counts down the result display. A plain field rather than a scene timer, so cancelling is
    /// just clearing it - no callback can survive to fire into a later encounter.
    /// </summary>
    private float _resultCountdown;

    /// <summary>
    /// The actors the encounter camera keeps in frame, built once from the exported references.
    /// Handing the rig a cached array keeps it from searching the scene tree every frame.
    /// </summary>
    private ActorBase[] _framingTargets = Array.Empty<ActorBase>();

    /// <summary>
    /// Bumped whenever an encounter starts or ends. Deferred callbacks capture the value they
    /// were scheduled under, so a Ready timer left pending by an exit can never drive a later
    /// encounter into Combat.
    /// </summary>
    private int _encounterGeneration;

    /// <summary>The camera blend this encounter is currently waiting on.</summary>
    private int _pendingBlendToken = CameraRig.InvalidBlendToken;

    /// <summary>
    /// Game positions the actors during startup, but the trigger area builds its overlap set
    /// from where those bodies were first registered (the scene origin, which is inside the
    /// trigger). Arming is therefore held back until those placements have settled, otherwise
    /// every actor reports an entry on the first physics step.
    /// </summary>
    private int _physicsFramesUntilArmed = 2;

    public override void _Ready()
    {
        if (_battleTrigger == null)
        {
            GD.PushWarning("CombatEncounter: _battleTrigger is not assigned; encounters cannot be triggered.");
            SetPhysicsProcess(false);
        }

        if (_cameraRig != null)
        {
            _cameraRig.BlendCompleted += OnCameraBlendCompleted;
        }
        else
        {
            GD.PushWarning("CombatEncounter: _cameraRig is not assigned; encounters run without a camera blend.");
        }

        SubscribeToCombatant(_allySpirit);
        SubscribeToCombatant(_enemySpirit);
        SubscribeToCombatant(_player);

        // Player first purely for readability; the rig is told which one takes priority separately.
        _framingTargets = new ActorBase[] { _player, _allySpirit, _enemySpirit };

        _gameHud?.SetCombatHudVisible(false);
        _gameHud?.HideResult();
    }

    public override void _ExitTree()
    {
        if (_cameraRig != null)
        {
            _cameraRig.BlendCompleted -= OnCameraBlendCompleted;
        }

        UnsubscribeFromCombatant(_allySpirit);
        UnsubscribeFromCombatant(_enemySpirit);
        UnsubscribeFromCombatant(_player);
    }

    private void SubscribeToCombatant(DamageableActor combatant)
    {
        if (combatant == null)
        {
            GD.PushWarning("CombatEncounter: a combatant is not assigned; the fight will not run.");
            return;
        }

        combatant.Defeated += OnCombatantDefeated;
        combatant.HealthChanged += OnCombatantHealthChanged;
    }

    private void UnsubscribeFromCombatant(DamageableActor combatant)
    {
        if (combatant == null)
        {
            return;
        }

        combatant.Defeated -= OnCombatantDefeated;
        combatant.HealthChanged -= OnCombatantHealthChanged;
    }

    /// <summary>Runs down the result display, then hands off to the existing return path.</summary>
    public override void _Process(double delta)
    {
        if (State == EncounterState.Combat)
        {
            _helpCooldownRemaining = Mathf.Max(0, _helpCooldownRemaining - (float)delta);
            _helpNoticeRemaining = Mathf.Max(0, _helpNoticeRemaining - (float)delta);
            string response = _enemySpirit.IsDistracted ? "Enemy targeting ally"
                : _enemySpirit.HasPendingDistraction ? "Distraction earned - waiting for attack to finish"
                : _allySpirit.HelpFeedback;
            if (_helpNoticeRemaining > 0) response = _helpNotice;
            bool allyDown = _allySpirit.IsDefeated;
            if (allyDown) response = "Ally down - keep moving!";
            string availability = allyDown ? "Unavailable"
                : _helpCooldownRemaining > 0 ? $"{Mathf.CeilToInt(_helpCooldownRemaining)}s"
                : IsHelpInProgress ? "In progress" : "Ready";
            _gameHud?.SetHelpStatus($"Help me [Q / Y]: {availability}\n{response}");
        }
        else
        {
            _gameHud?.SetHelpStatus("");
        }

        if (State != EncounterState.Resolving || _resultCountdown <= 0.0f)
        {
            return;
        }

        _resultCountdown -= (float)delta;

        if (_resultCountdown > 0.0f)
        {
            return;
        }

        _resultCountdown = 0.0f;
        EndEncounter();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_physicsFramesUntilArmed > 0)
        {
            _physicsFramesUntilArmed--;
            return;
        }

        _battleTrigger.BodyEntered += OnBattleTriggerBodyEntered;
        SetPhysicsProcess(false);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("command_help", allowEcho: false))
        {
            GetViewport().SetInputAsHandled();
            TryRequestHelp();
            return;
        }

        // allowEcho: false so holding the key repeats into nothing - one press acts once.
        if (!@event.IsActionPressed(DebugEndEncounterAction, allowEcho: false))
        {
            return;
        }

        GetViewport().SetInputAsHandled();

        // Harmless when no encounter is running, or while one is already returning.
        EndEncounter();
    }

    /// <summary>Only the initial request checks player proximity; retreat remains allowed.</summary>
    public bool TryRequestHelp()
    {
        if (State != EncounterState.Combat || !_player.IsCombatActive || _player.IsDefeated) return false;
        if (_allySpirit.IsDefeated || !_allySpirit.IsCombatActive)
        {
            ShowHelpNotice("Ally unavailable");
            return false;
        }
        if (IsHelpInProgress)
        {
            ShowHelpNotice("Already helping");
            return false;
        }
        if (_helpCooldownRemaining > 0)
        {
            ShowHelpNotice("Help is recharging");
            return false;
        }
        if (!_enemySpirit.IsCombatActive || _enemySpirit.IsDefeated
            || _player.GlobalPosition.DistanceSquaredTo(_enemySpirit.GlobalPosition) > _helpRadius * _helpRadius)
        {
            ShowHelpNotice("No nearby threat");
            return false;
        }
        if (!_allySpirit.TryAcceptHelp(_enemySpirit))
        {
            ShowHelpNotice("Ally unavailable");
            return false;
        }
        _helpCooldownRemaining = Mathf.Max(0.1f, _helpCooldownSeconds);
        ShowHelpNotice("Help requested - ally responding");
        return true;
    }

    private void ShowHelpNotice(string text)
    {
        _helpNotice = text;
        _helpNoticeRemaining = 1.2f;
    }

    private void ClearHelpCommand()
    {
        _helpCooldownRemaining = _helpNoticeRemaining = 0;
        _helpNotice = "";
        _gameHud?.SetHelpStatus("");
    }

    private void OnBattleTriggerBodyEntered(Node3D body)
    {
        // Only the Player starts an encounter; allies, enemies and scenery are ignored.
        if (body is not Player)
        {
            return;
        }

        BeginEncounter();
    }

    /// <summary>
    /// Enters Setup, holds the Player and starts the camera blend into the encounter view.
    /// Ready is deferred until that blend reports completion.
    /// Ignored unless the encounter is idle, so re-entry cannot restart an active encounter.
    /// </summary>
    public void BeginEncounter()
    {
        if (State != EncounterState.Exploration)
        {
            return;
        }

        _encounterGeneration++;
        State = EncounterState.Setup;

        // Gravity and grounding keep running while movement is held.
        SetPlayerMovementEnabled(false);

        // Participants are armed with their opponent and clean runtime state, but StartCombat is
        // withheld until Combat, so nothing moves or swings during Setup or Ready.
        ResetCombatants();

        _gameHud?.SetReadyVisible(false);
        _gameHud?.HideResult();
        _gameHud?.SetCombatHudVisible(true);
        _gameHud?.SetStateText("Setup - framing encounter");

        // CombatCenter stays the fallback focus for when no actor reference is usable.
        _pendingBlendToken = _cameraRig?.BlendToEncounterView(_combatCenter, _framingTargets, _player)
                             ?? CameraRig.InvalidBlendToken;

        if (_pendingBlendToken == CameraRig.InvalidBlendToken)
        {
            // No blend to wait on (missing rig or focus); keep the loop playable anyway.
            EnterReady();
        }
    }

    private void OnCameraBlendCompleted(int blendToken)
    {
        if (blendToken == CameraRig.InvalidBlendToken || blendToken != _pendingBlendToken)
        {
            return;
        }

        _pendingBlendToken = CameraRig.InvalidBlendToken;

        switch (State)
        {
            case EncounterState.Setup:
                EnterReady();
                break;

            case EncounterState.Returning:
                FinishReturnToExploration();
                break;
        }
    }

    private void EnterReady()
    {
        State = EncounterState.Ready;
        _gameHud?.SetReadyVisible(true);
        _gameHud?.SetStateText("Ready");

        int generation = _encounterGeneration;
        GetTree().CreateTimer(_readyDurationSeconds).Timeout += () => OnReadyElapsed(generation);
    }

    private void OnReadyElapsed(int generation)
    {
        // The encounter may have been ended, or replaced by a later one, while the timer was pending.
        if (generation != _encounterGeneration || State != EncounterState.Ready)
        {
            return;
        }

        State = EncounterState.Combat;
        _gameHud?.SetReadyVisible(false);
        _gameHud?.SetStateText("Combat");

        // The Player must be able to move and dodge during combat.
        SetPlayerMovementEnabled(true);

        // Only now may the combatants approach and attack.
        _player?.StartCombat();
        _allySpirit?.StartCombat();
        _enemySpirit?.StartCombat();
    }

    /// <summary>
    /// Ally defeat leaves the player fighting. Only player or enemy defeat settles the encounter;
    /// the first terminal outcome stops all participants and cannot be overwritten.
    /// </summary>
    private void OnCombatantDefeated(DamageableActor combatant)
    {
        if (State != EncounterState.Combat)
        {
            return;
        }

        if (combatant == _allySpirit)
        {
            // DamageableActor already stopped the ally and cancelled its help response.
            // The enemy completes its committed attack before reacquiring the player.
            ShowHelpNotice("Ally down - keep moving!");
            return;
        }

        StopCombatants();
        State = EncounterState.Resolving;
        SetPlayerMovementEnabled(false);

        // Attributed to the combatant that actually fell, rather than inferred from both flags.
        bool enemyFell = combatant == _enemySpirit;

        _gameHud?.SetStateText("Resolving");
        _gameHud?.ShowResult(enemyFell ? "Enemy defeated" : "Player defeated");

        _resultCountdown = _resultDurationSeconds;
    }

    private void OnCombatantHealthChanged(DamageableActor combatant)
    {
        if (combatant == _player)
        {
            _gameHud?.SetPlayerHealth(combatant.Health, combatant.MaxHealth);
        }
        else if (combatant == _allySpirit)
        {
            _gameHud?.SetAllyHealth(combatant.Health, combatant.MaxHealth);
        }
        else if (combatant == _enemySpirit)
        {
            _gameHud?.SetEnemyHealth(combatant.Health, combatant.MaxHealth);
        }
    }

    /// <summary>Clears runtime combat state on both sides and re-arms their opponents.</summary>
    private void ResetCombatants()
    {
        ClearHelpCommand();
        _player?.ResetForEncounter();
        _allySpirit?.ResetForEncounter(_enemySpirit);
        _enemySpirit?.ResetForEncounter(_player);
    }

    /// <summary>
    /// The single stop path: defeat, the debug exit and any other cancellation all come through
    /// here, dropping movement and any attack still winding up.
    /// </summary>
    private void StopCombatants()
    {
        ClearHelpCommand();
        _player?.StopCombat();
        _allySpirit?.StopCombat();
        _enemySpirit?.StopCombat();
    }

    /// <summary>
    /// Starts the return to Exploration from Setup, Ready or Combat: invalidates anything still
    /// pending, holds the Player for the return blend and hides Ready. Exploration is only
    /// restored once the camera is back on the Player. Repeated calls are no-ops.
    /// </summary>
    public void EndEncounter()
    {
        if (State == EncounterState.Exploration || State == EncounterState.Returning)
        {
            return;
        }

        // Retire the Ready timer, the blend callback and the result countdown before returning.
        _encounterGeneration++;
        _pendingBlendToken = CameraRig.InvalidBlendToken;
        _resultCountdown = 0.0f;

        // Safe from Setup, Ready, an attack windup or the result display alike.
        StopCombatants();

        State = EncounterState.Returning;
        SetPlayerMovementEnabled(false);
        _gameHud?.SetReadyVisible(false);
        _gameHud?.HideResult();
        _gameHud?.SetStateText("Returning to exploration");

        _pendingBlendToken = _cameraRig?.BlendToExplorationView() ?? CameraRig.InvalidBlendToken;

        if (_pendingBlendToken == CameraRig.InvalidBlendToken)
        {
            FinishReturnToExploration();
        }
    }

    /// <summary>
    /// Completes the return: the camera is following the Player again, so orbit input (restored
    /// by the rig) and movement are handed back and the Exploration HUD is shown. A fresh trigger
    /// entry is required for the next encounter, so standing in the trigger will not restart one.
    /// </summary>
    private void FinishReturnToExploration()
    {
        State = EncounterState.Exploration;
        _pendingBlendToken = CameraRig.InvalidBlendToken;
        _resultCountdown = 0.0f;

        _gameHud?.SetReadyVisible(false);
        _gameHud?.HideResult();
        _gameHud?.SetCombatHudVisible(false);
        _gameHud?.SetStateText("Exploration");
        SetPlayerMovementEnabled(true);

        // Reset only once the camera is off the arena, so no combatant is seen healing or
        // snapping back mid-fight. Runtime state is ours; Game re-places the bodies.
        ResetCombatants();
        EncounterReset?.Invoke();
    }

    private void SetPlayerMovementEnabled(bool isEnabled)
    {
        _player?.SetMovementEnabled(isEnabled);
    }
}
