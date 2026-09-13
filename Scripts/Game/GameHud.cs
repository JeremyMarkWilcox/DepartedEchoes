using Godot;

public partial class GameHud : CanvasLayer
{
    [Export] private Label _readyLabel = null!;
    [Export] private Label _stateLabel = null!;

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
}