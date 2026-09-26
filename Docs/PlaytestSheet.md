# Departed Echoes playtest sheet

Copy this sheet for each testing session and share the same completed copy with both agents. Leave untested rows marked Not run.

Date / tester: __________  Commit or working-copy description: __________

Godot version: __________  Window size: __________  Keyboard or controller: __________

Preset / spirit changes / tuning changes: __________

## Setup and controls

Open a preset under `Scenes/Game/Presets/` and choose **Run Current Scene**. Run Project opens the original Game scene instead. Stop before switching presets.

- Baseline: Channeler against one ordinary enemy targeting the ally.
- HuskPractice: the saved Ally Family against a Husk targeting the player. This is a normal fight with defeat, not an endless training dummy.
- SmallGroup: three spirits against three enemies. Commands start on the main Channeler; R / LB selects another living spirit.
- Move: WASD / left stick. Exploration orbit: captured mouse / right stick.
- Bat: J / RB (R1). Dodge: Space / A (Cross).
- Help: Q / Y (Triangle). Keep distance: E / X (Square).
- Next command spirit: R / LB (L1). Gold COMMANDS marker and `[commands]` health row identify the recipient. Selection locks during delivery; cooldowns are shared.
- Exit encounter: End / Back (Select). Esc releases the mouse; left click recaptures it.

For diagnostics, select **GameHud → Combat Debug → Show Combat Debug** before running. During a run, use the **Remote** scene tree to change Show Combat Debug or Debug Enemy Number. Enemy numbers match the health rows; 1 is the main enemy. Selection only changes the readout. The display is a live snapshot, not an event log or proof that a hit should have landed. Brief swing phases may pass quickly.

## Scenarios

If a bat attempt seems wrong, record **Last bat event** in the debug panel. It persists for five seconds and counts the whole enemy roster, regardless of the observed enemy number. A newer event replaces it. Try a blocked press during recovery, a swing facing away, and an unobstructed close hit; compare the explanation to what you saw.

| # | Preset / action | Expected behavior | Result (Pass / Fail / Not run) | Observations / bug ID |
| --- | --- | --- | --- | --- |
| 1 | Baseline: start, walk into trigger | Starts in Exploration. Setup holds movement, camera blends, then Ready, then Combat. No early attacks. | Not run | |
| 2 | Baseline: move, orbit, exit, move again | Exploration orbit works; combat view stays elevated and follows group spread. Return restores exploration at the player's current location. | Not run | |
| 3 | HuskPractice: face enemy, press J | Anticipation → Swing → Recovery → Idle. In-range unobstructed enemy staggers without losing health from the bat. Movement is held during swing/recovery. | Not run | |
| 4 | HuskPractice: swing away, out of reach, then hold J | Wrong direction/range misses. Holding does not repeat. Commands and dodge cannot overlap a swing. | Not run | |
| 5 | HuskPractice: interrupt overhead warning and moving lunge | Stagger cancels pending hit/travel. Damage that already landed is not reversed. Enemy eventually resumes. | Not run | |
| 6 | HuskPractice: step out of overhead/lunge warning | Attack stays committed to its warning direction. Lunge moves before contact damage; a successful attack hits once. | Not run | |
| 7 | Any: End during Setup, Ready, swing, lunge and result | Each phase exits cleanly. Repeated End does not restart return. No old hit/Ready callback reactivates combat. Record phases tested. | Not run | |
| 8 | Any: finish fight, leave trigger, re-enter | Health, placement, visuals and attack timing reset. Standing in trigger does not immediately restart. | Not run | |
| 9 | SmallGroup: fight, cycle selection and issue commands | Marker/health row match selected spirit; commands reach that spirit only. Selection locks during delivery, skips fallen spirits, preserves cooldowns, and resets next encounter. Defeating one enemy does not end fight; last enemy or player defeat resolves it. | Not run | |
| 10 | Any: Esc, use keyboard actions, left-click recapture | Mouse release/recapture works without performing a bat swing or encounter exit. Test a real controller separately. | Not run | |
| 11 | Any: debug on/off, change enemy number | Bat/dodge and enemy state reflect action; changing observed enemy does not change its target. Panel remains readable; invalid number says unavailable. | Not run | |
| 12 | SmallGroup: normal and narrow window, spread out/regroup | Camera reframes smoothly. At maximum distance player remains prioritized; distant fighters may leave frame. Check debug/HUD overlap. | Not run | |

## Bug report (copy for each issue)

- ID / short title:
- Preset, spirit family, observed enemy number:
- Exact steps, including controls and whether game had focus:
- Expected / actually happened:
- Debug encounter phase, bat/dodge state, enemy phase and target (if seen):
- Last bat event (if seen):
- Frequency (for example 2 of 5 attempts):
- Screenshot/video or Godot error text, if available:
- Assigned implementer / reviewer:
- Fix and retest result:

## Session notes

Most enjoyable moment: __________

Most confusing or frustrating moment: __________

Timing/reach/camera changes worth trying: __________

Automated checks are separate evidence: a passing build or headless test does not mark these manual scenarios as passed. Direct movement can crowd bodies; friendlies can block bat/projectile line of sight. There is no automatic ally revival yet.
