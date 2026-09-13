using Godot;

public partial class Game : Node3D
{
    [Export] private Player _player = null!;
    [Export] private CameraRig _cameraRig = null!;
    [Export] private Marker3D _playerSpawn = null!;
    [Export] private GameHud _gameHud = null!;
    [Export] private CombatEncounter _combatEncounter = null!;

    public override void _Ready()
    {
        PlacePlayerAtSpawn();
        ConnectCameraAndPlayer();

        // Combat is out of scope for the exploration prototype.
        if (_combatEncounter != null)
        {
            _combatEncounter.ProcessMode = ProcessModeEnum.Disabled;
        }

        _gameHud?.SetStateText("Exploration");
        _gameHud?.SetReadyVisible(false);

        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel"))
        {
            return;
        }

        Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
            ? Input.MouseModeEnum.Visible
            : Input.MouseModeEnum.Captured;

        GetViewport().SetInputAsHandled();
    }

    private void PlacePlayerAtSpawn()
    {
        if (_player == null)
        {
            GD.PushError("Game: _player is not assigned.");
            return;
        }

        if (_playerSpawn == null)
        {
            GD.PushWarning("Game: _playerSpawn is not assigned; leaving Player at its authored position.");
            return;
        }

        _player.GlobalTransform = _playerSpawn.GlobalTransform;
        _player.Velocity = Vector3.Zero;
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
