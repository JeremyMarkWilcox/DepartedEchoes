using Godot;

/// <summary>
/// Autonomous attacks with temporary responses to player commands.
/// </summary>
public partial class SpiritBase : CombatantBase
{
    public enum HelpPhase { Idle, Approach, Provoke }

    [ExportGroup("Attack Willingness Prototype")]
    [Export] public bool BraveEnabled { get; set; } = true;
    [Export(PropertyHint.Range, "0,1,0.05")] private float _baselineWillingness = 0.4f;
    [Export(PropertyHint.Range, "0,1,0.05")] private float _braveWillingness = 1.0f;
    [Export(PropertyHint.Range, "0.1,3,0.05")] private float _hesitationSeconds = 0.6f;
    [Export(PropertyHint.Range, "1,5,1")] private int _maxConsecutiveHesitations = 2;
    [Export] public bool ShowDecisionDebug { get; set; } = true;

    [ExportGroup("Brave Damage Prototype")]
    [Export(PropertyHint.Range, "0,100,5")] public float BraveDamageBonusPercent { get; set; } = 10.0f;

    private readonly RandomNumberGenerator _willingnessRandom = new();
    private float _hesitationRemaining, _decisionFeedbackRemaining;
    private string _decisionFeedback = "";
    private float _damageFeedbackRemaining;
    private string _damageFeedback = "";
    public int ConsecutiveHesitations { get; private set; }
    public bool IsHesitating => _hesitationRemaining > 0;

    [ExportGroup("Help Me")]
    [Export(PropertyHint.Range, "0.1,3,0.05")] private float _provocationSeconds = 0.55f;
    [Export(PropertyHint.Range, "0.5,10,0.1")] private float _distractionSeconds = 3.0f;
    [Export(PropertyHint.Range, "1,15,0.5")] private float _responseTimeoutSeconds = 5.0f;

    private EnemyBase _helpEnemy;
    private float _responseRemaining, _provocationRemaining, _feedbackRemaining;
    private Label3D _helpLabel;
    public HelpPhase CurrentHelpPhase { get; private set; }
    public bool IsRespondingToHelp => CurrentHelpPhase != HelpPhase.Idle;
    public string HelpFeedback { get; private set; } = "";

    public override void _Ready()
    {
        base._Ready();
        _willingnessRandom.Randomize();
        _helpLabel = new Label3D
        {
            Position = new Vector3(0, 2.5f, 0), FontSize = 32,
            Modulate = new Color(0.5f, 0.85f, 1),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Visible = false
        };
        AddChild(_helpLabel);
    }

    public bool TryAcceptHelp(EnemyBase enemy)
    {
        if (!IsCombatActive || IsDefeated || IsRespondingToHelp || IsKeepingDistance || !IsTargetValid()
            || enemy != _target || !enemy.CanAcceptDistraction) return false;

        CancelPendingAttack();
        ClearWillingness();
        _helpEnemy = enemy;
        _responseRemaining = Mathf.Max(0.1f, _responseTimeoutSeconds);
        _provocationRemaining = 0;
        CurrentHelpPhase = HelpPhase.Approach;
        ShowHelpFeedback("On my way!");
        return true;
    }

    public override void ResetForEncounter(DamageableActor opponent)
    {
        ClearDistanceCommand();
        ClearHelp();
        ClearWillingness();
        base.ResetForEncounter(opponent);
    }

    public override void StopCombat()
    {
        ClearDistanceCommand();
        ClearHelp();
        ClearWillingness();
        base.StopCombat();
    }

    private void ClearHelp()
    {
        if (IsInstanceValid(_helpEnemy)) _helpEnemy.CancelDistraction(this);
        _helpEnemy = null;
        CurrentHelpPhase = HelpPhase.Idle;
        _responseRemaining = _provocationRemaining = _feedbackRemaining = 0;
        HelpFeedback = "";
        if (_helpLabel != null) _helpLabel.Visible = false;
    }

    private void ShowHelpFeedback(string text)
    {
        HelpFeedback = text;
        _feedbackRemaining = 2.0f;
        if (_helpLabel == null) return;
        _helpLabel.Text = text;
        _helpLabel.Visible = true;
    }

    private void FinishResponse(string text)
    {
        CurrentHelpPhase = HelpPhase.Idle;
        _responseRemaining = _provocationRemaining = 0;
        CancelPendingAttack();
        ShowHelpFeedback(text);
    }

    protected override void TickCombat(float dt)
    {
        _hesitationRemaining = Mathf.Max(0, _hesitationRemaining - dt);
        _decisionFeedbackRemaining = Mathf.Max(0, _decisionFeedbackRemaining - dt);
        _damageFeedbackRemaining = Mathf.Max(0, _damageFeedbackRemaining - dt);
        if (IsKeepingDistance)
        {
            TickDistanceCommand(dt);
            return;
        }
        if (!IsRespondingToHelp)
        {
            _feedbackRemaining = Mathf.Max(0, _feedbackRemaining - dt);
            if (_feedbackRemaining <= 0)
            {
                HelpFeedback = "";
                _helpLabel.Visible = false;
            }
            base.TickCombat(dt);
            return;
        }

        _responseRemaining -= dt;
        if (!IsTargetValid() || !IsInstanceValid(_helpEnemy) || !_helpEnemy.CanAcceptDistraction)
        {
            FinishResponse("Help cancelled");
            return;
        }
        if (_responseRemaining <= 0)
        {
            FinishResponse("Couldn't reach the threat");
            return;
        }

        FaceTarget();
        float distance = PlanarDistanceTo(_helpEnemy);
        float contact = ContactDistanceTo(_helpEnemy);
        bool inReach = distance <= contact + 0.2f
            && Mathf.Abs(GlobalPosition.Y - _helpEnemy.GlobalPosition.Y)
                <= CollisionHalfHeight + _helpEnemy.CollisionHalfHeight;

        if (!inReach)
        {
            if (CurrentHelpPhase != HelpPhase.Approach) ShowHelpFeedback("On my way!");
            CurrentHelpPhase = HelpPhase.Approach;
            _provocationRemaining = 0;
            MoveTowardTarget(distance, contact, dt);
            return;
        }

        Velocity = new Vector3(0, Velocity.Y, 0);
        if (CurrentHelpPhase == HelpPhase.Approach)
        {
            CurrentHelpPhase = HelpPhase.Provoke;
            _provocationRemaining = Mathf.Max(0.1f, _provocationSeconds);
            ShowHelpFeedback("Hey! Over here!");
            return;
        }

        _provocationRemaining -= dt;
        if (_provocationRemaining <= 0)
        {
            bool accepted = _helpEnemy.TryQueueDistraction(this, _distractionSeconds);
            FinishResponse(accepted ? "Distracting!" : "Help cancelled");
        }
    }

    // Recalculate from authored damage for each hit; the bonus never compounds across attacks.
    protected override float GetAttackDamage()
    {
        float bonus = BraveEnabled && float.IsFinite(BraveDamageBonusPercent)
            ? Mathf.Clamp(BraveDamageBonusPercent, 0, 100) : 0;
        return base.GetAttackDamage() * (1 + bonus / 100);
    }

    protected override void OnAttackLanded(float attackDamage, float damageDealt)
    {
        if (attackDamage <= _attackDamage) return;
        float bonus = (attackDamage / _attackDamage - 1) * 100;
        _damageFeedback = $"Brave +{bonus:0.#}%: {damageDealt:0.#} damage";
        _damageFeedbackRemaining = 1.0f;
    }

    protected override bool ShouldCommitAttack()
    {
        if (IsHesitating) return false;

        if (ConsecutiveHesitations >= Mathf.Max(1, _maxConsecutiveHesitations))
        {
            RecordDecision("Pity: attacking");
            ConsecutiveHesitations = 0;
            return true;
        }

        float chance = Mathf.Clamp(BraveEnabled ? _braveWillingness : _baselineWillingness, 0, 1);
        float roll = _willingnessRandom.Randf();
        // Explicit endpoints make 0% and 100% reliable prototype/test settings.
        bool accepted = chance >= 1 || (chance > 0 && roll < chance);
        RecordDecision($"Roll {roll:P0} / chance {chance:P0}: {(accepted ? "attack" : "hesitate")}");
        if (accepted)
        {
            ConsecutiveHesitations = 0;
            return true;
        }

        ConsecutiveHesitations++;
        _hesitationRemaining = Mathf.Max(0.1f, _hesitationSeconds);
        return false;
    }

    private void RecordDecision(string text)
    {
        _decisionFeedback = text;
        _decisionFeedbackRemaining = 1.4f;
    }

    private void ClearWillingness()
    {
        _hesitationRemaining = _decisionFeedbackRemaining = 0;
        ConsecutiveHesitations = 0;
        _decisionFeedback = "";
        _damageFeedbackRemaining = 0;
        _damageFeedback = "";
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        RefreshDecisionDisplay();
    }

    private void RefreshDecisionDisplay()
    {
        if (_helpLabel == null) return;
        if (!IsCombatActive || IsDefeated)
        {
            _helpLabel.Visible = false;
            return;
        }

        string intention = IsKeepingDistance ? DistanceFeedback
            : IsRespondingToHelp ? $"Help me: {CurrentHelpPhase}"
            : !IsTargetValid() ? "No target"
            : IsWindingUp ? "Attacking"
            : !IsInAttackRange(_target) ? "Approaching"
            : IsHesitating ? "Hesitating..."
            : IsAttackCoolingDown ? "Recovering" : "Ready";

        // Gameplay cues remain when the optional technical decision display is hidden.
        string text = IsKeepingDistance ? DistanceFeedback
            : !string.IsNullOrEmpty(HelpFeedback) ? HelpFeedback
            : IsHesitating && IsTargetValid() && IsInAttackRange(_target) ? "Hesitating..." : "";
        if (ShowDecisionDebug)
        {
            text = $"{(BraveEnabled ? "Brave" : "Baseline")} | {intention}";
            if (!IsKeepingDistance && (IsRespondingToHelp || !string.IsNullOrEmpty(HelpFeedback))) text += "\n" + HelpFeedback;
            else if (_decisionFeedbackRemaining > 0) text += "\n" + _decisionFeedback;
            if (_damageFeedbackRemaining > 0) text += "\n" + _damageFeedback;
            if (ConsecutiveHesitations > 0)
                text += $"\nHesitations: {ConsecutiveHesitations}/{Mathf.Max(1, _maxConsecutiveHesitations)}";
        }
        if (IsKeepingDistance && IsHesitating) text += "\nHesitating...";
        if (_helpLabel.Text != text) _helpLabel.Text = text;
        _helpLabel.Visible = !string.IsNullOrEmpty(text);
    }
}
