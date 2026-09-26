# Encounter roster prototype

## Optional combat debug display

Select GameHud and enable **Combat Debug → Show Combat Debug** (off by default). **Debug Enemy Number** is one-based and matches the enemy health rows. Both settings can be changed during play using Godot's Remote scene tree. An unavailable number is shown explicitly; the display never switches targets or controls the fight.

The top-right panel shows encounter phase, player activity, bat phase, dodge/protection/grounding, and the observed enemy's health, targeting mode, target and attack phase. Inactive/defeated status distinguishes stopped actors from their reset attack phase. It updates live, has no keyboard shortcut and ignores mouse input. It is not a hit history or a prediction of range/line of sight. Check readability in narrow windows manually.

Use [the shared playtest sheet](../Docs/PlaytestSheet.md) to record scenarios and reproducible reports for both agents.

**Last bat event** remains visible for five seconds of game time after each blocked press, swing start, hit check, or cancellation before impact. Hit results count all roster enemies (independent of Debug Enemy Number): staggered, already staggered, not in front, out of reach, outside height reach, obstructed, or unavailable. Reasons come from the actual hit checks; each enemy reports the first failed check. A newer event replaces the message, keyboard echo does not create one, and encounter reset clears it. This is one retained event, not a complete history. The diagnostic does not alter swing timing or damage.

## Named encounter presets

Open a scene in `Scenes/Game/Presets/` and use Godot's **Run Current Scene** button. Stop the game before switching presets. Run Project still starts the original Game scene.

| Scene | Lineup | Main enemy targeting | Purpose |
| --- | --- | --- | --- |
| Baseline.tscn | Channeler + one ordinary enemy | Ally spirit | Simple reference fight |
| HuskPractice.tscn | Channeler + one Husk | Player | Dodge and bat interruption practice |
| SmallGroup.tscn | Channeler, Guardian, Trickster + three ordinary enemies | Closest | Group readability, targeting and reset checks |

Small Group's two extra enemies retain their scene default of targeting the player. Commands still address the main Channeler. Husk Practice is a normal fight: the ally can defeat the Husk and the player can lose; leave and re-enter for another attempt.

These are inherited Game scenes with configuration overrides, not a new runtime selector. Existing spawn markers, camera, controls and combat tuning are reused. Each preset explicitly selects Channeler and its main enemy scene, and sets all four existing optional slots. To try another main spirit, change the preset root's **Spirit Family Testing → Ally Family**, then run that scene again. To customize the lineup further, use its inherited EncounterSpawns slots. New slots added to the base Game scene will also be inherited; keep them disabled unless wanted in every preset.

The roster smoke test loads each saved preset without changing its settings, verifies the lineup/targeting and startup state, then checks two begin/exit/reset cycles. This uses direct encounter entry and injected End events; actual trigger entry and visual comfort still require editor playtesting.

## Change the test lineup

Open `Scenes/Game/Game.tscn` and expand **EncounterSpawns**. The four optional marker slots start disabled, so the existing one-ally/one-enemy scene remains the default.

- Enable **ExtraGuardian** and/or **ExtraTrickster** to add spirits.
- Enable **ExtraEnemyOne** and/or **ExtraEnemyTwo** to add enemies.
- Change a slot's **Participant Scene** to any existing spirit or enemy scene. Its type determines which side it joins; the slot name is just a label.
- Move the marker to choose its spawn point. Keep body centres above the floor (the current spirits use Y=1 and enemies Y=1.2), leave space between bodies, and stay on the ground plate.
- Duplicate a slot to add another participant. All slots must be direct children of EncounterSpawns. Changes take effect on the next run, not during combat.
- The original ally still uses **Game → Spirit Family Testing → Ally Family**. The original enemy retains its scene-instance targeting configuration; extra enemies use their selected scene's defaults. Use an inherited scene for a separately tuned enemy variant.

This is authoring support for small test groups, not a settled deployment limit. Disabling all extra slots restores the baseline. The main ally and enemy remain required.

## Prototype rules

Game instantiates and places participants once, and restores marker transforms after return. CombatEncounter supplies the roster to targeting, camera and HUD; it starts, stops and resets everyone together. Camera behavior remains unchanged. The bat and optional Husk additions are described below.

Allies initially face the main enemy and acquire the nearest living enemy when their target becomes invalid. Target changes cancel pending melee anticipation and command responses without resetting health. Existing ranged shots keep their original target and cannot transfer damage to a new one. Enemies receive explicit player/ally candidates instead of searching siblings; their targeting modes and committed attack direction remain in force.

Victory requires every enemy to fall. Player defeat ends the fight. Ally defeat does not end it and no automatic revival is added. End/Back stops all actors and their pending actions. Full reset happens only after the return blend.

Commands intentionally still address the main ally (marked `[commands]` in multi-ally health feedback), against that ally's current enemy. If that ally falls, commands remain unavailable; another ally is not silently chosen. Multi-spirit command selection, coordinated attacks, crowd avoidance and revival are separate gameplay decisions.

## Automated verification

Build first:

```powershell
dotnet build "Departed Echoes.csproj" --no-restore
```

Run with a Godot .NET executable, from the project root:

```powershell
& $godot --headless --path . --fixed-fps 60 res://Tests/EncounterRosterSmoke.tscn
```

The test refuses to run interactively. It covers default/1v3/3v3 assembly, startup, Setup/Ready holds, health rows, camera roster membership, partial/final enemy defeat, player defeat, reset, family replacement and retargeting, main-ally commands, all six enemy targeting rules, repeated encounters, and a naturally resolved 3v3 fight. Most cases use direct encounter/damage methods; cancellation uses injected End events. It prints a PASS total and exits nonzero on failure.

Injected input does not verify Godot editor shortcuts. Camera membership checks do not prove visual framing comfort. Tests do not establish balance.

## Manual check before accepting

1. Run the unchanged default lineup; confirm movement/orbit, command delivery, dodge, Esc release and left-click recapture.
2. Enable one extra enemy, then all four slots. Check readable health rows, camera framing, targeting cues, collision crowding and overlapping attack warnings.
3. Check End and controller Back during approach, anticipation and result display. Walk out/re-enter for a second cycle; confirm full health, original positions and clean visuals.
4. Defeat one enemy and confirm others continue; verify the main ally switches targets. Check commands before and after retargeting and while the main ally is down.

Known limits: direct movement can get blocked by other bodies, and friendly bodies still block Channeler shots. Large rosters can exceed the primitive HUD's vertical space. Slots are designed for small prototype groups; no formation logic, team command UI, new abilities, or balance changes are included.

## Bat and Husk prototype

**Controls:** J / right shoulder (RB / R1) swings the bat in the player's current facing direction. It is available only during Combat and while grounded. Each fresh press begins one anticipation, strike and recovery cycle; holding the button does not repeat. Movement, dodging and command delivery cannot overlap the swing. Incoming damage cancels it. End / Back still exits; Esc releases the mouse and left click recaptures it.

The bat deals **zero damage**. At its hit instant it checks each enemy's body against the cyan footprint and line of sight. Bodies and walls can block a hit. A successful hit interrupts the enemy's current attack and displays STAGGERED; an already staggered enemy cannot have its stagger extended. An attack that already landed cannot be undone. Bat visuals are separate from the player's baseline mesh.

**Try the Husk:** select the Game root and assign `Scenes/Characters/Enemy/HuskEnemy.tscn` to **Enemy Testing → Enemy Scene Override**. Empty restores the existing enemy. The override preserves the main enemy's marker and targeting mode. Optional EncounterSpawns slots can also use that scene. No marker moves are required.

The Husk alternates an overhead and a straight lunge. Both commit to their warning direction. The overhead hits once at the end of anticipation; the lunge hits once at close contact after collision-aware movement, with a line-of-sight check. A bat hit interrupts either action, including a moving lunge. These are placeholder warning/mesh effects, not authored attack animations.

### Inspector starting values

| Location | Setting | Value |
| --- | --- | --- |
| Player → Bat | Reach / width | 2.4 / 2.0 units |
| Player → Bat | Anticipation / swing / recovery | 0.16 / 0.12 / 0.45 seconds |
| Player → Bat | Stagger | 0.55 seconds |
| Husk → Husk Attacks | Overhead / lunge windup | 1.0 / 0.8 seconds |
| Husk → Husk Attacks | Lunge distance / speed | 4 units / 8 units per second |
| Husk → Husk Attacks | Lunge recovery | 1.2 seconds |
| Husk scene | Movement / overhead recovery | 3.4 units per second / 1 second |

The Husk inherits the existing enemy's health, damage and strike width/length. Enemy attacks use their phase-specific windup/recovery values; the generic combatant cooldown is not used by this enemy state machine. Values are prototype tuning, not balance decisions.

Player owns swing state, effects and hit checks in PlayerBat.cs; EnemyBase owns stagger and the shared attack phases; HuskEnemy supplies the alternating attack parameters and lunge movement. CombatEncounter supplies the bat roster and uses its existing stop/reset path. Game owns the optional scene replacement; the HUD shows controls. No camera changes or new global system were needed.

### Verification and remaining playtest

```powershell
& $godot --headless --path . --fixed-fps 60 res://Tests/BatHuskSmoke.tscn
```

35 headless checks cover inactive-phase rejection, injected J/right-shoulder/End events, echo rejection, range/direction/wall checks, zero-damage interruption, one-hit overhead/lunge behavior, committed travel, lunge interruption, command/dodge exclusion, mouse-release/recapture events, and exit/reset during each bat phase. Test setup uses direct lifecycle methods and positioning, and pauses the enemy in isolated geometry checks. A separate roster regression run passes 136 checks. Neither suite verifies physical controller delivery, editor shortcuts, animation readability or combat feel.

Manual checklist:

1. Face toward, away from and alongside an enemy; compare cyan footprint and bat reach. Try holding and repeatedly tapping J.
2. With the optional Husk, interrupt both warnings and a moving lunge. Step out of each warning and confirm misses. Check walls stop its movement.
3. Try commands/dodge between swings, then End mid-swing and mid-lunge. Leave and re-enter for a clean fight.
4. Check Esc/left-click and a real controller in the embedded Game view. Check HUD and warning overlap with extra participants.

Known limits: bat geometry is a simple rectangle plus actor radii, and friendlies can block its line of sight. Enemies have no crowd avoidance. Simultaneous events use scene processing order; a bat cannot reverse damage already applied. Swing timing, warning comfort and balance still need human playtesting.
