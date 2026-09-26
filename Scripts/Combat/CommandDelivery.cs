using Godot;

public partial class CombatEncounter
{
    private string DeliveryRestriction()
    {
        if (_player.IsDodging) return "Cannot give a command while dodging";
        if (_player.IsSwingingBat) return "Cannot give a command while swinging the bat";
        return "";
    }

    private bool BeginCommandDelivery(PlayerCommand command)
    {
        string rejection = IsDeliveringCommand ? "Already calling a command" : DeliveryRestriction();
        if (rejection != "")
        {
            ShowCommandNotice(command, rejection);
            return false;
        }

        _pendingCommand = command;
        _commandDeliveryRemaining = Mathf.Max(0, CommandDeliverySeconds);
        _player.SetCommandDeliveryActive(true);
        // Zero duration is an explicit tuning option, independent of permission to move.
        if (_commandDeliveryRemaining <= 0) CompleteCommandDelivery();
        return true;
    }

    private void TickCommandDelivery(float dt)
    {
        if (!IsDeliveringCommand) return;
        if (State != EncounterState.Combat || !_player.IsCombatActive || _player.IsDefeated
            || !_allySpirit.IsCombatActive || _allySpirit.IsDefeated
            || !IsInstanceValid(CommandEnemy) || !CommandEnemy.IsCombatActive || CommandEnemy.IsDefeated)
        {
            ClearCommandDelivery();
            return;
        }

        if (DeliveryRestriction() != "")
        {
            PlayerCommand cancelled = _pendingCommand;
            ClearCommandDelivery();
            ShowCommandNotice(cancelled, "Command cancelled - dodging");
            return;
        }

        _commandDeliveryRemaining = Mathf.Max(0, _commandDeliveryRemaining - dt);
        if (_commandDeliveryRemaining <= 0) CompleteCommandDelivery();
    }

    private void CompleteCommandDelivery()
    {
        PlayerCommand command = _pendingCommand;
        ClearCommandDelivery();
        // Recheck availability and Help proximity before spending a cooldown or changing the spirit.
        if (command == PlayerCommand.Help) RequestHelp(deliver: true);
        else if (command == PlayerCommand.Distance) RequestKeepDistance(deliver: true);
    }

    private void ShowCommandNotice(PlayerCommand command, string text)
    {
        if (command == PlayerCommand.Help) ShowHelpNotice(text);
        else
        {
            _distanceNotice = text;
            _distanceNoticeRemaining = 1.2f;
        }
    }

    private void ClearCommandDelivery()
    {
        _pendingCommand = PlayerCommand.None;
        _commandDeliveryRemaining = 0;
        if (IsInstanceValid(_player)) _player.SetCommandDeliveryActive(false);
    }
}
