# Boss island and map

Selecting BOSS replaces the home avatar, friend entry, wardrobe and character glow
with the supplied forest island and a compact progress strip. FIGHT uses the existing
boss preparation route; PUSHUP still opens the existing exercise settings.

The island presses down 5 UI units and shrinks to 94% for 75 ms, then returns over
140 ms before the map opens. OK uses the same feedback before returning home.
Repeated taps are ignored during the press. Disabling the view cancels pending
navigation and restores the exact position/scale.

The map's forest island is 325 x 367.5 reference units (25% larger); nodes above
it move upward to preserve the FIGHT clearance. OK's masked diagonal stripes sweep
left to right for 0.6 seconds every 3 seconds. Two yellow rings expand and fade
behind only the active boss, staggered by 0.8 seconds in a 1.6-second cycle.
Both effects use unscaled time and do not intercept taps.

The map is a vertical ScrollRect starting at its bottom. Its currency HUD is the
existing live HUD, not a duplicated balance. ForestChapter contains the first island,
five BossCatalog nodes, a gem islet and a chest. Green means defeated, yellow
means the current boss, grey means locked. Only the current boss starts a fight;
other nodes explain their state. Rewards are described below.

Append authored chapter RectTransforms under BossMap/MapViewport/Chapters to extend
the map upward. Each chapter is 390 reference units wide, bottom anchored/pivoted,
with its own height. BossMapController stacks and scales them, and its Nodes array
binds each boss index to its UI and `Rewards` binds each island collectible.

All objects and sprite references are serialized in Main. The additive installer is
Tools/Push Stars/Main/Add Boss Island and Map. Source images live in
Assets/_Project/UI/Sprites/BossMap; their original PNGs are preserved.
Validation outputs are in output/boss-map, with preview and Play Mode validation
available under Tools/Push Stars.

The backdrop is now a texture-free `BossMapBackdrop` UI mesh. It samples coherent
noise, edge shading and soft elliptical lights in map coordinates; scrolling moves
the atmosphere with the islands rather than switching a full-screen image. Each
chapter's `BossMapBiome` defines background, mist, light color, transition height
and light anchors. Adjacent chapter palettes blend with SmoothStep across a
620-reference-unit boundary. The background does not intercept input and only
rebuilds while its view changes. Returning home restores the forest atmosphere.

## Islands, rewards and COMING SOON chapters

`BossCatalog.Chapters` defines every island; only playable chapters form the fight ladder
(`BossCatalog.Bosses`). Progress (`boss_progress`) counts defeated ladder bosses, so it can reach
the ladder length: the last boss keeps its FIGHT rematch, and a player who has cleared everything
moves straight to the next island once it becomes playable.

| Island | Bosses | HP | Perfect-form reps | Boss hit | Gem islet | Chest |
|---|---|---|---|---|---|---|
| Forest (playable) | 5 goblins | 450 / 575 / 700 / 825 / 1025 | 5 / 6 / 7 / 9 / 11 | 75 | 50 gems | 1 case |
| Ice (COMING SOON) | 5 golems | 1200 / 1400 / 1500 / 1700 / 1900 | 12 / 14 / 15 / 17 / 19 | 75 | 80 gems | 1 case |
| Lava (COMING SOON) | 6 golems | 2000 … 3000 (+200) | 20 … 30 | 80 | 120 gems | 1 case |

Each island reads bottom to top: bosses 1–3, the gem islet, the remaining bosses, then the main
boss's chest. The gem islet opens after the third boss, the chest after the main (last) boss.
Tapping a ready gem islet credits its gems once (wallet receipt `boss-map:<island>:gems:v1`);
seven gems burst out and fly into the HUD pill, which counts up as they land. Tapping the ready
chest grants one ordinary case (receipt and case ID `boss-map:<island>:chest:v1`, upgradable by
taps like any other) and opens the CASE EARNED screen; "later" keeps it under CASES. Ready rewards
stand on grass and hop in a loop (`RewardBounce`: crouch, stretched jump, landing squash,
unscaled time); locked ones stand on stone, greyed; collected gems disappear and a collected chest
fades. The home island's gem bubble opens the map and hides once the forest gems are collected.

Bosses grant **no trophies** (wins and losses still count in the profile), so rematching the last
boss cannot farm the league. A boss win still adds `BossWinXpBonus` (+50) on top of per-rep XP;
ghost duels no longer receive that bonus. At the 60-second timeout the larger share of remaining
HP wins (bosses have up to 3x the player's HP, so raw HP would favour a barely scratched boss).

`IceChapter` shows its five golems (the king slightly larger), gem islet and chest on ice
platforms as a non-interactive COMING SOON preview; it has no buttons. Lava has catalog data only;
its island art and chapter rect come with the models.

**Making an island playable:** put each boss prefab (and optional `<id>-portrait.png`) in
`Resources/Bosses/` under the IDs in `BossCatalog` (`ice-golem-1` … `ice-golem-king`,
`lava-golem-1` … `lava-golem-king`), set the chapter's `playable` flag, then add its map nodes and
reward nodes (the forest's `BossMapRewardsSetup` layout is the template) and its battle backdrop
(`BossCombatScreen` currently shows the forest backdrop for forest bosses only).

Install/update: Tools/Push Stars/Main/Install Boss Map Rewards (also run by the map installer).
Validate: Tools/Push Stars/Validation/Boss Map Rewards → `output/boss-map/rewards-*.png`.
The procedural backdrop biomes are unchanged (Tools/Push Stars/Main/Install Boss Map Biomes).
