using Godot;

public partial class GameHud : CanvasLayer
{
    [Export] private Label _readyLabel = null!;
    [Export] private Label _stateLabel = null!;
}