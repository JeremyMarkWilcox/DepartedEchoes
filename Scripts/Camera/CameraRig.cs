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
    /// Yaw of the encounter view. The camera sits on this heading from the framing point and looks
    /// back at it. In the prototype arena the Player approaches from +X and the enemy waits at -X,
    /// so 25 degrees keeps the view mostly across that line while staying angled enough to read as
    /// a three-quarter view. Fixed for the whole encounter: framing moves the point, never the angle.
    /// </summary>
    [ExportGroup("Encounter View")]
    [Export(PropertyHint.Range, "-180,180,1")] private float _encounterYawDegrees = 25.0f;

    /// <summary>
    /// Pitch of the encounter view. More negative is higher up and looking further down.
    /// Still subject to the min/max pitch clamp above, and likewise fixed for the encounter.
    /// </summary>
    [Export(PropertyHint.Range, "-80,-5,1")] private float _encounterPitchDegrees = -35.0f;

    /// <summary>Seconds taken to blend into the encounter view, and back out of it.</summary>
    [Export(PropertyHint.Range, "0.1,5,0.05")] private float _blendDurationSeconds = 1.2f;

    /// <summary>
    /// Closest the encounter camera will pull in, however tightly the group is packed. Keeps the
    /// established combat perspective when the three actors are bunched together.
    /// </summary>
    [ExportGroup("Encounter Framing")]
    [Export(PropertyHint.Range, "4,40,0.5")] private float _minEncounterDistance = 12.0f;

    /// <summary>Furthest the encounter camera will pull back before the Player takes priority.</summary>
    [Export(PropertyHint.Range, "4,60,0.5")] private float _maxEncounterDistance = 26.0f;

    /// <summary>
    /// Fraction of the half-frame the group must fit inside. The remainder is left as margin for
    /// actor bodies, attack cues and the HUD, so nothing is framed hard against an edge.
    /// </summary>
    [Export(PropertyHint.Range, "0.3,1,0.01")] private float _framingSafeAreaFraction = 0.78f;

    /// <summary>
    /// Half-size of the screen-space dead zone, as a fraction of the half-frame. While the group
    /// stays inside this box the framing point does not move at all, so footsteps and trading
    /// blows do not drag the camera around.
    /// </summary>
    [Export(PropertyHint.Range, "0.05,0.95,0.01")] private float _framingDeadZoneFraction = 0.45f;

    /// <summary>How quickly the framing point eases toward its target. Higher is more responsive.</summary>
    [Export(PropertyHint.Range, "0.5,20,0.1")] private float _framingResponseSpeed = 3.0f;

    /// <summary>Zoom-out response. Kept brisk, because losing sight of an actor is the worse failure.</summary>
    [Export(PropertyHint.Range, "0.2,20,0.1")] private float _zoomOutResponseSpeed = 4.0f;

    /// <summary>
    /// Zoom-in response. Deliberately slower than zooming out, so the camera settles back gently
    /// instead of pumping in and out while the combatants trade blows.
    /// </summary>
    [Export(PropertyHint.Range, "0.1,20,0.1")] private float _zoomInResponseSpeed = 1.2f;

    /// <summary>
    /// How much required distance beyond the maximum hands framing priority fully to the Player.
    /// Spreads the changeover over a range instead of snapping at the limit.
    /// </summary>
    [Export(PropertyHint.Range, "1,40,0.5")] private float _playerPriorityFalloff = 8.0f;

    private float _pitch;
    private Node3D _followTarget;

    private CameraMode _mode = CameraMode.Exploration;

    // Encounter framing state. Cleared when the return blend completes.
    private ActorBase[] _framingTargets = Array.Empty<ActorBase>();
    private ActorBase _priorityTarget;
    private Node3D _framingFallback;
    private Vector3 _framingPoint;
    private float _framingDistance;
    private bool _hasFraming;

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

    /// <summary>World point the encounter camera is aiming at. Exposed for inspection.</summary>
    public Vector3 EncounterFramingPoint => _framingPoint;

    /// <summary>Spring length the encounter framing is asking for. Exposed for inspection.</summary>
    public float EncounterFramingDistance => _framingDistance;

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
    /// Blends from the live exploration view to the elevated encounter view, then tracks the group.
    ///
    /// <paramref name="framingTargets"/> are the actors to keep on screen, <paramref name="priorityTarget"/>
    /// is the one that keeps its place in frame once the group no longer fits, and
    /// <paramref name="fallbackFocus"/> is used if no target is valid. The exploration yaw, pitch
    /// and distance are saved so <see cref="BlendToExplorationView"/> can restore them.
    /// Returns the blend token, or <see cref="InvalidBlendToken"/> if nothing could be framed.
    /// </summary>
    public int BlendToEncounterView(
        Node3D fallbackFocus,
        ActorBase[] framingTargets,
        ActorBase priorityTarget)
    {
        _framingTargets = framingTargets ?? Array.Empty<ActorBase>();
        _priorityTarget = priorityTarget;
        _framingFallback = fallbackFocus;

        if (!HasAnyFramingSource())
        {
            GD.PushWarning("CameraRig: no valid framing targets or fallback; staying in exploration.");
            ClearEncounterFraming();
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

        // Establish a valid group pose immediately, so the blend has somewhere real to aim from
        // its very first frame and tracking can take over at the end without a snap.
        _hasFraming = false;
        UpdateEncounterFraming(0.0f, snap: true);

        return StartBlend(
            CameraMode.BlendingToEncounter,
            null,
            Mathf.DegToRad(_encounterYawDegrees),
            Mathf.DegToRad(_encounterPitchDegrees),
            _framingDistance);
    }

    /// <summary>
    /// Blends back to player-follow framing, restoring the saved exploration yaw, pitch and
    /// distance around the follow target's current position. Encounter framing stops updating
    /// straight away, so it cannot drag the return blend around.
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
                UpdateEncounterFraming(dt, snap: false);
                GlobalPosition = _framingPoint;
                CurrentSpringLength = _framingDistance;
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

        Vector3 targetPosition;
        float targetLength;

        if (_mode == CameraMode.BlendingToEncounter)
        {
            // Keep the group framing live through the blend: at progress 1 the rig is already
            // exactly where tracking wants it, so the handover cannot snap.
            UpdateEncounterFraming(dt, snap: false);
            targetPosition = _framingPoint;
            targetLength = _framingDistance;
        }
        else
        {
            // Read the destination live: blending home keeps tracking the Player as they move.
            targetPosition = _blendTargetNode != null
                ? _blendTargetNode.GlobalPosition
                : _blendStartPosition;
            targetLength = _blendTargetLength;
        }

        GlobalPosition = _blendStartPosition.Lerp(targetPosition, eased);
        _yaw = Mathf.LerpAngle(_blendStartYaw, _blendTargetYaw, eased);
        _pitch = Mathf.Lerp(_blendStartPitch, _blendTargetPitch, eased);
        CurrentSpringLength = Mathf.Lerp(_blendStartLength, targetLength, eased);
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
            ClearEncounterFraming();
        }

        BlendCompleted?.Invoke(completedToken);
    }

    /// <summary>Drops every encounter reference so the next encounter starts from nothing stale.</summary>
    private void ClearEncounterFraming()
    {
        _framingTargets = Array.Empty<ActorBase>();
        _priorityTarget = null;
        _framingFallback = null;
        _hasFraming = false;
    }

    /// <summary>
    /// Recomputes where the encounter camera should aim and how far back it should sit.
    ///
    /// Framing comes from the group's spatial extent, not an average of positions: every target
    /// contributes the eight corners of its collision box, so both height and horizontal spread
    /// are accounted for. Those bounds never animate, which is what keeps attack cues, hit
    /// flashes and defeat poses from shaking the camera.
    ///
    /// The point only moves once the group pushes past the dead zone, and the distance only grows
    /// to whatever keeps every corner inside the safe area, clamped to the configured limits.
    /// </summary>
    private void UpdateEncounterFraming(float dt, bool snap)
    {
        Basis basis = EncounterBasis();
        Vector3 right = basis.X;
        Vector3 up = basis.Y;
        Vector3 forward = -basis.Z;

        GetFrameTangents(out float tanHorizontal, out float tanVertical);

        if (!_hasFraming)
        {
            _framingPoint = FallbackAnchor();
            _framingDistance = Mathf.Clamp(_minEncounterDistance, 0.1f, _maxEncounterDistance);
            _hasFraming = true;
            snap = true;
        }

        // Pass one: where does the group sit relative to the point currently being aimed at?
        var group = new FrameExtents();
        int framed = 0;

        foreach (ActorBase target in _framingTargets)
        {
            if (AccumulateActor(target, _framingPoint, right, up, forward, ref group))
            {
                framed++;
            }
        }

        if (framed == 0)
        {
            // Nothing valid left to frame: sit on the fallback focus and hold a sane distance.
            Vector3 anchor = FallbackAnchor();
            _framingPoint = snap ? anchor : _framingPoint.Lerp(anchor, Response(_framingResponseSpeed, dt));
            _framingDistance = ApproachDistance(
                _framingDistance,
                Mathf.Clamp(_minEncounterDistance, 0.1f, _maxEncounterDistance),
                dt,
                snap);
            return;
        }

        // Pass two: shift the point only as far as the dead zone demands, then solve the distance
        // that actually fits the group about that new point.
        Vector3 groupPoint = ShiftForExtents(
            group, _framingPoint, right, up, forward, tanHorizontal, tanVertical, _framingDistance, snap);

        float requiredDistance = RequiredDistance(
            groupPoint, right, up, forward, tanHorizontal, tanVertical);

        // Maximum-distance policy: as the group stops fitting, hand framing over to the Player and
        // let the distant combatants slide out of frame rather than zooming out without end.
        Vector3 desiredPoint = groupPoint;
        float overflow = PlayerPriorityWeight(requiredDistance);

        if (overflow > 0.0f && _priorityTarget != null)
        {
            var solo = new FrameExtents();
            if (AccumulateActor(_priorityTarget, _framingPoint, right, up, forward, ref solo))
            {
                Vector3 playerPoint = ShiftForExtents(
                    solo, _framingPoint, right, up, forward, tanHorizontal, tanVertical, _framingDistance, snap);
                desiredPoint = groupPoint.Lerp(playerPoint, overflow);
            }
        }

        float desiredDistance = Mathf.Clamp(requiredDistance, _minEncounterDistance, _maxEncounterDistance);

        _framingPoint = snap
            ? desiredPoint
            : _framingPoint.Lerp(desiredPoint, Response(_framingResponseSpeed, dt));
        _framingDistance = ApproachDistance(_framingDistance, desiredDistance, dt, snap);
    }

    private static bool IsUsable(ActorBase target)
    {
        return target != null && GodotObject.IsInstanceValid(target) && target.IsInsideTree();
    }

    /// <summary>
    /// How much the Player should override group framing: zero while the group still fits, ramping
    /// to one as the required distance runs past the maximum, so the changeover is gradual.
    /// </summary>
    private float PlayerPriorityWeight(float requiredDistance)
    {
        float excess = requiredDistance - _maxEncounterDistance;

        if (excess <= 0.0f)
        {
            return 0.0f;
        }

        return _playerPriorityFalloff > 0.0f
            ? Mathf.Clamp(excess / _playerPriorityFalloff, 0.0f, 1.0f)
            : 1.0f;
    }

    /// <summary>
    /// Fixed encounter orientation, composed the same way the node hierarchy does it: yaw about
    /// world up, then pitch about the yawed right axis.
    /// </summary>
    private Basis EncounterBasis()
    {
        float pitch = Mathf.Clamp(
            Mathf.DegToRad(_encounterPitchDegrees),
            Mathf.DegToRad(_minPitchDegrees),
            Mathf.DegToRad(_maxPitchDegrees));

        return new Basis(Vector3.Up, Mathf.DegToRad(_encounterYawDegrees))
               * new Basis(Vector3.Right, pitch);
    }

    /// <summary>
    /// Half-frame tangents from the live viewport and the camera's own projection, so a narrow or
    /// tall window widens the required distance instead of silently cropping actors.
    /// </summary>
    private void GetFrameTangents(out float tanHorizontal, out float tanVertical)
    {
        float fov = _camera?.Fov ?? 75.0f;
        float tanHalf = Mathf.Tan(Mathf.DegToRad(fov) * 0.5f);

        Vector2 size = GetViewport().GetVisibleRect().Size;
        float aspect = size.Y > 0.0f ? size.X / size.Y : 16.0f / 9.0f;

        if (_camera != null && _camera.KeepAspect == Camera3D.KeepAspectEnum.Width)
        {
            tanHorizontal = tanHalf;
            tanVertical = aspect > 0.0f ? tanHalf / aspect : tanHalf;
        }
        else
        {
            tanVertical = tanHalf;
            tanHorizontal = tanHalf * aspect;
        }
    }

    /// <summary>
    /// Projects the eight corners of an actor's collision box onto the camera plane. Returns false
    /// for a missing, freed or detached actor, so callers can fall back to whatever is left.
    /// Defeated actors are deliberately still framed, so the outcome stays on screen.
    /// </summary>
    private static bool AccumulateActor(
        ActorBase actor,
        Vector3 origin,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        ref FrameExtents extents)
    {
        if (!IsUsable(actor))
        {
            return false;
        }

        Vector3 centre = actor.GlobalPosition;
        float radius = actor.CollisionRadius;
        float halfHeight = actor.CollisionHalfHeight;

        // One depth per actor rather than per corner. Corner depths differ by only a body's width,
        // but which corner is the lateral extreme flips as an actor shuffles, and a limit derived
        // from it would flip with it - a discontinuity the smoothing would render as jitter.
        float depthOffset = (centre - origin).Dot(forward);

        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 offset = centre - origin + new Vector3(
                (corner & 1) == 0 ? -radius : radius,
                (corner & 2) == 0 ? -halfHeight : halfHeight,
                (corner & 4) == 0 ? -radius : radius);

            extents.Add(offset.Dot(right), offset.Dot(up), depthOffset);
        }

        return true;
    }

    /// <summary>
    /// Moves the framing point only by however much the group overruns the dead zone. The dead
    /// zone is sized at the working distance rather than per corner depth - it is a comfort
    /// threshold, not a containment guarantee, and <see cref="RequiredDistance"/> does the exact
    /// per-corner work.
    /// </summary>
    private Vector3 ShiftForExtents(
        FrameExtents extents,
        Vector3 point,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        float tanHorizontal,
        float tanVertical,
        float distance,
        bool centreExactly)
    {
        float deadZoneRight = tanHorizontal * distance * _framingDeadZoneFraction;
        float deadZoneUp = tanVertical * distance * _framingDeadZoneFraction;

        float shiftRight = centreExactly
            ? (extents.RightMin + extents.RightMax) * 0.5f
            : DeadZoneShift(extents.RightMin, extents.RightMax, deadZoneRight);

        float shiftUp = centreExactly
            ? (extents.UpMin + extents.UpMax) * 0.5f
            : DeadZoneShift(extents.UpMin, extents.UpMax, deadZoneUp);

        // Depth is centred outright rather than dead-zoned. Sliding along the view axis barely
        // changes where anything sits on screen, so there is no jitter to guard against, and
        // keeping the group centred in depth is what stops a target that walks in toward the
        // camera from ending up beside or behind the lens once the distance has clamped at its
        // maximum. It also keeps every target near the pivot depth, which is what lets the dead
        // zone above be sized from the camera distance alone.
        float shiftForward = (extents.DepthMin + extents.DepthMax) * 0.5f;

        return point + right * shiftRight + up * shiftUp + forward * shiftForward;
    }

    /// <summary>
    /// Smallest move that brings the interval inside the dead zone, or the centring move when it
    /// is too wide to fit. Symmetric limits keep the two cases continuous where they meet, so the
    /// target never jumps as an actor drifts across the boundary.
    /// </summary>
    private static float DeadZoneShift(float min, float max, float limit)
    {
        bool overFar = max > limit;
        bool overNear = min < -limit;

        if (overFar && overNear)
        {
            return (min + max) * 0.5f;
        }

        if (overFar)
        {
            return max - limit;
        }

        if (overNear)
        {
            return min + limit;
        }

        return 0.0f;
    }

    /// <summary>
    /// Smallest spring length that keeps every corner inside the safe area, solved per corner so
    /// the near Player and the far enemy are each judged at their own depth.
    /// </summary>
    private float RequiredDistance(
        Vector3 point,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        float tanHorizontal,
        float tanVertical)
    {
        float safeHorizontal = Mathf.Max(tanHorizontal * _framingSafeAreaFraction, 0.0001f);
        float safeVertical = Mathf.Max(tanVertical * _framingSafeAreaFraction, 0.0001f);

        float required = _minEncounterDistance;

        foreach (ActorBase target in _framingTargets)
        {
            if (!IsUsable(target))
            {
                continue;
            }

            Vector3 centre = target.GlobalPosition;
            float radius = target.CollisionRadius;
            float halfHeight = target.CollisionHalfHeight;

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 offset = centre - point + new Vector3(
                    (corner & 1) == 0 ? -radius : radius,
                    (corner & 2) == 0 ? -halfHeight : halfHeight,
                    (corner & 4) == 0 ? -radius : radius);

                float depthOffset = offset.Dot(forward);

                // A point sits inside the frame when |lateral| <= tan * depth, and depth is the
                // spring length plus however far past the framing point the corner lies.
                required = Mathf.Max(required, Mathf.Abs(offset.Dot(right)) / safeHorizontal - depthOffset);
                required = Mathf.Max(required, Mathf.Abs(offset.Dot(up)) / safeVertical - depthOffset);

                // Never let a corner end up behind the lens.
                required = Mathf.Max(required, 1.0f - depthOffset);
            }
        }

        return required;
    }

    private bool HasAnyFramingSource()
    {
        if (_framingFallback != null && GodotObject.IsInstanceValid(_framingFallback))
        {
            return true;
        }

        foreach (ActorBase target in _framingTargets)
        {
            if (IsUsable(target))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>CombatCenter when it is available, otherwise wherever the rig already is.</summary>
    private Vector3 FallbackAnchor()
    {
        if (_framingFallback != null && GodotObject.IsInstanceValid(_framingFallback))
        {
            return _framingFallback.GlobalPosition;
        }

        return GlobalPosition;
    }

    /// <summary>
    /// Eases the spring length toward its target, quickly outward and gently back in, so a brief
    /// spread does not start the camera pumping.
    /// </summary>
    private float ApproachDistance(float current, float desired, float dt, bool snap)
    {
        if (snap)
        {
            return desired;
        }

        float speed = desired > current ? _zoomOutResponseSpeed : _zoomInResponseSpeed;
        return Mathf.Lerp(current, desired, Response(speed, dt));
    }

    /// <summary>Frame-rate independent smoothing weight.</summary>
    private static float Response(float speed, float dt)
    {
        return 1.0f - Mathf.Exp(-speed * dt);
    }

    private void FollowTarget(float dt)
    {
        if (_followTarget == null)
        {
            return;
        }

        GlobalPosition = GlobalPosition.Lerp(_followTarget.GlobalPosition, Response(_followSpeed, dt));
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

    /// <summary>
    /// Bounds of a set of world points in the camera's own frame: lateral extents across the
    /// screen plane, and depth extents along the view axis.
    /// </summary>
    private struct FrameExtents
    {
        public float RightMin;
        public float RightMax;
        public float UpMin;
        public float UpMax;
        public float DepthMin;
        public float DepthMax;

        private bool _hasAny;

        public void Add(float right, float up, float depthOffset)
        {
            if (!_hasAny)
            {
                _hasAny = true;
                RightMin = RightMax = right;
                UpMin = UpMax = up;
                DepthMin = DepthMax = depthOffset;
                return;
            }

            RightMin = Mathf.Min(RightMin, right);
            RightMax = Mathf.Max(RightMax, right);
            UpMin = Mathf.Min(UpMin, up);
            UpMax = Mathf.Max(UpMax, up);
            DepthMin = Mathf.Min(DepthMin, depthOffset);
            DepthMax = Mathf.Max(DepthMax, depthOffset);
        }
    }
}
