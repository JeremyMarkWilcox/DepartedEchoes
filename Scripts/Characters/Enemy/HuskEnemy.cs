using Godot;

/// <summary>Predictable first enemy: alternate a slow overhead swing and a committed lunge.</summary>
public partial class HuskEnemy : EnemyBase
{
    [ExportGroup("Husk Attacks")]
    [Export(PropertyHint.Range, "0.2,3,0.05")] private float _overheadWindupSeconds = 1.0f;
    [Export(PropertyHint.Range, "0.2,3,0.05")] private float _lungeWindupSeconds = 0.8f;
    [Export(PropertyHint.Range, "1,8,0.1")] private float _lungeDistance = 4.0f;
    [Export(PropertyHint.Range, "1,15,0.5")] private float _lungeSpeed = 8.0f;
    [Export(PropertyHint.Range, "0.2,3,0.1")] private float _lungeRecoverySeconds = 1.2f;
    private bool _nextIsLunge, _isLunge;
    private float _travelRemaining;
    public bool IsLungeAttack => _isLunge;

    protected override float AttackStartDistance => _nextIsLunge ? Mathf.Max(1, _lungeDistance) : base.AttackStartDistance;
    protected override float StrikeLength => _isLunge ? Mathf.Max(1, _lungeDistance) + CollisionRadius : base.StrikeLength;
    protected override float WindupSeconds => _isLunge ? _lungeWindupSeconds : _overheadWindupSeconds;
    protected override float ActiveSeconds => _isLunge ? Mathf.Max(1, _lungeDistance) / Mathf.Max(1, _lungeSpeed) : base.ActiveSeconds;
    protected override float RecoverySeconds => _isLunge ? _lungeRecoverySeconds : base.RecoverySeconds;
    protected override string AttackName => _isLunge ? "LUNGE" : "OVERHEAD";
    protected override bool MovingStrike => _isLunge;

    protected override void PrepareAttack()
    {
        _isLunge = _nextIsLunge;
        _nextIsLunge = !_nextIsLunge;
        _travelRemaining = Mathf.Max(1, _lungeDistance);
    }

    protected override Vector3 StrikeVelocity(float dt)
    {
        float travel = Mathf.Min(_travelRemaining, Mathf.Max(1, _lungeSpeed) * dt);
        _travelRemaining -= travel;
        // Physics delta normally equals dt; cap travel even on a long final frame.
        float physicsDelta = (float)GetPhysicsProcessDeltaTime();
        return StrikeForward * (physicsDelta > 0 ? travel / physicsDelta : 0);
    }

    public override void StopCombat()
    {
        _travelRemaining = 0;
        base.StopCombat();
    }

    public override void ResetForEncounter(DamageableActor opponent)
    {
        _isLunge = _nextIsLunge = false;
        _travelRemaining = 0;
        base.ResetForEncounter(opponent);
    }
}
