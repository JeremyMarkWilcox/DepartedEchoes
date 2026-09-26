using System;
using System.Collections.Generic;
using Godot;

public partial class Player
{
    public enum BatPhase { Idle, Anticipation, Swing, Recovery }

    [ExportGroup("Bat")]
    [Export(PropertyHint.Range, "0.5,5,0.1")] private float _batReach = 2.4f;
    [Export(PropertyHint.Range, "0.5,4,0.1")] private float _batWidth = 2.0f;
    [Export(PropertyHint.Range, "0.05,1,0.01")] private float _batAnticipationSeconds = 0.16f;
    [Export(PropertyHint.Range, "0.05,0.5,0.01")] private float _batSwingSeconds = 0.12f;
    [Export(PropertyHint.Range, "0.1,2,0.05")] private float _batRecoverySeconds = 0.45f;
    [Export(PropertyHint.Range, "0.1,2,0.05")] private float _batStaggerSeconds = 0.55f;

    private EnemyBase[] _batTargets = Array.Empty<EnemyBase>();
    private float _batRemaining;
    private Vector3 _batForward;
    private Node3D _batPivot;
    private MeshInstance3D _batFootprint;
    private StandardMaterial3D _batCueMaterial;
    public BatPhase CurrentBatPhase { get; private set; }
    public bool IsSwingingBat => CurrentBatPhase != BatPhase.Idle;
    public string LastBatFeedback { get; private set; } = "";
    private float _batFeedbackRemaining;

    private void RecordBatFeedback(string message)
    {
        LastBatFeedback = message;
        _batFeedbackRemaining = 5;
    }

    private void ClearBatFeedback()
    {
        LastBatFeedback = "";
        _batFeedbackRemaining = 0;
    }

    public void SetBatTargets(EnemyBase[] targets) => _batTargets = targets;

    public bool TrySwingBat()
    {
        string blocked = IsDefeated ? "defeated" : !IsCombatActive ? "combat inactive"
            : !_isMovementEnabled ? "movement held" : IsDodging ? "dodging"
            : _isDeliveringCommand ? "delivering command"
            : IsSwingingBat ? (CurrentBatPhase == BatPhase.Recovery ? "recovering" : "already swinging")
            : !IsOnFloor() ? "airborne" : null;
        if (blocked != null) { RecordBatFeedback($"Blocked: {blocked}"); return false; }
        _batForward = FlattenDirection(_meshHolder != null ? -_meshHolder.GlobalBasis.Z : -GlobalBasis.Z, Vector3.Forward);
        CurrentBatPhase = BatPhase.Anticipation;
        _batRemaining = Mathf.Max(0.05f, _batAnticipationSeconds);
        Velocity = new Vector3(0, Velocity.Y, 0);
        RecordBatFeedback("Swing started; awaiting hit check");
        UpdateBatVisuals();
        return true;
    }

    private void TickBat(float dt)
    {
        _batFeedbackRemaining = Mathf.Max(0, _batFeedbackRemaining - dt);
        if (_batFeedbackRemaining <= 0) LastBatFeedback = "";
        if (!IsSwingingBat) return;
        if (!IsCombatActive || IsDefeated || !_isMovementEnabled) { CancelBat(); return; }
        _batRemaining -= dt;
        if (_batRemaining <= 0)
        {
            switch (CurrentBatPhase)
            {
                case BatPhase.Anticipation:
                    CurrentBatPhase = BatPhase.Swing;
                    _batRemaining = Mathf.Max(0.05f, _batSwingSeconds);
                    // A single hit instant: no repeated overlap damage or delayed callbacks.
                    var results = new Dictionary<string, int>();
                    foreach (EnemyBase enemy in _batTargets)
                    {
                        string result = BatMissReason(enemy);
                        if (result == null)
                            result = !HasBatLineOfSight(enemy) ? "obstructed"
                                : enemy.TryStagger(this, Mathf.Max(0.1f, _batStaggerSeconds)) ? "staggered"
                                : enemy.IsStaggered ? "already staggered" : "stagger rejected";
                        results[result] = results.GetValueOrDefault(result) + 1;
                    }
                    var summaries = new List<string>();
                    foreach (var result in results) summaries.Add($"{result.Value} {result.Key}");
                    RecordBatFeedback(summaries.Count == 0 ? "Hit check: no enemies" : "Hit check: " + string.Join(", ", summaries));
                    break;
                case BatPhase.Swing:
                    CurrentBatPhase = BatPhase.Recovery;
                    _batRemaining = Mathf.Max(0.1f, _batRecoverySeconds);
                    break;
                default: CancelBat(); break;
            }
        }
        UpdateBatVisuals();
    }

    private string BatMissReason(EnemyBase enemy)
    {
        if (!IsInstanceValid(enemy) || !enemy.IsInsideTree() || !enemy.IsCombatActive || enemy.IsDefeated) return "unavailable";
        Vector3 offset = enemy.GlobalPosition - GlobalPosition;
        if (Mathf.Abs(offset.Y) > CollisionHalfHeight + enemy.CollisionHalfHeight) return "outside height reach";
        float forward = offset.Dot(_batForward);
        if (forward <= 0) return "not in front";
        float side = Mathf.Max(Mathf.Abs(offset.Dot(_batForward.Cross(Vector3.Up))) - _batWidth / 2, 0);
        float beyond = Mathf.Max(forward - _batReach, 0);
        return side * side + beyond * beyond <= enemy.CollisionRadius * enemy.CollisionRadius ? null : "out of reach";
    }

    private bool HasBatLineOfSight(EnemyBase enemy)
    {
        var query = PhysicsRayQueryParameters3D.Create(GlobalPosition, enemy.GlobalPosition,
            uint.MaxValue, new Godot.Collections.Array<Rid> { GetRid() });
        query.HitFromInside = true;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0 || hit["collider"].AsGodotObject() == enemy;
    }

    private void CancelBat()
    {
        if (CurrentBatPhase == BatPhase.Anticipation) RecordBatFeedback("Cancelled before hit check");
        CurrentBatPhase = BatPhase.Idle;
        _batRemaining = 0;
        if (_batPivot != null) _batPivot.Visible = false;
        if (_batFootprint != null) _batFootprint.Visible = false;
    }

    private void CreateBatVisuals()
    {
        _batPivot = new Node3D { Visible = false };
        AddChild(_batPivot);
        _batPivot.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.12f, 1.3f) },
            Position = new Vector3(0.6f, 0.1f, -0.55f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.8f, 0.85f) }
        });
        _batCueMaterial = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        _batFootprint = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(_batWidth, 0.03f, _batReach) },
            MaterialOverride = _batCueMaterial, Visible = false
        };
        AddChild(_batFootprint);
    }

    private void UpdateBatVisuals()
    {
        if (!IsSwingingBat || _batPivot == null) return;
        float yaw = Mathf.Atan2(-_batForward.X, -_batForward.Z);
        float swing = CurrentBatPhase == BatPhase.Anticipation ? -0.8f
            : CurrentBatPhase == BatPhase.Swing ? Mathf.Lerp(-0.8f, 0.8f, 1 - _batRemaining / Mathf.Max(0.05f, _batSwingSeconds)) : 0.8f;
        _batPivot.GlobalRotation = new Vector3(0, yaw + swing, 0);
        _batPivot.Visible = true;
        _batFootprint.GlobalPosition = GlobalPosition + _batForward * (_batReach / 2) - Vector3.Up * (CollisionHalfHeight - 0.06f);
        _batFootprint.GlobalRotation = new Vector3(0, yaw, 0);
        _batFootprint.Visible = CurrentBatPhase != BatPhase.Recovery;
        _batCueMaterial.AlbedoColor = CurrentBatPhase == BatPhase.Swing ? new Color(1, 1, 1, 0.65f) : new Color(0.3f, 0.85f, 1, 0.3f);
    }
}
