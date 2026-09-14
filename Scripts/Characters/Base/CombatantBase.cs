using System;
using Godot;

/// <summary>
/// Shared behaviour for the two automatic combatants: health, approach movement, attack timing,
/// damage response and placeholder feedback. CombatEncounter decides when any of it may run.
/// Player deliberately does not derive from this, so it never gains autonomous attack behaviour.
///
/// Authored values are the exports below; everything the fight changes lives in the runtime
/// fields and is cleared explicitly by <see cref="ResetForEncounter"/>.
/// </summary>
public partial class CombatantBase : ActorBase
{
    /// <summary>Holds the mesh. Rotated for facing and tilted on defeat; never scaled by physics.</summary>
    [Export] private Node3D _visuals = null!;

    /// <summary>Tinted for anticipation, hit and defeat cues.</summary>
    [Export] private MeshInstance3D _mesh = null!;

    [ExportGroup("Combat")]
    [Export(PropertyHint.Range, "1,200,1")] private float _maxHealth = 24.0f;
    [Export(PropertyHint.Range, "0.5,20,0.1")] private float _moveSpeed = 3.4f;
    [Export(PropertyHint.Range, "1,50,0.5")] private float _attackDamage = 6.0f;

    /// <summary>Furthest distance a hit can land from. Raised if it is inside the contact distance.</summary>
    [Export(PropertyHint.Range, "0.5,10,0.1")] private float _attackRange = 2.2f;

    [Export(PropertyHint.Range, "0.1,5,0.05")] private float _attackCooldownSeconds = 1.2f;

    /// <summary>Visible anticipation before the hit lands. The combatant holds still through it.</summary>
    [Export(PropertyHint.Range, "0.05,2,0.05")] private float _attackWindupSeconds = 0.3f;

    /// <summary>Gap left between the two collision radii when approaching.</summary>
    [Export(PropertyHint.Range, "0,2,0.05")] private float _approachPadding = 0.3f;

    private const float HitFlashSeconds = 0.14f;
    private const float DefeatTiltDegrees = 75.0f;
    private static readonly Color WindupColor = new(1.0f, 0.85f, 0.2f);
    private static readonly Color HitColor = new(1.0f, 1.0f, 1.0f);
    private static readonly Color FallbackAlbedo = new(0.72f, 0.78f, 0.9f);

    // Runtime state - reset for every encounter, never authored.
    private float _health;
    private CombatantBase _target;
    private float _cooldownRemaining;
    private float _windupRemaining;
    private float _hitFlashRemaining;
    private float _facingYaw;
    private bool _isCombatActive;
    private bool _isDefeated;

    private float _gravity = 9.8f;
    private float _collisionRadius = 0.5f;
    private StandardMaterial3D _runtimeMaterial;
    private Color _baselineAlbedo = FallbackAlbedo;
    private Vector3 _baselineVisualsRotation;
    private Vector3 _baselineVisualsScale = Vector3.One;

    /// <summary>Raised once, when this combatant's health first reaches zero.</summary>
    public event Action<CombatantBase> Defeated;

    /// <summary>Raised whenever health changes, including on reset, so the HUD can follow.</summary>
    public event Action<CombatantBase> HealthChanged;

    public float Health => _health;

    public float MaxHealth => _maxHealth;

    public bool IsDefeated => _isDefeated;

    /// <summary>True while an attack is in its anticipation window, before the hit lands.</summary>
    public bool IsWindingUp => _windupRemaining > 0.0f;

    /// <summary>Radius of the body's own collision shape, used to derive a stopping distance.</summary>
    public float CollisionRadius => _collisionRadius;

    public override void _Ready()
    {
        base._Ready();

        _gravity = ProjectSettings
            .GetSetting("physics/3d/default_gravity", 9.8f)
            .AsSingle();

        _collisionRadius = ResolveCollisionRadius();
        CaptureVisualBaseline();

        _health = _maxHealth;
        _facingYaw = _baselineVisualsRotation.Y;
        RefreshVisuals();
    }

    /// <summary>
    /// Clears every runtime value and assigns the opponent for the coming fight. Safe to call
    /// between encounters; it does not move the body, which stays Game's responsibility.
    /// </summary>
    public void ResetForEncounter(CombatantBase opponent)
    {
        if (ReferenceEquals(opponent, this))
        {
            GD.PushWarning($"{Name}: cannot target itself; opponent cleared.");
            opponent = null;
        }

        _target = opponent;
        _health = _maxHealth;
        _cooldownRemaining = 0.0f;
        _windupRemaining = 0.0f;
        _hitFlashRemaining = 0.0f;
        _isCombatActive = false;
        _isDefeated = false;
        _facingYaw = _baselineVisualsRotation.Y;

        Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);

        if (opponent != null && _attackRange < ContactDistanceTo(opponent))
        {
            GD.PushWarning(
                $"{Name}: attack range {_attackRange} is inside the contact distance; " +
                "it will be raised so attacks can land.");
        }

        RefreshVisuals();
        HealthChanged?.Invoke(this);
    }

    /// <summary>Lets this combatant start approaching and attacking. Called on entering Combat.</summary>
    public void StartCombat()
    {
        if (_isDefeated)
        {
            return;
        }

        _isCombatActive = true;
    }

    /// <summary>
    /// Stops movement and drops any attack still winding up. Used for defeat, the debug exit and
    /// every other cancellation, so there is one stop path. A defeated pose is left in place.
    /// </summary>
    public void StopCombat()
    {
        _isCombatActive = false;
        _windupRemaining = 0.0f;
        _cooldownRemaining = 0.0f;
        _hitFlashRemaining = 0.0f;
        Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);

        RefreshVisuals();
    }

    /// <summary>
    /// Applies one hit. Health is clamped at zero and <see cref="Defeated"/> fires exactly once,
    /// because the defeated flag short-circuits every later call.
    /// </summary>
    public void ApplyDamage(float amount)
    {
        if (_isDefeated || amount <= 0.0f)
        {
            return;
        }

        _health = Mathf.Max(0.0f, _health - amount);
        _hitFlashRemaining = HitFlashSeconds;
        HealthChanged?.Invoke(this);

        if (_health > 0.0f)
        {
            RefreshVisuals();
            return;
        }

        _isDefeated = true;
        _isCombatActive = false;
        _windupRemaining = 0.0f;
        _cooldownRemaining = 0.0f;
        Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);

        RefreshVisuals();
        Defeated?.Invoke(this);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        ApplyGravity(dt);

        if (_isCombatActive && !_isDefeated)
        {
            TickCombat(dt);
        }
        else
        {
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
        }

        MoveAndSlide();

        if (_hitFlashRemaining > 0.0f)
        {
            _hitFlashRemaining = Mathf.Max(0.0f, _hitFlashRemaining - dt);
        }

        RefreshVisuals();
    }

    private void TickCombat(float dt)
    {
        _cooldownRemaining = Mathf.Max(0.0f, _cooldownRemaining - dt);

        if (!IsTargetValid())
        {
            // Nothing worth acting on: hold position rather than drifting at a stale target.
            _windupRemaining = 0.0f;
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
            return;
        }

        FaceTarget();

        if (_windupRemaining > 0.0f)
        {
            // Committed to a swing: stand still until it lands.
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
            _windupRemaining -= dt;

            if (_windupRemaining <= 0.0f)
            {
                _windupRemaining = 0.0f;
                ResolveAttack();
            }

            return;
        }

        float distance = PlanarDistanceTo(_target);
        float stopDistance = ContactDistanceTo(_target);

        if (distance > stopDistance)
        {
            MoveTowardTarget(distance, stopDistance, dt);
        }
        else
        {
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
        }

        if (_cooldownRemaining <= 0.0f && distance <= EffectiveAttackRangeTo(_target))
        {
            _windupRemaining = _attackWindupSeconds;
        }
    }

    /// <summary>
    /// Lands a single hit. The cooldown is spent either way, and validity plus range are rechecked
    /// here rather than trusted from when the windup started - the target may have been defeated,
    /// removed or pushed out of reach in between.
    /// </summary>
    private void ResolveAttack()
    {
        _cooldownRemaining = _attackCooldownSeconds;

        if (!IsTargetValid() || PlanarDistanceTo(_target) > EffectiveAttackRangeTo(_target))
        {
            return;
        }

        _target.ApplyDamage(_attackDamage);
    }

    private void MoveTowardTarget(float distance, float stopDistance, float dt)
    {
        Vector3 toTarget = _target.GlobalPosition - GlobalPosition;
        toTarget.Y = 0.0f;

        if (toTarget.LengthSquared() < 0.0001f)
        {
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
            return;
        }

        Vector3 direction = toTarget.Normalized();

        // Never step past the stopping point, so the two settle at contact instead of grinding
        // into each other and shoving the pair across the arena.
        float remaining = Mathf.Max(0.0f, distance - stopDistance);
        float speed = dt > 0.0f ? Mathf.Min(_moveSpeed, remaining / dt) : 0.0f;

        Vector3 velocity = Velocity;
        velocity.X = direction.X * speed;
        velocity.Z = direction.Z * speed;
        Velocity = velocity;
    }

    private void ApplyGravity(float dt)
    {
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
            velocity.Y -= _gravity * dt;
        }

        Velocity = velocity;
    }

    /// <summary>A target must be present, still in the tree, alive and not this combatant.</summary>
    private bool IsTargetValid()
    {
        return _target != null
               && IsInstanceValid(_target)
               && !ReferenceEquals(_target, this)
               && _target.IsInsideTree()
               && !_target.IsDefeated;
    }

    /// <summary>Where the pair should come to rest, derived from both collision radii.</summary>
    private float ContactDistanceTo(CombatantBase other)
    {
        return _collisionRadius + other.CollisionRadius + _approachPadding;
    }

    /// <summary>
    /// The authored range, but never less than the distance the pair actually stops at, or a
    /// mistuned range would leave them standing nose to nose unable to connect.
    /// </summary>
    private float EffectiveAttackRangeTo(CombatantBase other)
    {
        return Mathf.Max(_attackRange, ContactDistanceTo(other) + 0.1f);
    }

    private float PlanarDistanceTo(Node3D other)
    {
        Vector3 delta = other.GlobalPosition - GlobalPosition;
        delta.Y = 0.0f;
        return delta.Length();
    }

    private void FaceTarget()
    {
        Vector3 toTarget = _target.GlobalPosition - GlobalPosition;
        toTarget.Y = 0.0f;

        if (toTarget.LengthSquared() < 0.0001f)
        {
            return;
        }

        _facingYaw = Mathf.Atan2(-toTarget.X, -toTarget.Z);
    }

    private float ResolveCollisionRadius()
    {
        // Only direct children, so a Hurtbox area's own shape cannot be mistaken for the body's.
        foreach (Node child in GetChildren())
        {
            if (child is not CollisionShape3D collider)
            {
                continue;
            }

            switch (collider.Shape)
            {
                case CapsuleShape3D capsule:
                    return capsule.Radius;
                case SphereShape3D sphere:
                    return sphere.Radius;
                case CylinderShape3D cylinder:
                    return cylinder.Radius;
            }
        }

        return 0.5f;
    }

    /// <summary>
    /// Takes a private copy of the authored material so the tint cues cannot leak into the shared
    /// resource, and remembers the authored visuals transform to return to.
    /// </summary>
    private void CaptureVisualBaseline()
    {
        if (_visuals != null)
        {
            _baselineVisualsRotation = _visuals.Rotation;
            _baselineVisualsScale = _visuals.Scale;
        }

        if (_mesh == null)
        {
            return;
        }

        if (_mesh.GetActiveMaterial(0) is StandardMaterial3D authored)
        {
            _runtimeMaterial = (StandardMaterial3D)authored.Duplicate();
        }
        else
        {
            _runtimeMaterial = new StandardMaterial3D { AlbedoColor = FallbackAlbedo };
        }

        _baselineAlbedo = _runtimeMaterial.AlbedoColor;
        _mesh.MaterialOverride = _runtimeMaterial;
    }

    /// <summary>
    /// Rebuilt from runtime state every frame rather than patched on state changes, so the
    /// baseline material, rotation and scale are always restored once a cue ends or is cancelled.
    /// </summary>
    private void RefreshVisuals()
    {
        float windupProgress = _windupRemaining > 0.0f
            ? 1.0f - Mathf.Clamp(_windupRemaining / Mathf.Max(_attackWindupSeconds, 0.0001f), 0.0f, 1.0f)
            : 0.0f;

        if (_runtimeMaterial != null)
        {
            Color albedo = _baselineAlbedo;

            if (_isDefeated)
            {
                albedo = _baselineAlbedo.Darkened(0.6f);
            }
            else if (_hitFlashRemaining > 0.0f)
            {
                albedo = HitColor;
            }
            else if (_windupRemaining > 0.0f)
            {
                albedo = _baselineAlbedo.Lerp(WindupColor, windupProgress);
            }

            _runtimeMaterial.AlbedoColor = albedo;
        }

        if (_visuals == null)
        {
            return;
        }

        Vector3 rotation = _baselineVisualsRotation;
        rotation.Y = _facingYaw;

        if (_isDefeated)
        {
            // Incapacitated, not destroyed: the body stays in the scene, just toppled over.
            rotation.Z = Mathf.DegToRad(DefeatTiltDegrees);
        }

        _visuals.Rotation = rotation;
        _visuals.Scale = _isDefeated
            ? _baselineVisualsScale
            : _baselineVisualsScale * (1.0f + 0.18f * windupProgress);
    }
}
