using Godot;

/// <summary>
/// Autonomous attacks with one temporary response to the player's Help me request.
/// </summary>
public partial class SpiritBase : CombatantBase
{
    public enum HelpPhase { Idle, Approach, Provoke }

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
        _helpLabel = new Label3D
        {
            Position = new Vector3(0, 2.1f, 0), FontSize = 40,
            Modulate = new Color(0.5f, 0.85f, 1),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Visible = false
        };
        AddChild(_helpLabel);
    }

    public bool TryAcceptHelp(EnemyBase enemy)
    {
        if (!IsCombatActive || IsDefeated || IsRespondingToHelp || !IsTargetValid()
            || enemy != _target || !enemy.CanAcceptDistraction) return false;

        CancelPendingAttack();
        _helpEnemy = enemy;
        _responseRemaining = Mathf.Max(0.1f, _responseTimeoutSeconds);
        _provocationRemaining = 0;
        CurrentHelpPhase = HelpPhase.Approach;
        ShowHelpFeedback("On my way!");
        return true;
    }

    public override void ResetForEncounter(DamageableActor opponent)
    {
        ClearHelp();
        base.ResetForEncounter(opponent);
    }

    public override void StopCombat()
    {
        ClearHelp();
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
}
