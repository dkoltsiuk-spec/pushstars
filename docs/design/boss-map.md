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
five BossCatalog nodes, a gem milestone and a chest. Green means defeated, yellow
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

The backdrop is now a texture-free `BossMapBackdrop` UI mesh. It samples coherent
noise, edge shading and soft elliptical lights in map coordinates; scrolling moves
the atmosphere with the islands rather than switching a full-screen image. Each
chapter's `BossMapBiome` defines background, mist, light color, transition height
and light anchors. Adjacent chapter palettes blend with SmoothStep across a
620-reference-unit boundary. The background does not intercept input and only
rebuilds while its view changes. Returning home restores the forest atmosphere.

`IceChapter` follows the forest with the supplied ice island, platform and face
PNGs. It is explicitly a COMING SOON preview and adds no playable opponent or
placeholder 3D model to BossCatalog. Its animated combat model can be connected
later. Install/update via Tools/Push Stars/Main/Install Boss Map Biomes, or the
existing map installer. `BossMapEnvironmentSetup.InstallAndValidate` is the batch
entry point. Validation additionally checks winter colors, continuity, art,
non-interactive ice preview and captures the transition and return to forest.
