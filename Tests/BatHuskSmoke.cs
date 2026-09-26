using System;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

/// <summary>Headless simulation and injected-input checks; not an editor shortcut or visual test.</summary>
public partial class BatHuskSmoke : Node
{
    private Game _game;
    private Player _player;
    private HuskEnemy _husk;
    private CombatEncounter _encounter;
    private int _checks;

    public override async void _Ready()
    {
        if (DisplayServer.GetName() != "headless") { GetTree().Quit(1); return; }
        try
        {
            _game = GD.Load<PackedScene>("res://Scenes/Game/Game.tscn").Instantiate<Game>();
            typeof(Game).GetField("_enemySceneOverride", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_game, GD.Load<PackedScene>("res://Scenes/Characters/Enemy/HuskEnemy.tscn"));
            AddChild(_game);
            _encounter = _game.GetNode<CombatEncounter>("CombatEncounter");
            _player = _game.GetNode<Player>("ActorContainer/Player");
            _husk = (HuskEnemy)_encounter.Enemies[0];
            await Frames(10);
            KeyEvent(Key.J);
            Check(!_player.IsSwingingBat, "exploration rejects bat input");
            Check(_player.LastBatFeedback == "Blocked: combat inactive", "inactive press explains rejection");
            _encounter.BeginEncounter();
            KeyEvent(Key.J);
            Check(!_player.IsSwingingBat, "Setup rejects bat input");
            await Until(() => _encounter.State == CombatEncounter.EncounterState.Ready);
            KeyEvent(Key.J);
            Check(!_player.IsSwingingBat, "Ready rejects bat input");
            await Until(() => _encounter.State == CombatEncounter.EncounterState.Combat);
            _encounter.Allies[0].StopCombat(); // Isolate player and enemy behavior, not a full fight test.
            _husk.Targeting = EnemyBase.TargetingMode.Player;
            await Arrange(2);
            float health = _husk.Health;
            KeyEvent(Key.J, echo: true);
            Check(!_player.IsSwingingBat, "keyboard echo cannot start swing");
            KeyEvent(Key.J);
            Check(_player.CurrentBatPhase == Player.BatPhase.Anticipation, "fresh J starts anticipation");
            KeyEvent(Key.Space);
            Check(!_player.IsDodging, "bat blocks dodge");
            await Until(() => _husk.IsStaggered);
            Check(_husk.Health == health, "bat staggers without health damage");
            Check(_player.LastBatFeedback == "Hit check: 1 staggered", "successful hit reports stagger");
            var hud = _game.GetNode<GameHud>("GameHud");
            hud.ShowCombatDebug = true;
            hud._Process(0);
            Check(hud.GetNode<Label>("HUDRoot/CombatDebugPanel/Readout").Text.Contains(_player.LastBatFeedback), "HUD presents actual hit result");
            float playerHealth = _player.Health;
            await Until(() => !_player.IsSwingingBat);
            Check(_player.Health == playerHealth, "interrupted windup never lands");
            await Frames(10);
            Check(!_player.IsSwingingBat, "swing does not repeat without a fresh press");

            foreach (float distance in new[] { -2f, 5f })
            {
                await Arrange(distance);
                _husk.SetPhysicsProcess(false);
                KeyEvent(Key.J);
                await Frames(15);
                Check(!_husk.IsStaggered, distance < 0 ? "bat excludes targets behind" : "bat excludes distant targets");
                Check(_player.LastBatFeedback.Contains(distance < 0 ? "not in front" : "out of reach"), "miss reports actual geometry rejection");
                await Until(() => !_player.IsSwingingBat);
                _husk.SetPhysicsProcess(true);
            }

            await Arrange(2);
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.RightShoulder, Pressed = true });
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.RightShoulder, Pressed = false });
            Input.FlushBufferedEvents();
            Check(_player.IsSwingingBat, "injected right shoulder follows bat path");
            _player.ApplyDamage(1);
            Check(!_player.IsSwingingBat, "taking damage cancels bat");
            Check(_player.LastBatFeedback == "Cancelled before hit check", "interrupted anticipation explains cancellation");

            await Arrange(2);
            _husk.SetPhysicsProcess(false);
            _husk.TryStagger(_player, 1);
            KeyEvent(Key.J);
            await Frames(15);
            Check(_player.LastBatFeedback == "Hit check: 1 already staggered", "existing stagger distinguished from a miss");
            await Until(() => _player.CurrentBatPhase == Player.BatPhase.Recovery);
            KeyEvent(Key.J);
            Check(_player.LastBatFeedback == "Blocked: recovering", "recovery press explains rejection");
            await Frames(310);
            Check(_player.LastBatFeedback == "", "feedback expires without another swing");
            _husk.SetPhysicsProcess(true);

            await Arrange(2);
            await Until(() => _husk.IsWindingUp);
            Check(!_husk.IsLungeAttack, "Husk opens with overhead");
            playerHealth = _player.Health;
            await Until(() => _husk.Phase == EnemyBase.AttackPhase.Recovery);
            Check(_player.Health == playerHealth - 10, "overhead applies one hit");
            await Until(() => _husk.IsLungeAttack && _husk.IsWindingUp);
            Vector3 committed = _husk.StrikeForward;
            _player.GlobalPosition += Vector3.Right * 4;
            await Until(() => _husk.Phase == EnemyBase.AttackPhase.Strike);
            Vector3 start = _husk.GlobalPosition;
            playerHealth = _player.Health;
            await Until(() => _husk.Phase == EnemyBase.AttackPhase.Recovery);
            Vector3 travel = _husk.GlobalPosition - start;
            travel.Y = 0;
            Check(travel.Length() > 2 && travel.Length() <= 4.1f, "lunge moves with bounded travel");
            Check(_husk.StrikeForward.IsEqualApprox(committed), "lunge direction stays committed");
            Check(_player.Health == playerHealth, "stepping outside lunge avoids damage");

            await Arrange(2);
            await Until(() => _husk.IsLungeAttack && _husk.IsWindingUp);
            _player.GlobalPosition = _husk.GlobalPosition + _husk.StrikeForward * 3.5f;
            playerHealth = _player.Health;
            await Until(() => _husk.Phase == EnemyBase.AttackPhase.Strike);
            Check(_player.Health == playerHealth, "lunge begins before distant impact");
            await Until(() => _husk.Phase == EnemyBase.AttackPhase.Recovery);
            Check(_player.Health == playerHealth - 10, "moving lunge applies exactly one contact hit");

            await Arrange(2);
            await Until(() => _husk.IsLungeAttack && _husk.IsWindingUp);
            _player.GlobalPosition = _husk.GlobalPosition + _husk.StrikeForward * 3.5f;
            await Until(() => _husk.Phase == EnemyBase.AttackPhase.Strike);
            await Frames(3);
            Check(_husk.TryStagger(_player, 0.55f), "active lunge accepts interruption");
            start = _husk.GlobalPosition;
            playerHealth = _player.Health;
            await Frames(20);
            Check(_husk.GlobalPosition.DistanceTo(start) < 0.05f && _player.Health == playerHealth,
                "stagger stops lunge travel and pending damage");

            await Arrange(2);
            _husk.SetPhysicsProcess(false);
            var wall = new StaticBody3D { Position = new Vector3(10, 1, -1) };
            wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4, 3, 0.1f) } });
            _game.AddChild(wall);
            await Frames(3);
            KeyEvent(Key.J);
            await Frames(15);
            Check(!_husk.IsStaggered, "wall blocks bat hit");
            Check(_player.LastBatFeedback == "Hit check: 1 obstructed", "wall obstruction reported");
            wall.QueueFree();
            await Until(() => !_player.IsSwingingBat);
            _husk.SetPhysicsProcess(true);

            await Arrange(2);
            _player.SetCommandDeliveryActive(true);
            KeyEvent(Key.J);
            Check(!_player.IsSwingingBat, "command delivery blocks bat");
            Check(_player.LastBatFeedback == "Blocked: delivering command", "command hold explained");
            _player.SetCommandDeliveryActive(false);
            KeyEvent(Key.Escape);
            KeyEvent(Key.J);
            Check(_player.IsSwingingBat, "bat works after mouse release");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            Input.FlushBufferedEvents();
            Check(_player.CurrentBatPhase == Player.BatPhase.Anticipation, "recapture does not restart or cancel bat");

            foreach (Player.BatPhase phase in new[] { Player.BatPhase.Anticipation, Player.BatPhase.Swing, Player.BatPhase.Recovery })
            {
                await Arrange(2);
                KeyEvent(Key.J);
                await Until(() => _player.CurrentBatPhase == phase);
                KeyEvent(Key.End);
                Check(_encounter.State == CombatEncounter.EncounterState.Returning && !_player.IsSwingingBat && !_husk.IsCombatActive,
                    $"End cancels bat during {phase}");
                KeyEvent(Key.End);
                await Until(() => _encounter.State == CombatEncounter.EncounterState.Exploration);
                Check(_husk.Health == _husk.MaxHealth && !_husk.IsLungeAttack && !_husk.IsStaggered, "return clears Husk runtime state");
                Check(_player.LastBatFeedback == "", "encounter reset clears old bat feedback");
                _encounter.BeginEncounter();
                await Until(() => _encounter.State == CombatEncounter.EncounterState.Combat);
                _encounter.Allies[0].StopCombat();
                Check(!_player.IsSwingingBat && _player.Health == _player.MaxHealth, "next encounter has no old bat action");
            }
            GD.Print($"BAT/HUSK CHECKS PASSED: {_checks}");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }

    private async Task Arrange(float distance)
    {
        _player.ResetForEncounter();
        _player.StartCombat();
        _player.SetMovementEnabled(true);
        _player.GlobalPosition = new Vector3(10, 1.05f, 0);
        ((Node3D)typeof(Player).GetField("_meshHolder", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_player)).GlobalRotation = Vector3.Zero;
        _husk.ResetForEncounter(_player);
        _husk.GlobalPosition = new Vector3(10, 1.2f, -distance);
        _husk.StartCombat();
        await Frames(8);
    }
    private static void KeyEvent(Key key, bool echo = false)
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true, Echo = echo });
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
        Input.FlushBufferedEvents();
    }
    private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private async Task Until(Func<bool> predicate)
    {
        for (int i = 0; i < 300 && !predicate(); i++) await Frames(1);
        if (!predicate()) throw new Exception($"Timeout: {_encounter.State}, {_husk.Phase}, {_player.CurrentBatPhase}");
    }
    private void Check(bool value, string label)
    {
        if (!value) throw new Exception("FAIL: " + label);
        _checks++; GD.Print("PASS: " + label);
    }
}
