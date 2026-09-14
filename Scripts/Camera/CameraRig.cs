using System;
using Godot;

public partial class CameraRig : Node3D
{
    /// <summary>Token returned when a blend could not be started. Never matches a real blend.</summary>
    public const int InvalidBlendToken = 0;

    /// <summary>
    /// What the rig is currently doing. Orbit input and target following are only live in
    /// <see cref="CameraMode.Exploration"/>; the other modes are driven entirely by the blend.
    /// </summary>
    public enum CameraMode
    {
        Exploration,
        BlendingToEncounter,
        Encounter,
        BlendingToExploration,
    }

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

    /// <summary>
    /// Yaw of the encounter view. The camera sits on this heading from the encounter focus and
    /// looks back at it. In the prototype arena the Player approaches from +X and the enemy
    /// waits at -X, so 25 degrees keeps the view mostly across that line - the 28 unit span
    /// reads as screen width - while staying angled enough to be a three-quarter view. Swinging
    /// much further round pushes the near Player off the side of the frame.
    /// </summary>
    [ExportGroup("Encounter View")]
    [Export(PropertyHint.Range, "-180,180,1")] private float _encounterYawDegrees = 25.0f;

    /// <summary>
    /// Pitch of the encounter view. More negative is higher up and looking further down.
    /// Still subject to the min/max pitch clamp above.
    /// </summary>
    [Export(PropertyHint.Range, "-80,-5,1")] private float _encounterPitchDegrees = -35.0f;

    /// <summary>
    /// Spring length of the encounter view. Sized so the Player, the allied spirit and the enemy
    /// all sit inside a 16:9 frame: the enemy placeholder is ~28 units from the Player's approach,
    /// so the view has to sit well back to take in the whole line.
    /// </summary>
    [Export(PropertyHint.Range, "4,40,0.5")] private float _encounterDistance = 18.0f;

    /// <summary>Seconds taken to blend into the encounter view, and back out of it.</summary>
    [Export(PropertyHint.Range, "0.1,5,0.05")] private float _blendDurationSeconds = 1.2f;

    private float _pitch;
    private Node3D _followTarget;

    private CameraMode _mode = CameraMode.Exploration;
    private Node3D _encounterFocus;

    private int _nextBlendToken = InvalidBlendToken + 1;
    private int _blendToken = InvalidBlendToken;
    private float _blendElapsed;
    private Node3D _blendTargetNode;
    private Vector3 _blendStartPosition;
    private float _blendStartYaw;
    private float _blendStartPitch;
    private float _blendStartLength;
    private float _blendTargetYaw;
    private float _blendTargetPitch;
    private float _blendTargetLength;

    private float _savedExplorationYaw;
    private float _savedExplorationPitch;
    private float _savedExplorationLength;

    /// <summary>
    /// Raised with the token of a blend that ran to completion. A blend that was replaced before
    /// finishing never raises this, so a stale completion cannot be acted on.
    /// </summary>
    public event Action<int> BlendCompleted;

    /// <summary>Yaw-only pivot, used as the reference frame for camera-relative movement.</summary>
    public Node3D YawPivot => _yawPivot;

    public Camera3D Camera => _camera;

    public CameraMode Mode => _mode;

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
        AddExcludedBody(excludedBody);
        SnapToTarget();
    }

    /// <summary>
    /// Stops <paramref name="body"/> from shortening the spring arm. Used for actors the rig has
    /// to sweep past or frame closely, which would otherwise pinch the arm mid-blend.
    /// </summary>
    public void AddExcludedBody(CollisionObject3D body)
    {
        if (_springArm != null && body != null)
        {
            _springArm.AddExcludedObject(body.GetRid());
        }
    }

    /// <summary>
    /// Blends from the live exploration view to the elevated encounter view framed on
    /// <paramref name="focus"/>, and parks there. The exploration yaw, pitch and distance are
    /// saved so <see cref="BlendToExplorationView"/> can restore them.
    /// Returns the blend token, or <see cref="InvalidBlendToken"/> if no blend could be started.
    /// </summary>
    public int BlendToEncounterView(Node3D focus)
    {
        if (focus == null)
        {
            GD.PushWarning("CameraRig: no encounter focus supplied; staying in the exploration view.");
            return InvalidBlendToken;
        }

        // Only the first transition out of exploration captures the framing to return to;
        // a re-entrant call must not overwrite it with a mid-encounter pose.
        if (_mode == CameraMode.Exploration)
        {
            _savedExplorationYaw = _yaw;
            _savedExplorationPitch = _pitch;
            _savedExplorationLength = CurrentSpringLength;
        }

        _encounterFocus = focus;

        return StartBlend(
            CameraMode.BlendingToEncounter,
            focus,
            Mathf.DegToRad(_encounterYawDegrees),
            Mathf.DegToRad(_encounterPitchDegrees),
            _encounterDistance);
    }

    /// <summary>
    /// Blends back to player-follow framing, restoring the saved exploration yaw, pitch and
    /// distance around the follow target's current position. Orbit input and following resume
    /// once the blend completes.
    /// </summary>
    public int BlendToExplorationView()
    {
        if (_mode == CameraMode.Exploration)
        {
            return InvalidBlendToken;
        }

        if (_followTarget == null)
        {
            GD.PushWarning("CameraRig: no follow target; cannot blend back to the exploration view.");
            return InvalidBlendToken;
        }

        return StartBlend(
            CameraMode.BlendingToExploration,
            _followTarget,
            _savedExplorationYaw,
            _savedExplorationPitch,
            _savedExplorationLength);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Orbit is exploration-only, so a mouse nudge cannot fight the encounter framing.
        if (_mode != CameraMode.Exploration)
        {
            return;
        }

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

        switch (_mode)
        {
            case CameraMode.Exploration:
                ApplyStickLook(dt);
                FollowTarget(dt);
                break;

            case CameraMode.BlendingToEncounter:
            case CameraMode.BlendingToExploration:
                AdvanceBlend(dt);
                break;

            case CameraMode.Encounter:
                HoldEncounterFraming();
                break;
        }
    }

    private void ApplyStickLook(float dt)
    {
        Vector2 look = Input.GetVector("camera_left", "camera_right", "camera_up", "camera_down");
        if (look == Vector2.Zero)
        {
            return;
        }

        float step = _stickSensitivity * dt;
        AddLook(-look.X * step, -look.Y * step);
    }

    private void AddLook(float yawDegrees, float pitchDegrees)
    {
        _yaw = Mathf.Wrap(_yaw + Mathf.DegToRad(yawDegrees), -Mathf.Pi, Mathf.Pi);
        _pitch += Mathf.DegToRad(pitchDegrees);
        ApplyRotation();
    }

    private int StartBlend(
        CameraMode mode,
        Node3D targetNode,
        float targetYaw,
        float targetPitch,
        float targetLength)
    {
        _mode = mode;
        _blendTargetNode = targetNode;
        _blendStartPosition = GlobalPosition;
        _blendStartYaw = _yaw;
        _blendStartPitch = _pitch;
        _blendStartLength = CurrentSpringLength;
        _blendTargetYaw = targetYaw;
        _blendTargetPitch = targetPitch;
        _blendTargetLength = targetLength;
        _blendElapsed = 0.0f;

        // A fresh token supersedes any blend still in flight, so its completion is never raised.
        _blendToken = _nextBlendToken++;
        if (_nextBlendToken == InvalidBlendToken)
        {
            _nextBlendToken = InvalidBlendToken + 1;
        }

        return _blendToken;
    }

    private void AdvanceBlend(float dt)
    {
        _blendElapsed += dt;

        float progress = _blendDurationSeconds > 0.0f
            ? Mathf.Clamp(_blendElapsed / _blendDurationSeconds, 0.0f, 1.0f)
            : 1.0f;
        float eased = Mathf.SmoothStep(0.0f, 1.0f, progress);

        // Read the destination live: blending home keeps tracking the Player as they move.
        Vector3 targetPosition = _blendTargetNode != null
            ? _blendTargetNode.GlobalPosition
            : _blendStartPosition;

        GlobalPosition = _blendStartPosition.Lerp(targetPosition, eased);
        _yaw = Mathf.LerpAngle(_blendStartYaw, _blendTargetYaw, eased);
        _pitch = Mathf.Lerp(_blendStartPitch, _blendTargetPitch, eased);
        CurrentSpringLength = Mathf.Lerp(_blendStartLength, _blendTargetLength, eased);
        ApplyRotation();

        if (progress < 1.0f)
        {
            return;
        }

        int completedToken = _blendToken;
        _blendToken = InvalidBlendToken;
        _blendTargetNode = null;

        _mode = _mode == CameraMode.BlendingToEncounter
            ? CameraMode.Encounter
            : CameraMode.Exploration;

        if (_mode == CameraMode.Exploration)
        {
            _encounterFocus = null;
        }

        BlendCompleted?.Invoke(completedToken);
    }

    private void HoldEncounterFraming()
    {
        if (_encounterFocus != null)
        {
            GlobalPosition = _encounterFocus.GlobalPosition;
        }
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

    private float CurrentSpringLength
    {
        get => _springArm != null ? _springArm.SpringLength : 0.0f;
        set
        {
            if (_springArm != null)
            {
                _springArm.SpringLength = value;
            }
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
