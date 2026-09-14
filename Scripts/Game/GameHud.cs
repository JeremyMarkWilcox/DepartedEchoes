using Godot;

public partial class GameHud : CanvasLayer
{
    [Export] private Label _readyLabel = null!;
    [Export] private Label _stateLabel = null!;
    [Export] private Label _allyHealthLabel = null!;
    [Export] private Label _enemyHealthLabel = null!;
    [Export] private Label _resultLabel = null!;

    public void SetStateText(string text)
    {
        if (_stateLabel != null)
        {
            _stateLabel.Text = text;
        }
    }

    public void SetReadyVisible(bool isVisible)
    {
        if (_readyLabel != null)
        {
            _readyLabel.Visible = isVisible;
        }
    }

    /// <summary>Shows or hides both health readouts. Hidden during exploration.</summary>
    public void SetCombatHudVisible(bool isVisible)
    {
        if (_allyHealthLabel != null)
        {
            _allyHealthLabel.Visible = isVisible;
        }

        if (_enemyHealthLabel != null)
        {
            _enemyHealthLabel.Visible = isVisible;
        }
    }

    public void SetAllyHealth(float current, float max)
    {
        SetHealthText(_allyHealthLabel, "Ally", current, max);
    }

    public void SetEnemyHealth(float current, float max)
    {
        SetHealthText(_enemyHealthLabel, "Enemy", current, max);
    }

    public void ShowResult(string text)
    {
        if (_resultLabel == null)
        {
            return;
        }

        _resultLabel.Text = text;
        _resultLabel.Visible = true;
    }

    public void HideResult()
    {
        if (_resultLabel != null)
        {
            _resultLabel.Visible = false;
        }
    }

    private static void SetHealthText(Label label, string who, float current, float max)
    {
        if (label != null)
        {
            label.Text = $"{who}  {Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
        }
    }
}
