using Godot;

/// <summary>Optional read-only inspection of the existing combat state.</summary>
public partial class GameHud
{
    [ExportGroup("Combat Debug")]
    [Export] public bool ShowCombatDebug { get; set; }
    /// <summary>One-based roster number, matching the enemy health rows. Never changes targeting.</summary>
    [Export(PropertyHint.Range, "1,16,1,or_greater")] public int DebugEnemyNumber { get; set; } = 1;
    private Player _debugPlayer;
    private CombatEncounter _debugEncounter;
    private PanelContainer _debugPanel;
    private Label _debugText;

    public void ConnectCombatDebug(Player player, CombatEncounter encounter)
    {
        _debugPlayer = player;
        _debugEncounter = encounter;
        if (_debugPanel != null) return;
        _debugPanel = new PanelContainer { Name = "CombatDebugPanel", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        GetNode<Control>("HUDRoot").AddChild(_debugPanel);
        _debugPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _debugPanel.OffsetLeft = -316;
        _debugPanel.OffsetRight = -16;
        _debugPanel.OffsetTop = 12;
        _debugPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.025f, 0.035f, 0.05f, 0.92f),
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 8, ContentMarginBottom = 8
        });
        _debugText = new Label
        {
            Name = "Readout", CustomMinimumSize = new Vector2(280, 0),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _debugText.AddThemeFontSizeOverride("font_size", 16);
        _debugPanel.AddChild(_debugText);
    }

    public override void _Process(double delta)
    {
        UpdateCommandMarker();
        if (_debugPanel == null) return;
        _debugPanel.Visible = ShowCombatDebug;
        if (!ShowCombatDebug) return;
        string phase = IsInstanceValid(_debugEncounter) ? _debugEncounter.State.ToString() : "unavailable";
        string player = IsInstanceValid(_debugPlayer)
            ? $"Player: {Status(_debugPlayer)}\nBat: {_debugPlayer.CurrentBatPhase}\nDodge: {(_debugPlayer.IsDodging ? "active" : "inactive")} | Protected: {_debugPlayer.IsDamageProtected}\nGrounded: {_debugPlayer.IsOnFloor()}"
            : "Player unavailable";
        int count = IsInstanceValid(_debugEncounter) ? _debugEncounter.Enemies.Count : 0;
        int index = DebugEnemyNumber - 1;
        EnemyBase enemy = index >= 0 && index < count ? _debugEncounter.Enemies[index] : null;
        string enemyText = $"Enemy {DebugEnemyNumber} / {count}: unavailable";
        if (IsInstanceValid(enemy))
        {
            var target = enemy.CurrentTarget;
            string attack = enemy is HuskEnemy husk ? (husk.IsLungeAttack ? "Lunge" : "Overhead") : "Attack";
            enemyText = $"Enemy {DebugEnemyNumber} / {count}: {enemy.Name}\n{Status(enemy)} | HP: {enemy.Health:0.#}/{enemy.MaxHealth:0.#}\nMode: {enemy.Targeting}\nTarget: {(IsInstanceValid(target) ? target.Name.ToString() : "none")}\nPhase: {enemy.Phase} ({attack})";
        }
        string batFeedback = IsInstanceValid(_debugPlayer) && !string.IsNullOrEmpty(_debugPlayer.LastBatFeedback)
            ? _debugPlayer.LastBatFeedback : "none recent";
        _debugText.Text = $"COMBAT DEBUG (live)\nEncounter: {phase}\n{player}\nLast bat event (all enemies): {batFeedback}\n\n{enemyText}";
    }

    private static string Status(DamageableActor actor) => actor.IsDefeated ? "defeated" : actor.IsCombatActive ? "active" : "inactive";
}
