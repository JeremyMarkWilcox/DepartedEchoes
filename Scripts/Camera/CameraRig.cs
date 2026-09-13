using Godot;

public partial class CameraRig : Node3D
{
    [Export] private Node3D _yawPivot = null!;
    [Export] private Node3D _pitchPivot = null!;
    [Export] private SpringArm3D _springArm = null!;
    [Export] private Camera3D _camera = null!;

    private float _yaw = 0.0f;
    [Export(PropertyHint.Range, "-70,40,1")]
    private float _startingPitchDegrees = -15.0f;

    [Export(PropertyHint.Range, "-89,0,1")] private float _minPitchDegrees = -70.0f;
    [Export(PropertyHint.Range, "0,89,1")] private float _maxPitchDegrees = 40.0f;

    /// <summary>Degrees of rotation per pixel of mouse movement.</summary>
    [Export(PropertyHint.Range, "0.01,1,0.01")] private float _mouseSensitivity = 0.16f;

    /// <summary>Degrees of rotation per second at full right-stick deflection.</summary>
    [Export(PropertyHint.Range, "30,540,5")] private float _stickSensitivity = 180.0f;

    /// <summary>How quickly the rig catches up to the follow target. Higher is snappier.</summary>
    [Export(PropertyHint.Range, "1,40,0.5")] private float _followSpeed = 16.0f;

    private float _pitch;
    private Node3D _followTarget;

    /// <summary>Yaw-only pivot, used as the reference frame for camera-relative movement.</summary>
    public Node3D YawPivot => _yawPivot;

    public Camera3D Camera => _camera;

    public override void _Ready()
    {
        _pitch = Mathf.DegToRad(_startingPitchDegrees);
        ApplyRotation();
        SnapToTarget();
    }

    /// <summary>
    /// Follows <paramref name="anchor"/> and keeps <paramref name="excludedBody"/> from pushing
    /// the spring arm in, so the camera does not collide with the character it is framing.
    /// </summary>
    public void SetTarget(Node3D anchor, CollisionObject3D excludedBody = null)
    {
        _followTarget = anchor;

        if (_springArm != null && excludedBody != null)
        {
            _springArm.AddExcludedObject(excludedBody.GetRid());
        }

        SnapToTarget();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseMotion motion || Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            return;
        }

        AddLook(-motion.Relative.X * _mouseSensitivity, -motion.Relative.Y * _mouseSensitivity);
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        Vector2 look = Input.GetVector("camera_left", "camera_right", "camera_up", "camera_down");
        if (look != Vector2.Zero)
        {
            float step = _stickSensitivity * dt;
            AddLook(-look.X * step, -look.Y * step);
        }

        FollowTarget(dt);
    }

    private void AddLook(float yawDegrees, float pitchDegrees)
    {
        _yaw = Mathf.Wrap(_yaw + Mathf.DegToRad(yawDegrees), -Mathf.Pi, Mathf.Pi);
        _pitch += Mathf.DegToRad(pitchDegrees);
        ApplyRotation();
    }

    private void FollowTarget(float dt)
    {
        if (_followTarget == null)
        {
            return;
        }

        GlobalPosition = GlobalPosition.Lerp(
            _followTarget.GlobalPosition,
            1.0f - Mathf.Exp(-_followSpeed * dt));
    }

    private void SnapToTarget()
    {
        if (_followTarget != null && IsInsideTree())
        {
            GlobalPosition = _followTarget.GlobalPosition;
        }
    }

    private void ApplyRotation()
    {
        _pitch = Mathf.Clamp(
            _pitch,
            Mathf.DegToRad(_minPitchDegrees),
            Mathf.DegToRad(_maxPitchDegrees));

        _yawPivot.Rotation = new Vector3(0.0f, _yaw, 0.0f);
        _pitchPivot.Rotation = new Vector3(_pitch, 0.0f, 0.0f);
    }
}
