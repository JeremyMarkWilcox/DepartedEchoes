# Enemy targeting prototype

The enemy currently exposes one `Targeting` dropdown and a target commitment duration. The available modes are:

- `FixedTarget` — the encounter's primary target, currently the player.
- `Player` — prefer the player.
- `AllySpirit` — prefer the allied spirit.
- `Closest` — choose the nearest valid player or ally when reconsidering.
- `LastAttacker` — prefer the most recent actor attributed damage to.
- `LowestHealth` — choose the lowest current-health percentage when reconsidering.

The default is `Player`, with a three-second commitment. The enemy does not reconsider every frame. A fallen target can be replaced immediately when `RetargetAfterTargetDefeated` is enabled. The existing Help me distraction temporarily overrides the selected mode and returns to the selected rule when it expires.

These are prototype rules. Behavior profiles may later cycle through them, but cycling is intentionally not implemented yet. Berserkers, fast enemies, and other special cases can receive different commitment or retarget settings later.

Targeting cues remain visible above the enemy during combat, including the current target and attack phase.
