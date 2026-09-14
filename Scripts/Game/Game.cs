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

        Input.MouseMode = Input.MouseModeEnum.Captured;
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
    }
}
