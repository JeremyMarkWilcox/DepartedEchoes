using System.Linq;
using Godot;

/// <summary>Pursuer with committed strikes and a temporary, earned distraction.</summary>
public partial class EnemyBase : CombatantBase
{
    public enum TargetingMode { FixedTarget, Player, AllySpirit, Closest, LastAttacker, LowestHealth }
    [ExportGroup("Targeting Prototype")]
    [Export] public TargetingMode Targeting { get; set; } = TargetingMode.Player;
    [Export(PropertyHint.Range, "0.2,10,0.1")] private float _targetCommitSeconds = 3.0f;
    [Export] private bool _retargetAfterTargetDefeated = true;
    public enum AttackPhase { Pursue, Windup, Strike, Recovery }
    [Export] private float _strikeLength = 2.8f;
    [Export] private float _strikeWidth = 1.6f;
    [Export] private float _recoverySeconds = 0.9f;
    private const float StrikeSeconds = 0.12f;
    private float _phaseRemaining;
    private float _targetCommitRemaining;
    private DamageableActor _primaryTarget;
    private Player _playerTarget;
    private SpiritBase _allyTarget;
    private SpiritBase _pendingDistraction, _distractionTarget;
    private float _pendingDistractionSeconds, _distractionRemaining;
    private Vector3 _strikeOrigin, _strikeForward;
    private MeshInstance3D _telegraph;
    private StandardMaterial3D _telegraphMaterial;
    private Label3D _phaseLabel;
    public AttackPhase Phase { get; private set; }
    public Vector3 StrikeForward => _strikeForward;
    public DamageableActor CurrentTarget => _target;
    public bool HasPendingDistraction => IsInstanceValid(_pendingDistraction);
    public bool IsDistracted => IsInstanceValid(_distractionTarget);
    public bool CanAcceptDistraction => IsCombatActive && !IsDefeated
        && !HasPendingDistraction && !IsDistracted;
    public override bool IsWindingUp => _isCombatActive && Phase == AttackPhase.Windup;
    public override void _Ready()
    {
        base._Ready();
        _telegraphMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1, 0.65f, 0.05f, 0.5f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        _telegraph = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(_strikeWidth, 0.035f, _strikeLength) },
            MaterialOverride = _telegraphMaterial,
            Visible = false
        };
        AddChild(_telegraph);
        _phaseLabel = new Label3D
        {
            Position = new Vector3(0, 2, 0),
            FontSize = 42,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Visible = false
        };
        AddChild(_phaseLabel);
    }
    public override void ResetForEncounter(DamageableActor opponent)
    {
        base.ResetForEncounter(opponent);
        _primaryTarget = opponent;
        _playerTarget = opponent as Player ?? GetParent()?.GetChildren().OfType<Player>().FirstOrDefault();
        _allyTarget = GetParent()?.GetChildren().OfType<SpiritBase>().FirstOrDefault();
        _target = SelectTarget(true);
        ClearAttack();
    }

    public bool TryQueueDistraction(SpiritBase ally, float seconds)
    {
        if (!CanAcceptDistraction || !IsAvailable(ally) || !float.IsFinite(seconds) || seconds <= 0)
            return false;
        _pendingDistraction = ally;
        _pendingDistractionSeconds = seconds;
        return true;
    }

    public void CancelDistraction(SpiritBase ally)
    {
        if (_pendingDistraction == ally) _pendingDistraction = null;
        // Expiry is applied only between attacks; cancellation cannot redirect a telegraph.
        if (_distractionTarget == ally) _distractionRemaining = 0;
    }

    private static bool IsAvailable(DamageableActor actor) => IsInstanceValid(actor)
        && actor.IsInsideTree() && actor.IsCombatActive && !actor.IsDefeated;

    private void UpdateDistraction(float dt)
    {
        _distractionRemaining = Mathf.Max(0, _distractionRemaining - dt);
        _targetCommitRemaining = Mathf.Max(0, _targetCommitRemaining - dt);
        if (!IsAvailable(_pendingDistraction)) _pendingDistraction = null;
        if (Phase != AttackPhase.Pursue) return;

        if (IsDistracted && (_distractionRemaining <= 0 || !IsAvailable(_distractionTarget)))
        {
            _distractionTarget = null;
            _target = SelectTarget(true);
        }
        if (HasPendingDistraction)
        {
            _distractionTarget = _pendingDistraction;
            _target = _distractionTarget;
            _distractionRemaining = _pendingDistractionSeconds;
            _pendingDistraction = null;
        }
    }
    public override void StopCombat() { base.StopCombat(); ClearAttack(); }
    private void ClearAttack()
    {
        Phase = AttackPhase.Pursue;
        _phaseRemaining = _pendingDistractionSeconds = _distractionRemaining = 0;
        _pendingDistraction = _distractionTarget = null;
        if (!IsAvailable(_target) || _retargetAfterTargetDefeated) _target = SelectTarget(true);
        if (_telegraph != null) _telegraph.Visible = false;
        if (_phaseLabel != null) _phaseLabel.Visible = false;
    }
    protected override void TickCombat(float dt)
    {
        UpdateDistraction(dt);
        if (Phase == AttackPhase.Pursue && !IsDistracted && _targetCommitRemaining <= 0)
            _target = SelectTarget(false);
        // A fallen target does not cancel recovery or redirect a strike toward the player.
        // UpdateDistraction reacquires the player when this attack cycle reaches Pursue.
        if (!IsTargetValid() && Phase == AttackPhase.Pursue)
        {
            ClearAttack();
            Velocity = new Vector3(0, Velocity.Y, 0);
            return;
        }
        if (Phase == AttackPhase.Pursue)
        {
            FaceTarget();
            float distance = PlanarDistanceTo(_target);
            if (distance <= _strikeLength * 0.8f)
            {
                Velocity = new Vector3(0, Velocity.Y, 0);
                _strikeOrigin = GlobalPosition;
                _strikeForward = _target.GlobalPosition - GlobalPosition;
                _strikeForward.Y = 0;
                _strikeForward = _strikeForward.IsZeroApprox() ? Vector3.Forward : _strikeForward.Normalized();
                Phase = AttackPhase.Windup;
                _phaseRemaining = Mathf.Max(0.1f, _attackWindupSeconds);
                _telegraph.GlobalPosition = _strikeOrigin + _strikeForward * (_strikeLength / 2) - Vector3.Up * (CollisionHalfHeight - 0.04f);
                _telegraph.GlobalRotation = new Vector3(0, _facingYaw, 0);
            }
            else MoveTowardTarget(distance, ContactDistanceTo(_target), dt);
        }
        else
        {
            Velocity = new Vector3(0, Velocity.Y, 0);
            _phaseRemaining -= dt;
            if (_phaseRemaining <= 0)
            {
                if (Phase == AttackPhase.Windup)
                {
                    Phase = AttackPhase.Strike;
                    _phaseRemaining = StrikeSeconds;
                    // One hit test at the strike instant, not a homing or continuously damaging zone.
                    if (IsTargetValid() && StrikeContains(_target))
                    {
                        _target.RegisterAttacker(this);
                        _target.ApplyDamage(_attackDamage);
                    }
                    if (!_isCombatActive) return; // Damage can synchronously end the encounter.
                }
                else if (Phase == AttackPhase.Strike) { Phase = AttackPhase.Recovery; _phaseRemaining = Mathf.Max(0.1f, _recoverySeconds); }
                else Phase = AttackPhase.Pursue;
            }
        }
        _telegraph.Visible = Phase == AttackPhase.Windup || Phase == AttackPhase.Strike;
        _telegraphMaterial.AlbedoColor = Phase == AttackPhase.Strike ? new Color(1, 0.15f, 0.1f, 0.75f) : new Color(1, 0.65f, 0.05f, 0.5f);
        string targetCue = IsDistracted ? "TARGET: ALLY" : "TARGET: PLAYER";
        string phaseCue = Phase == AttackPhase.Windup ? "WINDUP !" : Phase == AttackPhase.Strike ? "STRIKE" :
            Phase == AttackPhase.Recovery ? "RECOVER" : "PURSUING";
        _phaseLabel.Text = targetCue + "\n" + phaseCue;
        _phaseLabel.Modulate = IsDistracted ? new Color(0.5f, 0.85f, 1) : Colors.White;
        _phaseLabel.Visible = true;
    }

    private DamageableActor SelectTarget(bool force)
    {
        DamageableActor chosen = Targeting switch
        {
            TargetingMode.AllySpirit => _allyTarget,
            TargetingMode.Closest => ClosestValidTarget(),
            TargetingMode.LastAttacker => IsSelectable(LastAttacker) ? LastAttacker : _primaryTarget,
            TargetingMode.LowestHealth => LowestHealthTarget(),
            TargetingMode.FixedTarget => _primaryTarget,
            _ => _playerTarget ?? _primaryTarget,
        };
        if (!IsSelectable(chosen)) chosen = FallbackTarget();
        if (chosen != null && (force || !ReferenceEquals(chosen, _target)))
            _targetCommitRemaining = Mathf.Max(0.1f, _targetCommitSeconds);
        return chosen;
    }

    private DamageableActor ClosestValidTarget() => ValidCandidates()
        .OrderBy(candidate => GlobalPosition.DistanceSquaredTo(candidate.GlobalPosition)).FirstOrDefault();

    private DamageableActor LowestHealthTarget() => ValidCandidates()
        .OrderBy(candidate => candidate.Health / Mathf.Max(candidate.MaxHealth, 0.01f)).FirstOrDefault();

    private DamageableActor FallbackTarget() => IsSelectable(_playerTarget) ? _playerTarget
        : IsSelectable(_allyTarget) ? _allyTarget : _primaryTarget;

    private static bool IsSelectable(DamageableActor actor) => IsInstanceValid(actor)
        && actor.IsInsideTree() && !actor.IsDefeated;

    private System.Collections.Generic.IEnumerable<DamageableActor> ValidCandidates()
    {
        if (IsSelectable(_playerTarget)) yield return _playerTarget;
        if (IsSelectable(_allyTarget) && !ReferenceEquals(_allyTarget, _playerTarget)) yield return _allyTarget;
    }
    public bool StrikeContains(DamageableActor actor)
    {
        if (actor == null || !IsInstanceValid(actor)) return false;
        Vector3 delta = actor.GlobalPosition - _strikeOrigin;
        if (Mathf.Abs(delta.Y) > CollisionHalfHeight + actor.CollisionHalfHeight) return false;
        float forward = delta.Dot(_strikeForward);
        float side = delta.Dot(_strikeForward.Cross(Vector3.Up));
        // Circle-versus-rectangle overlap, using the same dimensions as the visible footprint.
        float dx = Mathf.Max(Mathf.Abs(side) - _strikeWidth / 2, 0);
        float dz = forward < 0 ? -forward : Mathf.Max(forward - _strikeLength, 0);
        return dx * dx + dz * dz <= actor.CollisionRadius * actor.CollisionRadius;
    }
}
