# Departed Echoes development workflow

This is a Godot 4 C# prototype. Keep changes small, inspect the current scene and scripts before editing, and leave all work uncommitted for the project owner to review.

## Ownership boundaries

- `Game` assembles the scene, selects prototype actor scenes, places actors at markers, connects references, and restores authored placement after an encounter.
- `Player` owns exploration movement, camera-relative input, dodge, mouse-independent gameplay input, and player-only actions such as the bat.
- `CameraRig` owns exploration follow/orbit, combat poses, framing, SpringArm collision, and camera blending.
- `CombatEncounter` owns encounter state, transition timing, movement holds, participant activation, outcomes, cancellation, and reset timing.
- `CombatantBase`, `SpiritBase`, and enemy subclasses own participant health, movement, targeting, attack timing, and feedback.
- `GameHud` owns prototype feedback only. Keep it readable and simple.

Do not introduce global managers, signal buses, scene-tree searches in per-frame combat logic, or a general ability framework for a single prototype mechanic. Reuse the existing stop, reset, blend-token, and encounter-generation paths.

## Before editing

1. Check `git status --short`. Existing uncommitted work belongs to the project owner; preserve it and do not reset or discard it.
2. Read the relevant scene and script together. Scene ownership and exported NodePaths are part of the design.
3. Check the current prototype notes in `Docs/`, especially [SpiritFamilies.md](SpiritFamilies.md), [EnemyTargeting.md](EnemyTargeting.md), and [../Tests/README.md](../Tests/README.md).
4. State the implementation plan briefly in the handoff, including any architectural change.

## Scene and tuning conventions

Prefer inspector exports for prototype tuning. Keep authored values separate from runtime state, and reset runtime state explicitly at the encounter reset boundary. Game owns actor transforms; combat scripts should not teleport participants as part of ordinary behavior.

The default scene should remain a useful one-ally/one-enemy test. Extra participant markers and scene overrides are optional test configuration. Name inherited variants clearly and keep the required `Player`, `AllySpirit`, and `EnemySpirit` connections intact.

## Verification

Build from the project root:

```powershell
dotnet build "Departed Echoes.csproj" --no-restore
```

Run the headless integration checks with the Godot .NET console executable:

```powershell
& $godot --headless --path . --fixed-fps 60 res://Tests/EncounterRosterSmoke.tscn
& $godot --headless --path . --fixed-fps 60 res://Tests/BatHuskSmoke.tscn
```

Headless and injected-input checks do not prove visual framing, physical controller delivery, or Godot editor shortcut behavior. If a change involves input, say which checks used direct methods, injected events, and actual editor playtesting. Do not claim editor shortcut verification from synthetic input.

For interactive testing, use the embedded Game view when possible. Do not open a foreground test window or capture keyboard input while the owner may be typing without coordinating first. Record the exact controls and the manual checks still needed.

## Handoff

Report changed files and ownership, inspector starting values, build/test results, manual checks performed, known limitations, and whether any work remains uncommitted. Do not update the Google design document unless the owner explicitly asks for that separate task. The owner handles commits and pushes.
