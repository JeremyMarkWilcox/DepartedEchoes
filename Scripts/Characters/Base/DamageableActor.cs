using System;
using Godot;

/// <summary>Shared health/lifecycle only; does not grant movement or attack behavior.</summary>
public partial class DamageableActor : ActorBase
{
    [Export(PropertyHint.Range, "1,200,1")] protected float _maxHealth = 30.0f;
    protected float _health;
    protected bool _isDefeated;
    protected bool _isCombatActive;
    public float Health => _health;
    public float MaxHealth => _maxHealth;
    public bool IsDefeated => _isDefeated;
    public bool IsCombatActive => _isCombatActive;
    public DamageableActor LastAttacker { get; private set; }
    public virtual bool IsDamageProtected => false;
    public event Action<DamageableActor> Defeated;
    public event Action<DamageableActor> HealthChanged;

    public override void _Ready() { base._Ready(); _health = _maxHealth; }
    protected void ResetHealth()
    {
        _health = _maxHealth;
        _isDefeated = false;
        _isCombatActive = false;
        LastAttacker = null;
        HealthChanged?.Invoke(this);
    }
    public virtual void StartCombat() { if (!_isDefeated) _isCombatActive = true; }
    public virtual void StopCombat() { _isCombatActive = false; }
    public void RegisterAttacker(DamageableActor attacker)
    {
        if (attacker != null && attacker != this) LastAttacker = attacker;
    }
    public void ApplyDamage(float amount)
    {
        if (!_isCombatActive || _isDefeated || IsDamageProtected || !float.IsFinite(amount) || amount <= 0) return;
        _health = Mathf.Max(0, _health - amount);
        _isDefeated = _health <= 0;
        if (_isDefeated) StopCombat();
        OnDamageReceived();
        HealthChanged?.Invoke(this);
        if (_isDefeated) Defeated?.Invoke(this);
    }
    protected virtual void OnDamageReceived() { }
}
