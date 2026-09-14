using Godot;

public partial class Player : ActorBase
{
    [Export] private Node3D _cameraAnchor = null!;
    [Export] private Node3D _meshHolder = null!;

    [Export(PropertyHint.Range, "0.5,20,0.1")] private float _moveSpeed = 5.5f;
    [Export(PropertyHint.Range, "1,120,1")] private float _acceleration = 45.0f;
    [Export(PropertyHint.Range, "1,120,1")] private float _deceleration = 60.0f;
    [Export(PropertyHint.Range, "1,40,0.5")] private float _turnSpeed = 12.0f;
    [Export(PropertyHint.Range, "0,3,0.05")] private float _gravityScale = 1.0f;

    /// <summary>Where the camera rig should sit. Assigned in Player.tscn to SpringArmAnchor.</summary>
    public Node3D CameraAnchor => _cameraAnchor;

    private Node3D _cameraReference;
    private float _gravity = 9.8f;
    private bool _isMovementEnabled = true;

    public override void _Ready()
    {
        base._Ready();

        _gravity = ProjectSettings
            .GetSetting("physics/3d/default_gravity", 9.8f)
            .AsSingle();
    }

    /// <summary>
    /// Sets the node whose horizontal facing movement input is interpreted against.
    /// Game passes the camera rig's yaw pivot; falls back to the active camera when unset.
    /// </summary>
    public void SetCameraReference(Node3D cameraReference)
    {
        _cameraReference = cameraReference;
    }

    /// <summary>
    /// Holds or releases exploration movement. Gravity and grounding keep running while held,
    /// so the Player stays settled instead of drifting.
    /// </summary>
    public void SetMovementEnabled(bool isEnabled)
    {
        _isMovementEnabled = isEnabled;

        if (!isEnabled)
        {
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector3 velocity = Velocity;

        if (IsOnFloor())
        {
            if (velocity.Y < 0.0f)
            {
                velocity.Y = 0.0f;
            }
        }
        else
        {
            velocity.Y -= _gravity * _gravityScale * dt;
        }

        Vector3 desired = GetMoveDirection() * _moveSpeed;
        Vector3 planar = new Vector3(velocity.X, 0.0f, velocity.Z);
        float rate = desired.IsZeroApprox() ? _deceleration : _acceleration;
        planar = planar.MoveToward(desired, rate * dt);

        velocity.X = planar.X;
        velocity.Z = planar.Z;
        Velocity = velocity;

        MoveAndSlide();
        FaceMoveDirection(planar, dt);
    }

    private Vector3 GetMoveDirection()
    {
        if (!_isMovementEnabled)
        {
            return Vector3.Zero;
        }

        Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        if (input == Vector2.Zero)
        {
            return Vector3.Zero;
        }

        Basis reference = GetCameraBasis();
        Vector3 forward = FlattenDirection(-reference.Z, Vector3.Forward);
        Vector3 right = FlattenDirection(reference.X, Vector3.Right);

        return (forward * -input.Y + right * input.X).LimitLength(1.0f);
    }

    private Basis GetCameraBasis()
    {
        if (_cameraReference != null)
        {
            return _cameraReference.GlobalBasis;
        }

        Camera3D camera = GetViewport().GetCamera3D();
        return camera != null ? camera.GlobalBasis : GlobalBasis;
    }

    private static Vector3 FlattenDirection(Vector3 direction, Vector3 fallback)
    {
        direction.Y = 0.0f;
        return direction.LengthSquared() > 0.0001f ? direction.Normalized() : fallback;
    }

    private void FaceMoveDirection(Vector3 planarVelocity, float dt)
    {
        if (_meshHolder == null || planarVelocity.LengthSquared() < 0.04f)
        {
            return;
        }

        float targetYaw = Mathf.Atan2(-planarVelocity.X, -planarVelocity.Z);
        Vector3 rotation = _meshHolder.Rotation;
        rotation.Y = Mathf.LerpAngle(rotation.Y, targetYaw, 1.0f - Mathf.Exp(-_turnSpeed * dt));
        _meshHolder.Rotation = rotation;
    }
}
