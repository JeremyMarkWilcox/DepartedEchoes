using Godot;

public partial class CombatEncounter : Node3D
{
    public enum EncounterState
    {
        Exploration,
        Setup,
        Ready,
        Combat,
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
    /// Enters Setup and holds the Player, then hands off to Ready.
    /// Ignored unless the encounter is idle, so re-entry cannot restart an active encounter.
    /// </summary>
    public void BeginEncounter()
    {
        if (State != EncounterState.Exploration)
        {
            return;
        }

        State = EncounterState.Setup;
        SetPlayerMovementEnabled(false);

        EnterReady();
    }

    private void EnterReady()
    {
        State = EncounterState.Ready;
        _gameHud?.SetReadyVisible(true);

        GetTree().CreateTimer(_readyDurationSeconds).Timeout += OnReadyElapsed;
    }

    private void OnReadyElapsed()
    {
        // The encounter may have been ended while the timer was pending.
        if (State != EncounterState.Ready)
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
    /// Returns the world to Exploration: restores the HUD, hides Ready, frees the Player
    /// and makes the battle trigger eligible again.
    /// </summary>
    public void EndEncounter()
    {
        if (State == EncounterState.Exploration)
        {
            return;
        }

        State = EncounterState.Exploration;
        _gameHud?.SetReadyVisible(false);
        _gameHud?.SetStateText("Exploration");
        SetPlayerMovementEnabled(true);
    }

    private void SetPlayerMovementEnabled(bool isEnabled)
    {
        _player?.SetMovementEnabled(isEnabled);
    }
}
