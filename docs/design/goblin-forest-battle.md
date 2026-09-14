# Goblin island battle scenery

The `novice` boss in `Fight.unity` uses the supplied `Group 595.png` backdrop and four transparent foreground PNGs. Preparation and the other bosses retain their existing presentation. Source images are copied intact into `Assets/_Project/UI/Sprites/GoblinForest`; transparent padding is excluded using UV bounds.

HUD and fighter proportions use the existing 390 × 844 `BossCombatScreen.Content` coordinate system. The following centres describe that reference size; props follow viewport edges when the aspect changes:

| Object | Centre x, y | Width × height | Motion |
| --- | --- | --- | --- |
| GrassLeft | -150, -367 | 198 × 183 | Rooted bend, 8 units; speed .95 |
| GrassRight | 154, -367 | 202 × 196 | Mirrored; rooted bend, 8.5 units; speed .84 |
| BranchTopRight | 104, 349 | 230 × 82 | Mirrored, fixed right end; 4.2 units; speed .68 |
| FernStoneMiddleLeft | -141, 54 | 155 × 84 | Bottom 30% fixed, upper foliage bends 2.1 units |

`ForestWindGraphic` subdivides each graphic into a 24 × 16 grid and deforms its vertices at up to 30 updates per second using unscaled time. Motion varies in phase and speed between props. The lower fern band includes the stone and remains fixed. This is procedural mesh animation, not a skeletal rig or an exported animation clip. Adjust Amplitude, Speed, Phase and FixedBase on the graphics in the Inspector.

Foreground renders above avatars but below HUD/buttons, does not intercept raycasts and is clipped to a viewport-sized rectangle. `GoblinForestPresentation.ApplyLayout` receives the viewport in Content coordinates after HUD scaling, independently stretches the forest to fill it, and positions grass relative to the bottom corners and the branch relative to the top right. The fern stays at divider height and follows the left edge. Scenery can change proportions on wide displays; fighters and HUD do not stretch. The divider stays at the centre.

`ForestBackdropGraphic` removes the transparent right-hand export padding through UV mapping. At rounded source corners it extends the nearest opaque edge colour, using conservative alpha scanline bounds baked by the installer into a 32 × 64 mesh. The dark matte is retained as a fallback, rather than visible sidebars.

`ForestDefringe.shader` applies a soft 1.35-source-pixel alpha erosion to the four foreground sprites to hide the white cutout fringe. It supports UI stencil/rectangle clipping. The original PNGs are unchanged. The inset can be adjusted with `_FringePixels` on `ForestDefringe.mat`.

Install/update through `Tools > Push Stars > Boss > Install Goblin Forest`. This patches only the boss layout in the authored Fight scene and first saves a dated backup under `output/goblin-forest`. The health-screen installer also reapplies the forest if textures are present.

`ForestParticleEffects` uses the supplied four leaf images, two loose petal images and whole flower. Up to ten muted leaves drift down with spin and flutter; a new leaf appears every 2.1–3.3 seconds. Eleven petals burst at the boss's torso on a real `BossCombatState.Damaged(true, ...)` event. Player damage never emits petals. On knockout, `BossAvatarPresentation.BeginDefeat()` starts the existing Defeat clip, and a single 28-particle burst appears at the ground partway through the fall. The defeat hold gives the burst 1.3 seconds before Results; other boss/player-loss paths retain their .6-second hold. Fallen boss framing is bottom-aligned to keep him on the platform.

Particles are pooled in 64 non-raycast RawImages below the HUD, reuse the fringe-cleanup material, fade out and respect the adaptive viewport mask. No allocations or object creation are needed for successive hits after initialization, and the effect does not change Unity's gameplay random state. Deactivation clears particles and pending falls. `particles-validation.txt` checks hit filtering, one-shot KO, expiry and pool capacity. `BossCombatPlayValidation` also checks real Update-driven bursts with CV disabled and reward/progression changes suppressed.

Run `BossCombatValidation.Run()` for preview-scene captures at 390 × 844, 320 × 568, 412 × 915 and 768 × 1024 and checks of existing combat bindings. `GoblinForestValidation` additionally checks real generated mesh movement, stationary root vertices, alpha textures and fringe material, viewport coverage and edge anchoring, clipping, UI order, and theme restoration. Wind snapshots are in `output/boss-combat/GoblinForest-wind-a.png` and `GoblinForest-wind-b.png`. Adaptive captures use `Fight-small.png`, `Fight-412x915.png` and `Fight-768x1024.png` in the same output folder.
