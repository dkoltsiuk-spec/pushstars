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
three BossCatalog nodes, a gem milestone and a chest. Green means defeated, yellow
means the current boss, grey means locked. Only the current boss starts a fight;
other nodes explain their state. Reward markers are previews, with no currency
grant until their reward definitions are supplied.

Append authored chapter RectTransforms under BossMap/MapViewport/Chapters to extend
the map upward. Each chapter is 390 reference units wide, bottom anchored/pivoted,
with its own height. BossMapController stacks and scales them, and its Nodes array
binds each boss index to its UI. Additional boss profiles must be added to BossCatalog
alongside later islands. The existing last-boss repeat behavior is unchanged.

All objects and sprite references are serialized in Main. The additive installer is
Tools/Push Stars/Main/Add Boss Island and Map. Source images live in
Assets/_Project/UI/Sprites/BossMap; their original PNGs are preserved.
Validation outputs are in output/boss-map, with preview and Play Mode validation
available under Tools/Push Stars.
