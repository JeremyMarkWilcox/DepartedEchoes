# Spirit family prototypes

Use family names for scenes, with roles described separately:

| Family | Role | Scene |
| --- | --- | --- |
| Guardian | Melee / tank | `Scenes/Characters/Spirit/GuardianSpirit.tscn` |
| Channeler | Ranged / glass cannon | `Scenes/Characters/Spirit/ChannelerSpirit.tscn` |
| Trickster | Agile disruptor | Planned; no scene or behavior yet |

These are working family names, not final creature names. Distraction is a command/ability rather than a family name. Evolution forms can later use `<Family>Form01.tscn`, `Form02`, and `Form03` without requiring separate behavior scripts for each form.

Both existing family scenes inherit `SpiritBase.tscn`, which holds the common body, visuals references, and melee defaults. Guardian currently uses those defaults. Channeler attaches `ChannelerSpirit.cs`, a subclass of `SpiritBase`, so health, Brave, hesitation, and command responses remain shared. Only ranged positioning and projectile attacks differ.

The game still contains a scene instance named `AllySpirit`; it now instances Channeler. To test Guardian, replace that instance with `GuardianSpirit.tscn` and retain the `AllySpirit` node name so Game and CombatEncounter references remain valid. Preserve the desired per-instance Brave/hesitation settings. There is no party-selection system yet.

## Channeler prototype rules

- Approaches toward a preferred distance of 6 metres, with an 8-metre firing range. May begin winding up as soon as it enters firing range. Does not automatically flee from close threats yet.
- Cyan sphere placeholder, 20 health, 6 base damage, 0.4-second windup, 1.4-second attack cooldown. These are testing values, not final balance.
- Fires a visible straight projectile at the target's position at launch. Damage occurs on impact, with Brave's bonus captured at launch. Moving targets can evade shots; bodies and scenery block them. No friendly fire. Shot collision uses a swept centre ray, not the visual sphere's full radius.
- Help me still requires approach and provocation. Keep your distance retreats to 6 metres, suppresses new attacks during its response, and then resumes ranged attacks. An already-fired projectile can finish travelling during a command.
- Projectiles expire after 2 seconds and are removed when their source stops combat, is defeated, or resets. They cannot carry damage into another encounter.
- Player command delivery and its movement-upgrade hook apply to either family unchanged.

This document is local project documentation, not an update to the Google GDD.
