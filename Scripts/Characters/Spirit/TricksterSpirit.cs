using Godot;

/// <summary>
/// Agile disruptor prototype. It still uses the shared melee attack and commands, but briefly
/// bursts to the side after a landed hit so it does not stand in one place trading blows.
/// </summary>
public partial class TricksterSpirit : SpiritBase
{
    [ExportGroup("Trickster")]
    [Export(PropertyHint.Range, "3,15,0.5")] private float _burstSpeed = 9.0f;
    [Export(PropertyHint.Range, "0.1,1,0.05")] private float _burstSeconds = 0.35f;
    [Export(PropertyHint.Range, "0.5,5,0.1")] private float _reengageDistance = 2.8f;

    private float _burstRemaining;
    private Vector3 _burstDirection;

    public bool IsBursting => _burstRemaining > 0.0f;

    protected override float ApproachStopDistanceTo(DamageableActor target)
    {
        return Mathf.Max(ContactDistanceTo(target), _reengageDistance);
    }

    protected override void TickCombat(float dt)
    {
        if (IsBursting)
        {
            _burstRemaining = Mathf.Max(0.0f, _burstRemaining - dt);
            if (_burstRemaining <= 0.0f || !IsTargetValid())
            {
                _burstRemaining = 0.0f;
                Velocity = new Vector3(0, Velocity.Y, 0);
                return;
            }

            FaceTarget();
            Velocity = new Vector3(_burstDirection.X * _burstSpeed, Velocity.Y,
                _burstDirection.Z * _burstSpeed);
            return;
        }

        base.TickCombat(dt);
    }

    protected override void OnAttackLanded(float attackDamage, float damageDealt)
    {
        base.OnAttackLanded(attackDamage, damageDealt);
        if (!IsCombatActive || IsDefeated || !IsTargetValid()) return;

        Vector3 away = GlobalPosition - _target.GlobalPosition;
        away.Y = 0.0f;
        if (away.LengthSquared() < 0.0001f) away = Vector3.Back;
        away = away.Normalized();

        // A deterministic side step makes the prototype readable and avoids random pathing.
        _burstDirection = new Vector3(-away.Z, 0.0f, away.X).Normalized();
        _burstRemaining = Mathf.Max(0.05f, _burstSeconds);
    }

    public override void StopCombat()
    {
        _burstRemaining = 0.0f;
        base.StopCombat();
    }

    public override void ResetForEncounter(DamageableActor opponent)
    {
        _burstRemaining = 0.0f;
        base.ResetForEncounter(opponent);
    }
}
