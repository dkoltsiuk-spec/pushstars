# Shop

The upper-right `ShopTile` opens the authored Shop overlay in `Main.unity`.
`Tools > Push Stars > UI > Install Shop` installs it once into a saved Main scene.

- One special offer: Sonic, $5.99. The shared AvatarCatalog marks Sonic as Dollars;
  the collection, premium filter, home roster and fight selection respect ownership.
  Madam Engry and Fighter remain included.
- Offer and info taps open the existing animated avatar preview. Back returns to
  Shop, Home closes to the lobby. Opening the collection directly retains its
  original navigation.
- Three gem packs follow the supplied mockup: 30 / $1.99, 120 / $5.99,
  360 / $5.99. Pack details show the artwork, amount, price and availability.
- Dollar purchases are unavailable until store integration exists. There is no
  simulated payment, currency grant or client-side dollar unlock.
- Content scrolls on shorter phones; Back, Home and OK stay outside the scroll area.
  Assets were copied from the user's supplied Downloads exports to UI/Sprites/Shop.

Gem cards now use `ShopGemCardSurface`: the frame, green field and price band
share one rectangle, so separately skewed PNG edges cannot misalign.
The built-in ImageGen tool produced `UI/Sprites/Shop/Gems30Clean.png`,
`Gems120Clean.png` and `Gems360Clean.png` from the original exports. The shared
prompt requested removal of white hairline seams and star glints while preserving
the number, arrangement, outlines and green facets, with no text or added elements.
The outputs have an RGB neutral matte rather than alpha; `GemSprite.mat` keys that
matte in the UI shader, including scroll clipping and UI tint. Originals remain intact.

Exact built-in ImageGen prompt (one call per original Gems30/120/360 PNG):

> Use case: precise-object-edit. Input image is the edit target: a green gemstone pile sprite for a mobile game shop. Change only the unwanted white/pale hairline seams and starburst sparkle scratches: remove all those white lines and white star glints, filling with the underlying green facet colors. Preserve exactly the number and arrangement of gems, silhouette, dark green outlines, faceted angular shapes, vivid lime/emerald palette and flat cartoon style. Clean smooth antialiased edges, contiguous flat-colored facets with no seams. Do not redesign. Keep the same framing and padding and aspect ratio as input. Genuine transparent RGBA background, no background, no ground, no shadow, no glow, no text. One isolated pile only.

Validation: `ShopValidation.CheckInteractions()` in Play Mode exercises the entry,
shared preview, return routes, pack dialogs and wallet invariance. `Capture()` renders
phone-size previews under `Logs/Shop`. `AvatarUnlockRegression.Run()` checks the
existing wallet and unlock persistence rules without modifying player saves.
