using Godot;

public partial class Player : DamageableActor
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

    [ExportGroup("Dodge")]
    [Export] private float _dodgeSpeed = 12.0f;
    [Export] private float _dodgeDuration = 0.32f;
    [Export] private float _dodgeCooldown = 0.85f;
    [Export] private float _protectionStart = 0.04f;
    [Export] private float _protectionDuration = 0.16f;
    private float _dodgeRemaining, _cooldownRemaining, _hitRemaining;
    private Vector3 _dodgeDirection;
    private MeshInstance3D _playerMesh;
    private StandardMaterial3D _playerMaterial;
    private Vector3 _baselineScale = Vector3.One;
    public bool IsDodging => _dodgeRemaining > 0;

    [ExportGroup("Command Delivery")]
    /// <summary>Upgrade hook: permits walking during delivery, but never commanding during a dodge.</summary>
    [Export] public bool CanCommandWhileMoving { get; set; } = false;
    private bool _isDeliveringCommand;
    public bool IsHeldForCommand => _isDeliveringCommand && !CanCommandWhileMoving;

    /// <summary>Independent of encounter movement locks; releasing a call cannot unlock a transition.</summary>
    public void SetCommandDeliveryActive(bool active)
    {
        _isDeliveringCommand = active;
        if (IsHeldForCommand) Velocity = new Vector3(0, Velocity.Y, 0);
    }
    public override bool IsDamageProtected => IsDodging &&
        _dodgeDuration - _dodgeRemaining >= _protectionStart &&
        _dodgeDuration - _dodgeRemaining < _protectionStart + Mathf.Max(0, _protectionDuration);

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed("dodge", allowEcho: false)) return;
        if (!_isCombatActive || !_isMovementEnabled || _isDeliveringCommand || _isDefeated || IsDodging || _cooldownRemaining > 0 || !IsOnFloor()) return;
        Vector3 direction = GetMoveDirection();
        if (direction.IsZeroApprox()) direction = _meshHolder != null ? -_meshHolder.GlobalBasis.Z : Vector3.Forward;
        direction.Y = 0;
        _dodgeDirection = direction.Normalized();
        _dodgeRemaining = Mathf.Max(0.01f, _dodgeDuration);
        _cooldownRemaining = Mathf.Max(_dodgeRemaining, _dodgeCooldown);
        GetViewport().SetInputAsHandled();
    }
    public void ResetForEncounter()
    {
        StopCombat();
        ResetHealth();
        RefreshPlayerVisuals();
    }
    public override void StopCombat()
    {
        base.StopCombat();
        SetCommandDeliveryActive(false);
        _dodgeRemaining = _cooldownRemaining = _hitRemaining = 0;
        Velocity = new Vector3(0, Velocity.Y, 0);
        RefreshPlayerVisuals();
    }
    protected override void OnDamageReceived() { _hitRemaining = 0.18f; RefreshPlayerVisuals(); }
    private void RefreshPlayerVisuals()
    {
        if (_playerMaterial == null) return;
        _playerMaterial.AlbedoColor = _isDefeated ? new Color(0.2f, 0.2f, 0.2f) :
            _hitRemaining > 0 ? new Color(1, 0.25f, 0.25f) : IsDamageProtected ? new Color(0.2f, 1, 1) : Colors.White;
        if (_meshHolder != null)
        {
            _meshHolder.Scale = IsDodging ? _baselineScale * new Vector3(1.15f, 0.65f, 1.15f) : _baselineScale;
            Vector3 rotation = _meshHolder.Rotation;
            rotation.Z = _isDefeated ? Mathf.DegToRad(75) : 0;
            _meshHolder.Rotation = rotation;
        }
    }

    private Node3D _cameraReference;
    private float _gravity = 9.8f;
    private bool _isMovementEnabled = true;

    public override void _Ready()
    {
        base._Ready();
        _playerMesh = _meshHolder?.GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
        if (_meshHolder != null) _baselineScale = _meshHolder.Scale;
        if (_playerMesh != null)
        {
            _playerMaterial = new StandardMaterial3D { AlbedoColor = Colors.White };
            _playerMesh.MaterialOverride = _playerMaterial;
        }

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
            _dodgeRemaining = 0;
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _cooldownRemaining = Mathf.Max(0, _cooldownRemaining - dt);
        _hitRemaining = Mathf.Max(0, _hitRemaining - dt);
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
        planar = IsDodging ? _dodgeDirection * _dodgeSpeed : planar.MoveToward(desired, rate * dt);
        if (IsHeldForCommand) planar = Vector3.Zero;

        velocity.X = planar.X;
        velocity.Z = planar.Z;
        Velocity = velocity;

        MoveAndSlide();
        FaceMoveDirection(planar, dt);
        _dodgeRemaining = Mathf.Max(0, _dodgeRemaining - dt);
        RefreshPlayerVisuals();
    }

    private Vector3 GetMoveDirection()
    {
        if (!_isMovementEnabled || IsHeldForCommand || _isDefeated)
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
