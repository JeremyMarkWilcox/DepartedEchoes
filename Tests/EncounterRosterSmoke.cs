using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

/// <summary>Headless scene integration checks. Never launches or captures an interactive window.</summary>
public partial class EncounterRosterSmoke : Node
{
    private Game _game;
    private CombatEncounter _encounter;
    private int _checks;

    public override async void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            GD.PushError("Run EncounterRosterSmoke with --headless.");
            GetTree().Quit(1);
            return;
        }
        try
        {
            await CheckRoster(0, 0);
            await CheckRoster(0, 2);
            await CheckRoster(2, 2);
            foreach (Game.AllyFamily family in new[] { Game.AllyFamily.Guardian, Game.AllyFamily.Channeler, Game.AllyFamily.Trickster })
            {
                await LoadGame(0, 1, family);
                await BeginCombat();
                Check(_encounter.Allies[0].CurrentTarget is EnemyBase, $"{family} assigned an enemy");
                var victim = (EnemyBase)_encounter.Allies[0].CurrentTarget;
                victim.ApplyDamage(10000);
                await Frames(4);
                Check(_encounter.Allies[0].CurrentTarget != victim && _encounter.Allies[0].CurrentTarget is EnemyBase,
                    $"{family} retargets after defeat");
                ExitWithEnd();
                await Until(() => _encounter.State == CombatEncounter.EncounterState.Exploration, 180);
            }
            await CheckCommands();
            await CheckSpiritSelection();
            await CheckEnemyCandidates();
            await CheckCancellation();
            await CheckNaturalFight();
            await CheckNamedPresets();
            GD.Print($"ROSTER CHECKS PASSED: {_checks}");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private async Task LoadGame(int extraAllies, int extraEnemies, Game.AllyFamily family = Game.AllyFamily.UseCurrentScene)
    {
        if (IsInstanceValid(_game))
        {
            _game.QueueFree();
            await Frames(3);
        }
        _game = GD.Load<PackedScene>("res://Scenes/Game/Game.tscn").Instantiate<Game>();
        SetField(_game, "_allyFamily", family);
        var slots = _game.GetNode<Node3D>("EncounterSpawns").GetChildren().OfType<EncounterSpawn>().ToArray();
        slots[0].Enabled = extraAllies > 0;
        slots[1].Enabled = extraAllies > 1;
        slots[2].Enabled = extraEnemies > 0;
        slots[3].Enabled = extraEnemies > 1;
        AddChild(_game);
        _encounter = _game.GetNode<CombatEncounter>("CombatEncounter");
        await Frames(8);
        Check(_encounter.State == CombatEncounter.EncounterState.Exploration, "startup stays exploration");
        Check(_encounter.Allies.Count == extraAllies + 1 && _encounter.Enemies.Count == extraEnemies + 1, "enabled slots assembled once");
        var targets = (ActorBase[])Field(_encounter, "_framingTargets");
        Check(targets.Length == extraAllies + extraEnemies + 3 && targets.Distinct().Count() == targets.Length,
            "camera receives whole roster without duplicates");
    }

    private async Task BeginCombat()
    {
        _encounter.BeginEncounter();
        Check(_encounter.Allies.All(a => !a.IsCombatActive) && _encounter.Enemies.All(e => !e.IsCombatActive), "Setup holds entire roster");
        await Until(() => _encounter.State == CombatEncounter.EncounterState.Ready, 180);
        Check(_encounter.Allies.All(a => a.Health == a.MaxHealth && !a.IsCombatActive)
            && _encounter.Enemies.All(e => e.Health == e.MaxHealth && !e.IsCombatActive), "Ready holds full-health roster");
        await Until(() => _encounter.State == CombatEncounter.EncounterState.Combat, 180);
    }

    private async Task CheckRoster(int extraAllies, int extraEnemies)
    {
        await LoadGame(extraAllies, extraEnemies);
        var participants = _encounter.Allies.Cast<CombatantBase>().Concat(_encounter.Enemies).ToArray();
        var starts = participants.Select(a => a.GlobalPosition).ToArray();
        await BeginCombat();
        Check(participants.All(a => a.IsCombatActive), "Combat activates entire roster");
        var hud = _game.GetNode<GameHud>("GameHud");
        Check(hud.GetNode<Label>("HUDRoot/AllyHealthLabel").Text.Split('\n').Length == extraAllies + 1
            && hud.GetNode<Label>("HUDRoot/EnemyHealthLabel").Text.Split('\n').Length == extraEnemies + 1, "health rows include whole roster");
        for (int i = 0; i < _encounter.Enemies.Count; i++)
        {
            _encounter.Enemies[i].ApplyDamage(10000);
            if (i < _encounter.Enemies.Count - 1)
                Check(_encounter.State == CombatEncounter.EncounterState.Combat, "partial enemy defeat is not victory");
        }
        Check(_encounter.State == CombatEncounter.EncounterState.Resolving, "last enemy resolves victory");
        Check(participants.All(a => !a.IsCombatActive), "victory stops everyone immediately");
        await Until(() => _encounter.State == CombatEncounter.EncounterState.Exploration, 300);
        for (int i = 0; i < participants.Length; i++)
        {
            Check(participants[i].Health == participants[i].MaxHealth && !participants[i].IsDefeated && !participants[i].IsCombatActive,
                "reset clears health and activity");
            Check(participants[i].GlobalPosition.DistanceTo(starts[i]) < 0.1f, "reset returns actor to its marker");
        }
        Check(!hud.GetNode<Label>("HUDRoot/EnemyHealthLabel").Visible, "health hidden in exploration");
        await Frames(30);
        Check(_encounter.State == CombatEncounter.EncounterState.Exploration, "no unsolicited re-entry");
        await BeginCombat();
        var player = _game.GetNode<Player>("ActorContainer/Player");
        player.ApplyDamage(10000);
        Check(_encounter.State == CombatEncounter.EncounterState.Resolving && participants.All(a => !a.IsCombatActive),
            "player defeat stops all participants");
        ExitWithEnd();
        ExitWithEnd();
        await Until(() => _encounter.State == CombatEncounter.EncounterState.Exploration, 180);
    }

    private async Task CheckCommands()
    {
        await LoadGame(1, 1, Game.AllyFamily.Guardian);
        await BeginCombat();
        var ally = _encounter.Allies[0];
        var enemy = _encounter.Enemies[0];
        var player = _game.GetNode<Player>("ActorContainer/Player");
        player.GlobalPosition = enemy.GlobalPosition + Vector3.Right * 4;
        _encounter.CommandDeliverySeconds = 0;
        Check(_encounter.TryRequestHelp(), "main ally accepts Help with multiple enemies");
        Check(ally.IsRespondingToHelp && !_encounter.Allies[1].IsRespondingToHelp, "command remains scoped to main ally");
        ExitWithEnd();
        await Until(() => _encounter.State == CombatEncounter.EncounterState.Exploration, 180);
        Check(!_encounter.IsDeliveringCommand && !_encounter.IsHelpInProgress, "exit clears all command/distraction state");
        await BeginCombat();
        Check(_encounter.TryRequestKeepDistance(), "distance command works next encounter");
        ExitWithEnd();
        await Until(() => _encounter.State == CombatEncounter.EncounterState.Exploration, 180);
    }

    private async Task CheckSpiritSelection()
    {
        await LoadGame(2, 1);
        void Cycle(bool echo = false, bool controller = false)
        {
            if (controller)
            {
                Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.LeftShoulder, Pressed = true });
                Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.LeftShoulder, Pressed = false });
            }
            else
            {
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.R, Pressed = true, Echo = echo });
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.R, Pressed = false });
            }
            Input.FlushBufferedEvents();
        }
        Cycle();
        Check(_encounter.SelectedAlly == _encounter.Allies[0], "selection ignored outside combat");
        await BeginCombat();
        foreach (var actor in _encounter.Allies.Cast<CombatantBase>().Concat(_encounter.Enemies)) actor.SetPhysicsProcess(false);
        Cycle(echo: true);
        Check(_encounter.SelectedAlly == _encounter.Allies[0], "selection ignores keyboard echo");
        var enemyTarget = _encounter.Enemies[0].CurrentTarget;
        Cycle();
        Check(_encounter.SelectedAlly == _encounter.Allies[1] && _encounter.Enemies[0].CurrentTarget == enemyTarget,
            "R selects second spirit without retargeting enemy");
        var hud = _game.GetNode<GameHud>("GameHud");
        hud._Process(0);
        var marker = hud.GetNode<Label3D>("CommandSelectionMarker");
        Check(marker.Visible && hud.GetNode<Label>("HUDRoot/AllyHealthLabel").Text.Contains("Ally 2 [commands]"),
            "selected spirit has HUD and world indicators without debug enabled");
        _encounter.CommandDeliverySeconds = 0.3f;
        Check(_encounter.TryRequestKeepDistance(), "selected extra spirit accepts distance delivery");
        Cycle();
        Check(_encounter.SelectedAlly == _encounter.Allies[1] && _encounter.IsDeliveringCommand, "selection locks during delivery");
        await Frames(25);
        Check(_encounter.Allies[1].IsKeepingDistance && !_encounter.Allies[0].IsKeepingDistance, "command delivered only to selected recipient");
        float cooldown = _encounter.DistanceCooldownRemaining;
        Cycle(controller: true);
        Check(_encounter.SelectedAlly == _encounter.Allies[2] && _encounter.DistanceCooldownRemaining == cooldown
            && !_encounter.TryRequestKeepDistance(), "controller cycles and shared cooldown cannot be bypassed");
        var player = _game.GetNode<Player>("ActorContainer/Player");
        player.GlobalPosition = _encounter.Enemies[0].GlobalPosition + Vector3.Right * 4;
        Check(_encounter.TryRequestHelp(), "Help delivery can address third spirit");
        _encounter.Allies[2].ApplyDamage(10000);
        Check(!_encounter.IsDeliveringCommand && !player.IsHeldForCommand, "recipient defeat cancels delivery and movement hold immediately");
        await Frames(30);
        Check(_encounter.HelpCooldownRemaining == 0 && !_encounter.Allies[0].IsRespondingToHelp, "cancelled delivery never transfers to another spirit");
        Cycle();
        Check(_encounter.SelectedAlly == _encounter.Allies[0], "cycle wraps past defeated spirit");
        _encounter.CommandDeliverySeconds = 0;
        Check(_encounter.TryRequestHelp() && _encounter.Allies[0].IsRespondingToHelp, "Help lands on newly selected living spirit");
        Cycle();
        Check(_encounter.Allies[0].IsRespondingToHelp && _encounter.SelectedAlly == _encounter.Allies[1], "switching preserves an accepted response");
        _encounter.Allies[0].ApplyDamage(10000);
        _encounter.Allies[1].ApplyDamage(10000);
        Cycle();
        Check(!_encounter.TryRequestHelp() && !_encounter.TryRequestKeepDistance(), "all allies down safely rejects commands");
        ExitWithEnd();
        await Until(() => _encounter.State == CombatEncounter.EncounterState.Exploration, 180);
        hud._Process(0);
        Check(!marker.Visible && _encounter.SelectedAlly == _encounter.Allies[0], "return hides marker and resets selection");
        foreach (var actor in _encounter.Allies.Cast<CombatantBase>().Concat(_encounter.Enemies)) actor.SetPhysicsProcess(true);
        await BeginCombat();
        Check(_encounter.SelectedAlly == _encounter.Allies[0] && !_encounter.IsDeliveringCommand, "later encounter begins with clean selection");
        ExitWithEnd();
        await Until(() => _encounter.State == CombatEncounter.EncounterState.Exploration, 180);
    }

    private async Task CheckCancellation()
    {
        foreach (var phase in new[] { CombatEncounter.EncounterState.Setup, CombatEncounter.EncounterState.Ready, CombatEncounter.EncounterState.Combat })
        {
            await LoadGame(2, 2);
            _encounter.BeginEncounter();
            await Until(() => _encounter.State == phase, 360);
            ExitWithEnd();
            await Frames(1);
            Check(_encounter.State == CombatEncounter.EncounterState.Returning, $"injected End exits {phase}");
            ExitWithEnd();
            await Until(() => _encounter.State == CombatEncounter.EncounterState.Exploration, 180);
            await Frames(120);
            Check(_encounter.State == CombatEncounter.EncounterState.Exploration && _encounter.Allies.All(a => !a.IsCombatActive), "cancelled callbacks stay cancelled");
        }
    }

    private async Task CheckEnemyCandidates()
    {
        await LoadGame(2, 0);
        await BeginCombat();
        EnemyBase enemy = _encounter.Enemies[0];
        var player = _game.GetNode<Player>("ActorContainer/Player");
        var extra = _encounter.Allies[2];
        extra.GlobalPosition = enemy.GlobalPosition + Vector3.Right * 2;
        // Exercise selection rules directly, independent of attack commitment timing.
        DamageableActor Select(EnemyBase.TargetingMode mode)
        {
            enemy.Targeting = mode;
            return (DamageableActor)typeof(EnemyBase).GetMethod("SelectTarget", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(enemy, new object[] { true });
        }
        Check(Select(EnemyBase.TargetingMode.Player) == player, "Player targeting preserved");
        Check(Select(EnemyBase.TargetingMode.FixedTarget) == player, "FixedTarget preserved");
        Check(Select(EnemyBase.TargetingMode.AllySpirit) == _encounter.Allies[0], "AllySpirit uses roster order");
        Check(Select(EnemyBase.TargetingMode.Closest) == extra, "Closest considers additional allies");
        extra.ApplyDamage(extra.MaxHealth * 0.75f);
        Check(Select(EnemyBase.TargetingMode.LowestHealth) == extra, "LowestHealth considers additional allies");
        enemy.RegisterAttacker(extra);
        Check(Select(EnemyBase.TargetingMode.LastAttacker) == extra, "LastAttacker considers additional allies");
        _encounter.Allies[0].ApplyDamage(10000);
        Check(_encounter.State == CombatEncounter.EncounterState.Combat, "main ally defeat does not end group encounter");
        Check(Select(EnemyBase.TargetingMode.AllySpirit) == _encounter.Allies[1], "AllySpirit skips defeated main ally");
        ExitWithEnd();
        await Until(() => _encounter.State == CombatEncounter.EncounterState.Exploration, 180);
    }

    private async Task CheckNaturalFight()
    {
        await LoadGame(2, 2);
        foreach (EnemyBase enemy in _encounter.Enemies) enemy.Targeting = EnemyBase.TargetingMode.Closest;
        await BeginCombat();
        await Until(() => _encounter.State == CombatEncounter.EncounterState.Resolving, 3600);
        Check(_encounter.Allies.All(a => !a.IsCombatActive) && _encounter.Enemies.All(e => !e.IsCombatActive),
            "unassisted 3v3 fight reaches a terminal outcome and stops");
    }

    private async Task CheckNamedPresets()
    {
        foreach (string name in new[] { "Baseline", "HuskPractice", "SmallGroup" })
        {
            _game.QueueFree();
            await Frames(3);
            _game = GD.Load<PackedScene>($"res://Scenes/Game/Presets/{name}.tscn").Instantiate<Game>();
            var configuredFamily = (Game.AllyFamily)Field(_game, "_allyFamily");
            AddChild(_game);
            _encounter = _game.GetNode<CombatEncounter>("CombatEncounter");
            await Frames(8);
            int count = name == "SmallGroup" ? 3 : 1;
            Check(_encounter.State == CombatEncounter.EncounterState.Exploration, $"{name}: startup stays exploration");
            Check(_encounter.Allies.Count == count && _encounter.Enemies.Count == count, $"{name}: authored roster assembled");
            Check(configuredFamily switch
            {
                Game.AllyFamily.Channeler => _encounter.Allies[0] is ChannelerSpirit,
                Game.AllyFamily.Trickster => _encounter.Allies[0] is TricksterSpirit,
                Game.AllyFamily.Guardian => _encounter.Allies[0].GetType() == typeof(SpiritBase),
                _ => true
            }, $"{name}: main ally matches saved family choice");
            Check((_encounter.Enemies[0] is HuskEnemy) == (name == "HuskPractice"), $"{name}: intended enemy scene");
            var mode = name == "HuskPractice" ? EnemyBase.TargetingMode.Player
                : name == "SmallGroup" ? EnemyBase.TargetingMode.Closest : EnemyBase.TargetingMode.AllySpirit;
            Check(_encounter.Enemies[0].Targeting == mode, $"{name}: intended main enemy targeting");
            var hud = _game.GetNode<GameHud>("GameHud");
            var panel = hud.GetNode<PanelContainer>("HUDRoot/CombatDebugPanel");
            var readout = panel.GetNode<Label>("Readout");
            hud._Process(0);
            Check(!panel.Visible, $"{name}: debug is off by default");
            hud.ShowCombatDebug = true;
            hud.DebugEnemyNumber = count;
            hud._Process(0);
            Check(panel.Visible && readout.Text.Contains($"Enemy {count} / {count}: {_encounter.Enemies[count - 1].Name}")
                && readout.Text.Contains("Bat: Idle") && readout.Text.Contains("Encounter: Exploration"),
                $"{name}: debug reads selected roster member and player state");
            Check(panel.MouseFilter == Control.MouseFilterEnum.Ignore && readout.MouseFilter == Control.MouseFilterEnum.Ignore,
                $"{name}: debug does not consume mouse input");
            hud.DebugEnemyNumber = 999;
            hud._Process(0);
            Check(readout.Text.Contains("Enemy 999 /") && readout.Text.Contains("unavailable"), $"{name}: invalid debug selection handled");
            hud.DebugEnemyNumber = 1;
            var actors = _encounter.Allies.Cast<CombatantBase>().Concat(_encounter.Enemies).ToArray();
            var starts = actors.Select(a => a.GlobalPosition).ToArray();
            for (int cycle = 0; cycle < 2; cycle++)
            {
                await BeginCombat();
                await Frames(20);
                var target = _encounter.Enemies[0].CurrentTarget;
                hud._Process(0);
                Check(readout.Text.Contains("Encounter: Combat") && readout.Text.Contains($"Phase: {_encounter.Enemies[0].Phase}")
                    && _encounter.Enemies[0].CurrentTarget == target, $"{name}: live debug observes without retargeting");
                ExitWithEnd();
                await Until(() => _encounter.State == CombatEncounter.EncounterState.Exploration, 180);
                Check(actors.All(a => !a.IsCombatActive && a.Health == a.MaxHealth), $"{name}: cycle {cycle + 1} resets health/activity");
                Check(actors.Select((a, i) => a.GlobalPosition.DistanceTo(starts[i]) < 0.1f).All(v => v),
                    $"{name}: cycle {cycle + 1} restores markers");
            }
            hud.ShowCombatDebug = false;
            hud._Process(0);
            Check(!panel.Visible, $"{name}: debug can be hidden again");
        }
    }

    private void ExitWithEnd()
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.End, Keycode = Key.End, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.End, Keycode = Key.End, Pressed = false });
    }

    private async Task Until(Func<bool> predicate, int frames)
    {
        while (!predicate() && frames-- > 0) await Frames(1);
        if (!predicate()) throw new Exception($"Timed out in {_encounter.State}");
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        _checks++;
        GD.Print("PASS: " + message);
    }

    private static object Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
    private static void SetField(object value, string name, object fieldValue) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, fieldValue);
}
