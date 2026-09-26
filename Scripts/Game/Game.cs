using Godot;
using System.Collections.Generic;

public partial class Game : Node3D
{
    public enum AllyFamily { UseCurrentScene, Guardian, Channeler, Trickster }

    [ExportGroup("Spirit Family Testing")]
    [Export] private AllyFamily _allyFamily = AllyFamily.UseCurrentScene;
    [Export] private PackedScene _guardianSpiritScene = null!;
    [Export] private PackedScene _channelerSpiritScene = null!;
    [Export] private PackedScene _tricksterSpiritScene = null!;

    [ExportGroup("Enemy Testing")]
    /// <summary>Optional main-enemy replacement. Keeps its marker and authored targeting mode.</summary>
    [Export] private PackedScene _enemySceneOverride;

    [ExportGroup("Scene Connections")]
    [Export] private Player _player = null!;
    [Export] private CameraRig _cameraRig = null!;
    [Export] private Marker3D _playerSpawn = null!;
    [Export] private SpiritBase _allySpirit = null!;
    [Export] private Marker3D _allySpiritStart = null!;
    [Export] private EnemyBase _enemySpirit = null!;
    [Export] private Marker3D _enemySpiritStart = null!;
    [Export] private GameHud _gameHud = null!;
    [Export] private CombatEncounter _combatEncounter = null!;

    [ExportGroup("Encounter Participants")]
    [Export] private Node3D _additionalSpawns = null!;
    private readonly List<(CombatantBase Actor, Marker3D Spawn)> _additionalParticipants = new();

    public override void _EnterTree()
    {
        SwapConfiguredAllyFamily();
        SwapConfiguredEnemy();
    }

    private void SwapConfiguredEnemy()
    {
        if (_enemySceneOverride == null) return;
        var container = GetNodeOrNull<Node3D>("ActorContainer");
        var original = container?.GetNodeOrNull<EnemyBase>("EnemySpirit");
        Node replacement = _enemySceneOverride.Instantiate();
        if (original == null || replacement is not EnemyBase enemy)
        {
            GD.PushWarning("Game: enemy override requires an EnemyBase scene and an existing EnemySpirit.");
            replacement.Free();
            return;
        }
        int index = original.GetIndex();
        enemy.Targeting = original.Targeting;
        container.RemoveChild(original);
        original.Free();
        enemy.Name = "EnemySpirit";
        container.AddChild(enemy);
        container.MoveChild(enemy, index);
        _enemySpirit = enemy;
    }

    public override void _Ready()
    {
        AssembleParticipants();
        _gameHud?.ConnectCombatDebug(_player, _combatEncounter);
        PlaceActorAtSpawn(_player, _playerSpawn, "Player");
        PlaceActorAtSpawn(_allySpirit, _allySpiritStart, "AllySpirit");
        PlaceActorAtSpawn(_enemySpirit, _enemySpiritStart, "EnemySpirit");
        ConnectCameraAndPlayer();

        _gameHud?.SetStateText("Exploration");
        _gameHud?.SetReadyVisible(false);

        if (_combatEncounter != null)
        {
            _combatEncounter.EncounterReset += OnEncounterReset;
        }

        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void SwapConfiguredAllyFamily()
    {
        PackedScene selectedScene = _allyFamily switch
        {
            AllyFamily.Guardian => _guardianSpiritScene,
            AllyFamily.Channeler => _channelerSpiritScene,
            AllyFamily.Trickster => _tricksterSpiritScene,
            _ => null,
        };

        if (selectedScene == null)
        {
            return;
        }

        Node3D actorContainer = GetNodeOrNull<Node3D>("ActorContainer");
        Node oldAlly = actorContainer?.GetNodeOrNull("AllySpirit");
        if (actorContainer == null || oldAlly == null)
        {
            GD.PushWarning("Game: cannot swap ally family; ActorContainer/AllySpirit is missing.");
            return;
        }

        Node replacement = selectedScene.Instantiate();
        replacement.Name = "AllySpirit";
        int childIndex = oldAlly.GetIndex();
        actorContainer.RemoveChild(oldAlly);
        oldAlly.Free();
        actorContainer.AddChild(replacement);
        actorContainer.MoveChild(replacement, childIndex);

        _allySpirit = replacement as SpiritBase;
        if (_allySpirit == null)
        {
            GD.PushError($"Game: selected ally scene '{selectedScene.ResourcePath}' is not a SpiritBase.");
        }
    }

    public override void _ExitTree()
    {
        if (_combatEncounter != null)
        {
            _combatEncounter.EncounterReset -= OnEncounterReset;
        }
    }

    /// <summary>
    /// The encounter decides when a reset is safe; putting the bodies back is still Game's job.
    /// The Player keeps whatever exploration position they returned to.
    /// </summary>
    private void OnEncounterReset()
    {
        PlaceActorAtSpawn(_allySpirit, _allySpiritStart, "AllySpirit");
        PlaceActorAtSpawn(_enemySpirit, _enemySpiritStart, "EnemySpirit");
        foreach (var entry in _additionalParticipants)
            PlaceActorAtSpawn(entry.Actor, entry.Spawn, entry.Actor.Name);
    }

    private void AssembleParticipants()
    {
        var allies = new List<SpiritBase> { _allySpirit };
        var enemies = new List<EnemyBase> { _enemySpirit };
        Node container = _player.GetParent();
        if (_additionalSpawns != null)
        {
            foreach (Node node in _additionalSpawns.GetChildren())
            {
                if (node is not EncounterSpawn slot || !slot.Enabled) continue;
                if (slot.ParticipantScene == null)
                {
                    GD.PushWarning($"Game: enabled spawn {slot.Name} has no participant scene.");
                    continue;
                }
                Node instance = slot.ParticipantScene.Instantiate();
                if (instance is not SpiritBase && instance is not EnemyBase)
                {
                    GD.PushWarning($"Game: {slot.Name} requires a spirit or enemy scene.");
                    instance.Free();
                    continue;
                }
                instance.Name = slot.Name;
                container.AddChild(instance);
                var actor = (CombatantBase)instance;
                PlaceActorAtSpawn(actor, slot, slot.Name);
                _additionalParticipants.Add((actor, slot));
                if (actor is SpiritBase ally) allies.Add(ally);
                else enemies.Add((EnemyBase)actor);
                _cameraRig?.AddExcludedBody(actor);
            }
        }
        _combatEncounter?.ConfigureParticipants(allies.ToArray(), enemies.ToArray());
    }

    /// <summary>
    /// Game owns mouse capture, since it is a whole-session concern rather than a camera or
    /// encounter one. Escape releases the cursor, a left click recaptures it. Neither is bound
    /// to a gameplay or encounter action, so freeing the mouse cannot trigger an encounter.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel") && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (Input.MouseMode != Input.MouseModeEnum.Captured
            && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Game owns actor placement: the spawn marker drives both position and facing.
    /// </summary>
    private static void PlaceActorAtSpawn(ActorBase actor, Marker3D spawn, string actorName)
    {
        if (actor == null)
        {
            GD.PushError($"Game: {actorName} is not assigned.");
            return;
        }

        if (spawn == null)
        {
            GD.PushWarning($"Game: {actorName} spawn marker is not assigned; leaving it at its authored position.");
            return;
        }

        actor.GlobalTransform = spawn.GlobalTransform;
        actor.Velocity = Vector3.Zero;
    }

    private void ConnectCameraAndPlayer()
    {
        if (_cameraRig == null || _player == null)
        {
            GD.PushWarning("Game: camera rig or player missing; camera follow is disabled.");
            return;
        }

        Node3D anchor = _player.CameraAnchor ?? _player;
        _cameraRig.SetTarget(anchor, _player);
        _player.SetCameraReference(_cameraRig.YawPivot);

        // The encounter view sweeps over the spirits to frame them; without this the spring arm
        // can catch on their bodies and pull the camera in part-way through the blend.
        _cameraRig.AddExcludedBody(_allySpirit);
        _cameraRig.AddExcludedBody(_enemySpirit);
    }
}
