using Godot;

/// <summary>Straight shot aimed at launch. Sweeps each step so fast shots cannot tunnel through bodies.</summary>
public partial class SpiritProjectile : Node3D
{
    private ChannelerSpirit _source;
    private DamageableActor _target;
    private Vector3 _velocity;
    private float _damage, _remaining;
    private bool _spent;
    private Godot.Collections.Array<Rid> _excluded = new();

    public void Launch(ChannelerSpirit source, DamageableActor target, float damage, float speed, float lifetime)
    {
        _source = source;
        _target = target;
        _damage = damage;
        _remaining = lifetime;
        TopLevel = true;
        GlobalPosition = source.GlobalPosition;
        _velocity = (target.GlobalPosition - GlobalPosition).Normalized() * speed;
        _excluded.Add(source.GetRid());
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_spent) return;
        if (!IsInstanceValid(_source) || !_source.IsCombatActive || _source.IsDefeated
            || !IsInstanceValid(_target) || !_target.IsCombatActive || _target.IsDefeated)
        {
            Discard();
            return;
        }

        float step = Mathf.Min((float)delta, _remaining);
        Vector3 next = GlobalPosition + _velocity * step;
        var query = PhysicsRayQueryParameters3D.Create(GlobalPosition, next, uint.MaxValue, _excluded);
        query.HitFromInside = true;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0)
        {
            // Other bodies and scenery block the shot; there is no friendly fire.
            bool hitTarget = hit["collider"].AsGodotObject() == _target;
            Discard();
            if (hitTarget) _source.ResolveProjectileHit(_target, _damage);
            return;
        }
        GlobalPosition = next;
        _remaining -= step;
        if (_remaining <= 0) Discard();
    }

    public void Discard()
    {
        if (_spent) return;
        _spent = true;
        Hide();
        SetPhysicsProcess(false);
        QueueFree();
    }
}
