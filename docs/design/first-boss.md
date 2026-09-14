# First forest boss

The first existing ladder entry (`novice`) uses the supplied rigged Orc Idle FBX.
Its textured toon prefab is `Assets/_Project/Resources/Bosses/novice.prefab`.
The player's selected character is retained. Boss preparation and battle use a
dedicated HP layout; PvP and training retain their own existing layouts.

## Health combat

- First boss: 450 HP. Player: 1000 HP. Later bosses currently default to 1000 HP.
- Each accepted live push-up deals `70 + round(30 * clamp(form / 100))` damage.
  Perfect form wins in 5 reps, minimum credited form in 7, midrange in 6.
  Rejected repetitions, setup/countdown reps and post-KO reps do not deal damage.
- Each attack on the existing boss timeline deals 75 damage. No random criticals.
- Zero HP locks further damage and repetition credit immediately. The finishing
  hit remains visible for 0.6 seconds before the existing results/reward flow.
- At 60 seconds remaining HP decides the result; equal HP is a draw.
- Rewards and ladder advancement still run once through the existing finish
  path. The preparation card displays the actual +50 win bonus, not the mockup's
  illustrative +570; per-rep XP remains additional.

The UI includes animated red HP bars, five difficulty stars, live rep/form/tempo
readouts, pooled floating damage numbers and a READY gate before body arming.
Portraits preserve aspect ratio. The player's HP icon retains the preparation
head snapshot so it does not turn into the top of their hair during a push-up.

`BossCombatSceneSetup` installs only the two additional boss-mode layers.
`BossCombatValidation` checks pure combat rules, scene bindings and portrait
renders; `BossCombatPlayValidation` checks real preparation/HOME/battle wiring
and KO with CV disabled and rewards suppressed. Reports: `output/boss-combat/`.

Animation mapping:

- Orc Idle: standing idle.
- Warming Up: preparation entrance, then standing idle.
- Bouncing Fight Idle: live battle idle.
- Punching / Kicking: alternating attacks on boss reps.
- Taking Punch / Reaction: alternating reactions on player reps during battle.
- Sweep Fall: defeated boss on the result screen, holding the final pose.
- Victory (1), then Laughing: boss win sequence, then back to standing idle.

All ten clips use valid Humanoid avatars, disabled root motion and baked root
transforms. Idle clips loop; actions return to idle. Victory and laughter play once
each, in sequence, only when the boss wins; a draw stays in standing idle.

The material inherits MainMan's Character Toon surface and black outline. The
outline compensates for the FBX mesh scale. The three screens reuse their existing
player-matched stage lights. FightRequest snapshots the boss ID so winning and
advancing the ladder cannot swap the defeated boss for the next one on Results.

Editor tools: Tools / Push Stars / Boss. Import First Orc builds the controller,
prefab and targeted opponent bindings without rebuilding any screen. Validation
renders all animations and tests boss/PVP selection in preview scenes without
starting camera tracking or awarding rewards. Outputs: `output/first-boss/`.
