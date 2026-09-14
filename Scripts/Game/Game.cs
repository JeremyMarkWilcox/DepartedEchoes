using Godot;

public partial class Game : Node3D
{
    [Export] private Player _player = null!;
    [Export] private CameraRig _cameraRig = null!;
    [Export] private Marker3D _playerSpawn = null!;
    [Export] private SpiritBase _allySpirit = null!;
    [Export] private Marker3D _allySpiritStart = null!;
    [Export] private EnemyBase _enemySpirit = null!;
    [Export] private Marker3D _enemySpiritStart = null!;
    [Export] private GameHud _gameHud = null!;
    [Export] private CombatEncounter _combatEncounter = null!;

    public override void _Ready()
    {
        PlaceActorAtSpawn(_player, _playerSpawn, "Player");
        PlaceActorAtSpawn(_allySpirit, _allySpiritStart, "AllySpirit");
        PlaceActorAtSpawn(_enemySpirit, _enemySpiritStart, "EnemySpirit");
        ConnectCameraAndPlayer();

        _gameHud?.SetStateText("Exploration");
        _gameHud?.SetReadyVisible(false);

        if (_combatEncounter != null)
        {
            _combatEncounter.EncounterReset += OnEncounterReset;
        }

        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _ExitTree()
    {
        if (_combatEncounter != null)
        {
            _combatEncounter.EncounterReset -= OnEncounterReset;
        }
    }

    /// <summary>
    /// The encounter decides when a reset is safe; putting the bodies back is still Game's job.
    /// The Player keeps whatever exploration position they returned to.
    /// </summary>
    private void OnEncounterReset()
    {
        PlaceActorAtSpawn(_allySpirit, _allySpiritStart, "AllySpirit");
        PlaceActorAtSpawn(_enemySpirit, _enemySpiritStart, "EnemySpirit");
    }

    /// <summary>
    /// Game owns mouse capture, since it is a whole-session concern rather than a camera or
    /// encounter one. Escape releases the cursor, a left click recaptures it. Neither is bound
    /// to a gameplay or encounter action, so freeing the mouse cannot trigger an encounter.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel") && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (Input.MouseMode != Input.MouseModeEnum.Captured
            && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Game owns actor placement: the spawn marker drives both position and facing.
    /// </summary>
    private static void PlaceActorAtSpawn(ActorBase actor, Marker3D spawn, string actorName)
    {
        if (actor == null)
        {
            GD.PushError($"Game: {actorName} is not assigned.");
            return;
        }

        if (spawn == null)
        {
            GD.PushWarning($"Game: {actorName} spawn marker is not assigned; leaving it at its authored position.");
            return;
        }

        actor.GlobalTransform = spawn.GlobalTransform;
        actor.Velocity = Vector3.Zero;
    }

    private void ConnectCameraAndPlayer()
    {
        if (_cameraRig == null || _player == null)
        {
            GD.PushWarning("Game: camera rig or player missing; camera follow is disabled.");
            return;
        }

        Node3D anchor = _player.CameraAnchor ?? _player;
        _cameraRig.SetTarget(anchor, _player);
        _player.SetCameraReference(_cameraRig.YawPivot);

        // The encounter view sweeps over the spirits to frame them; without this the spring arm
        // can catch on their bodies and pull the camera in part-way through the blend.
        _cameraRig.AddExcludedBody(_allySpirit);
        _cameraRig.AddExcludedBody(_enemySpirit);
    }
}
