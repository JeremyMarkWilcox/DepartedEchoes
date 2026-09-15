using Godot;

/// <summary>Prototype pursuer: one fixed player target and a committed rectangular strike.</summary>
public partial class EnemyBase : CombatantBase
{
    public enum AttackPhase { Pursue, Windup, Strike, Recovery }
    [Export] private float _strikeLength = 2.8f;
    [Export] private float _strikeWidth = 1.6f;
    [Export] private float _recoverySeconds = 0.9f;
    private const float StrikeSeconds = 0.12f;
    private float _phaseRemaining, _targetCueRemaining;
    private Vector3 _strikeOrigin, _strikeForward;
    private MeshInstance3D _telegraph;
    private StandardMaterial3D _telegraphMaterial;
    private Label3D _phaseLabel;
    public AttackPhase Phase { get; private set; }
    public Vector3 StrikeForward => _strikeForward;
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
        ClearAttack();
    }
    public override void StartCombat()
    {
        base.StartCombat();
        _targetCueRemaining = 1.2f;
    }
    public override void StopCombat() { base.StopCombat(); ClearAttack(); }
    private void ClearAttack()
    {
        Phase = AttackPhase.Pursue;
        _phaseRemaining = _targetCueRemaining = 0;
        if (_telegraph != null) _telegraph.Visible = false;
        if (_phaseLabel != null) _phaseLabel.Visible = false;
    }
    protected override void TickCombat(float dt)
    {
        if (!IsTargetValid()) { ClearAttack(); Velocity = new Vector3(0, Velocity.Y, 0); return; }
        _targetCueRemaining = Mathf.Max(0, _targetCueRemaining - dt);
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
                    if (StrikeContains(_target)) _target.ApplyDamage(_attackDamage);
                    if (!_isCombatActive) return; // Damage can synchronously end the encounter.
                }
                else if (Phase == AttackPhase.Strike) { Phase = AttackPhase.Recovery; _phaseRemaining = Mathf.Max(0.1f, _recoverySeconds); }
                else Phase = AttackPhase.Pursue;
            }
        }
        _telegraph.Visible = Phase == AttackPhase.Windup || Phase == AttackPhase.Strike;
        _telegraphMaterial.AlbedoColor = Phase == AttackPhase.Strike ? new Color(1, 0.15f, 0.1f, 0.75f) : new Color(1, 0.65f, 0.05f, 0.5f);
        _phaseLabel.Text = Phase == AttackPhase.Windup ? "WINDUP !" : Phase == AttackPhase.Strike ? "STRIKE" :
            Phase == AttackPhase.Recovery ? "RECOVER" : "TARGET: PLAYER";
        _phaseLabel.Visible = Phase != AttackPhase.Pursue || _targetCueRemaining > 0;
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
