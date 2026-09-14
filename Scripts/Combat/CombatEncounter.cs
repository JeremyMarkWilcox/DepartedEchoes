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

        /// <summary>Blending back to the exploration view. Movement stays held until it lands.</summary>
        Returning,
    }

    [Export] private CameraRig _cameraRig = null!;
    [Export] private Player _player = null!;
    [Export] private Area3D _combatBoundary = null!;
    [Export] private Marker3D _combatCenter = null!;
    [Export] private Area3D _battleTrigger = null!;
    [Export] private GameHud _gameHud = null!;

    /// <summary>How long the Ready banner is held before Combat begins.</summary>
    [Export(PropertyHint.Range, "0.1,5,0.1")] private float _readyDurationSeconds = 1.5f;

    /// <summary>Current encounter phase. Exploration means a new encounter may be triggered.</summary>
    public EncounterState State { get; private set; } = EncounterState.Exploration;

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
    }

    public override void _ExitTree()
    {
        if (_cameraRig != null)
        {
            _cameraRig.BlendCompleted -= OnCameraBlendCompleted;
        }
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
        // allowEcho: false so holding the key repeats into nothing - one press acts once.
        if (!@event.IsActionPressed(DebugEndEncounterAction, allowEcho: false))
        {
            return;
        }

        GetViewport().SetInputAsHandled();

        // Harmless when no encounter is running, or while one is already returning.
        EndEncounter();
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
        _gameHud?.SetReadyVisible(false);
        _gameHud?.SetStateText("Setup - framing encounter");

        _pendingBlendToken = _cameraRig?.BlendToEncounterView(_combatCenter) ?? CameraRig.InvalidBlendToken;

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

        // Retire the Ready timer and the encounter blend callback before starting the return.
        _encounterGeneration++;
        _pendingBlendToken = CameraRig.InvalidBlendToken;

        State = EncounterState.Returning;
        SetPlayerMovementEnabled(false);
        _gameHud?.SetReadyVisible(false);
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

        _gameHud?.SetReadyVisible(false);
        _gameHud?.SetStateText("Exploration");
        SetPlayerMovementEnabled(true);
    }

    private void SetPlayerMovementEnabled(bool isEnabled)
    {
        _player?.SetMovementEnabled(isEnabled);
    }
}
