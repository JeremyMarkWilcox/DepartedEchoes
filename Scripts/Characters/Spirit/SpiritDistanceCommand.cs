using Godot;

public partial class SpiritBase
{
    [ExportGroup("Keep Your Distance")]
    [Export(PropertyHint.Range, "2,12,0.5")] private float _safeDistance = 5.0f;
    [Export(PropertyHint.Range, "0.1,5,0.1")] private float _distanceHoldSeconds = 1.5f;
    [Export(PropertyHint.Range, "1,15,0.5")] private float _distanceTimeoutSeconds = 6.0f;

    private float _distanceTimeRemaining, _distanceHoldRemaining;
    private bool _holdingDistance;
    public bool IsKeepingDistance { get; private set; }
    public string DistanceFeedback => !IsKeepingDistance ? ""
        : _holdingDistance ? "Holding distance." : "Creating distance...";

    public bool TryAcceptKeepDistance()
    {
        if (!IsCombatActive || IsDefeated || IsKeepingDistance || IsRespondingToHelp || !IsTargetValid())
            return false;

        CancelPendingAttack();
        // Preserve hesitation and its failure count. TickCombat keeps its timer running normally.
        HelpFeedback = "";
        _feedbackRemaining = 0;
        _decisionFeedbackRemaining = _damageFeedbackRemaining = 0;
        IsKeepingDistance = true;
        _holdingDistance = false;
        _distanceTimeRemaining = Mathf.Max(0.1f, _distanceTimeoutSeconds);
        _distanceHoldRemaining = Mathf.Max(0.1f, _distanceHoldSeconds);
        return true;
    }

    private void TickDistanceCommand(float dt)
    {
        _distanceTimeRemaining -= dt;
        if (!IsTargetValid() || _distanceTimeRemaining <= 0)
        {
            FinishDistanceCommand();
            return;
        }

        FaceTarget();
        float distance = PlanarDistanceTo(_target);
        float desired = Mathf.Max(_safeDistance, ContactDistanceTo(_target) + 0.5f);
        // A small tolerance prevents flickering between moving and holding at the boundary.
        if (_holdingDistance && distance < desired - 0.3f) _holdingDistance = false;
        if (!_holdingDistance && distance >= desired - 0.05f) _holdingDistance = true;

        if (!_holdingDistance)
        {
            MoveAwayFromTarget(desired - distance, dt);
            return;
        }

        Velocity = new Vector3(0, Velocity.Y, 0);
        _distanceHoldRemaining -= dt;
        if (_distanceHoldRemaining <= 0) FinishDistanceCommand();
    }

    private void FinishDistanceCommand()
    {
        ClearDistanceCommand();
        Velocity = new Vector3(0, Velocity.Y, 0);
        // Normal family attack behavior resumes next tick, subject to existing hesitation/cooldown.
    }

    private void ClearDistanceCommand()
    {
        IsKeepingDistance = _holdingDistance = false;
        _distanceTimeRemaining = _distanceHoldRemaining = 0;
    }
}
