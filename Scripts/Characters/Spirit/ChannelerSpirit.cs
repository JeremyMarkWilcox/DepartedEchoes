using Godot;

/// <summary>Ranged family prototype. Shared SpiritBase still owns traits and command responses.</summary>
public partial class ChannelerSpirit : SpiritBase
{
    [ExportGroup("Channeler")]
    [Export(PropertyHint.Range, "2,10,0.5")] private float _preferredDistance = 6.0f;
    [Export(PropertyHint.Range, "2,30,0.5")] private float _projectileSpeed = 12.0f;
    [Export(PropertyHint.Range, "0.2,5,0.1")] private float _projectileLifetime = 2.0f;
    [Export] private PackedScene _projectileScene = null!;

    protected override float ApproachStopDistanceTo(DamageableActor target)
    {
        return Mathf.Clamp(_preferredDistance, ContactDistanceTo(target), EffectiveAttackRangeTo(target) - 0.05f);
    }

    protected override void ExecuteAttack(DamageableActor target, float damage)
    {
        if (_projectileScene == null) return;
        var projectile = _projectileScene.Instantiate<SpiritProjectile>();
        AddChild(projectile);
        projectile.Launch(this, target, damage, Mathf.Max(0.1f, _projectileSpeed),
            Mathf.Max(0.1f, _projectileLifetime));
    }

    internal void ResolveProjectileHit(DamageableActor target, float damage)
    {
        if (!IsCombatActive || IsDefeated || !IsInstanceValid(target)
            || target != _target || !target.IsCombatActive || target.IsDefeated) return;
        ApplyAttackDamage(target, damage);
    }

    private void ClearProjectiles()
    {
        foreach (Node child in GetChildren())
            if (child is SpiritProjectile projectile) projectile.Discard();
    }

    public override void StopCombat()
    {
        ClearProjectiles();
        base.StopCombat();
    }

    public override void ResetForEncounter(DamageableActor opponent)
    {
        ClearProjectiles();
        base.ResetForEncounter(opponent);
    }
}
