using Godot;
using System;
using System.Linq;

public partial class GameHud : CanvasLayer
{
    [Export] private Label _playerHealthLabel = null!;
    [Export] private Label _readyLabel = null!;
    [Export] private Label _stateLabel = null!;
    [Export] private Label _allyHealthLabel = null!;
    [Export] private Label _enemyHealthLabel = null!;
    [Export] private Label _resultLabel = null!;
    [Export] private Label _helpLabel = null!;
    private SpiritBase[] _allies = Array.Empty<SpiritBase>();
    private EnemyBase[] _enemies = Array.Empty<EnemyBase>();

    public void SetParticipantRoster(SpiritBase[] allies, EnemyBase[] enemies)
    {
        _allies = allies;
        _enemies = enemies;
        // Reserve complete rows even after defeat; no HUD jumping during a fight.
        if (_allyHealthLabel != null) _allyHealthLabel.Size = new Vector2(400, 28 * allies.Length);
        if (_enemyHealthLabel != null)
        {
            _enemyHealthLabel.Position = new Vector2(16, 50 + 30 * allies.Length);
            _enemyHealthLabel.Size = new Vector2(400, 28 * enemies.Length);
        }
        float bottom = 50 + 30 * (allies.Length + enemies.Length);
        if (_playerHealthLabel != null) _playerHealthLabel.Position = new Vector2(16, bottom);
        var controls = GetNodeOrNull<Label>("HUDRoot/ControlsLabel");
        if (controls != null)
        {
            controls.Position = new Vector2(16, bottom + 36);
        }
        if (_helpLabel != null) _helpLabel.Position = new Vector2(16, bottom + 100);
        RefreshParticipantHealth();
    }

    public void RefreshParticipantHealth()
    {
        if (_allyHealthLabel != null)
            _allyHealthLabel.Text = string.Join("\n", _allies.Select((a, i) =>
                HealthRow(a, _allies.Length == 1 ? "Ally" : $"Ally {i + 1}{(i == 0 ? " [commands]" : "")}")));
        if (_enemyHealthLabel != null)
            _enemyHealthLabel.Text = string.Join("\n", _enemies.Select((e, i) =>
                HealthRow(e, _enemies.Length == 1 ? "Enemy" : $"Enemy {i + 1}")));
    }

    private static string HealthRow(DamageableActor actor, string label) =>
        IsInstanceValid(actor) ? $"{label}  {Mathf.CeilToInt(actor.Health)} / {Mathf.CeilToInt(actor.MaxHealth)}" : $"{label} unavailable";

    public void SetHelpStatus(string text)
    {
        if (_helpLabel == null) return;
        _helpLabel.Text = text;
        _helpLabel.Visible = !string.IsNullOrEmpty(text);
    }

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

    /// <summary>Shows or hides all participant health readouts. Hidden during exploration.</summary>
    public void SetCombatHudVisible(bool isVisible)
    {
        if (_playerHealthLabel != null) _playerHealthLabel.Visible = isVisible;
        if (_allyHealthLabel != null)
        {
            _allyHealthLabel.Visible = isVisible;
        }

        if (_enemyHealthLabel != null)
        {
            _enemyHealthLabel.Visible = isVisible;
        }
    }

    public void SetPlayerHealth(float current, float max) => SetHealthText(_playerHealthLabel, "Player", current, max);

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
