# Avatar collection

## Catalog heroes (October 2026)

Seven heroes were added as catalog rows that carry their own prefab (`AvatarOffer.Prefab`, a
Resources path). Nothing in a scene references them: the collection clones its last authored
card for every catalog slot past the authored ones, the shop clones its authored offer card for
every listed Gems/Dollars hero (two per row, the sections below move down), and the home and
fight stages load the prefab by the saved catalog slot (`character.homeAvatar` >= 5,
`CharacterRoster.SetHero` / `AvatarCatalog.LoadPrefab`). Slots 0-4 keep their old save values.

| slot | id | unlock |
|---|---|---|
| 5 | bogatyr | Aura 100K, 100 cards |
| 6 | viking | Aura 500K, 500 cards |
| 7 | zombie | 500 gems |
| 8 | tigress | 800 gems |
| 9 | skinny | $6.99 |
| 10 | chubby | $6.99 |
| 11 | skeleton | $9.99 |

Prices live in `Assets/_Project/Resources/AvatarCatalog.asset` (mirrored in
`AvatarCatalog.Defaults`). Dollar heroes show their price but cannot be bought until a store
flow exists. To add a hero: put the rigged FBX at `Assets/Character/<Name>/<name>.fbx` with its
diffuse in `Textures/`, add the catalog row with `Prefab = "Heroes/<Name>"`, and run
`Tools > Push Stars > Character > Import Catalog Heroes` (`HeroRosterSetup`). Aura heroes with
cards also need `Resources/AvatarCollection/<Name>Head.png` (`HeroRosterSetup.RenderHeads`
renders the close-up; the sticker outline is added outside Unity). Collection cards outside the
scroll window pause their preview camera.

Shared-clip fixes for these bodies. The idle every hero rests in is Mixamo's briefcase stand,
whose right hand is a fist: `Tools > Push Stars > Character > Open the idle's fist`
(`MainCharacterSetup.BuildOpenHandIdle`) writes `Assets/Character/Animations/Idle.anim`, a copy
with the left hand's finger curves mirrored onto the right, and repoints the controllers; the
import tools now load that clip for the Idle state. Four prefabs carry `StandingArmSpread`
(Chubby 12, Viking 11, Skinny 8, Tigress 8 degrees; tunable on the prefab and kept across
re-imports), which opens the upper arms while the body is upright and fades out in a plank.

## Aura unlock bars (late September 2026 — supersedes the prices below)

Aura is now a status score that is never spent (docs/design/economy.md §3). Robot and Gladiator
are not bought: each has an Aura goal (`Price` in `AvatarCatalog.asset`: 50 000 and 250 000) and a
bar that fills with the player's **peak** Aura plus that hero's cards at `Price / RequiredCards`
(1K) each. Losing Aura never empties the bar. The card bar reads `42K / 50K`, the footer and the
detail button read `8K <aura> TO GO`, and the detail status says where the Aura comes from. The
detail button is disabled while locked. When a fight, level or card claim fills the bar, the ledger
marks the hero owned in the same save (`CaseRewardLedger.ApplyAura` / `TryClaim`) and the Aura
screen adds a "ROBOT UNLOCKED!" line. Heroes already bought with old Aura stay owned (save v6).
Gems heroes still use their price; `TryBuyAvatar` only spends gems.

## Hero cards and unlock prices (September 2026)

The saved Main scene has five avatars. Sonic, Madam Engry and Fighter remain
included. Robot costs 300 Aura or 60 hero cards; Gladiator costs 600 Aura or
120 cards. These starting values are editable in
`Assets/_Project/Resources/AvatarCatalog.asset`. Stable IDs, rather than translated
names or UI indices, identify saved progress.

For Aura avatars, the remaining price is
`ceil(basePrice * (requiredCards - collectedCards) / requiredCards)`.
Each card currently saves 5 Aura. A complete set unlocks the avatar immediately
when the case is claimed. The progress strip has a horizontal bottom, gently rising
top, rounded corners, black track and solid yellow fill. The supplied head icon
tilts 12 degrees and protrudes beyond the left card edge. An exact card count stays
on the bar; the price uses the existing Aura flame icon instead of a currency word.
The same icon appears on the detail BUY button and balance. A locked card opens
its details; only the detail BUY button spends currency.
After unlocking, SELECT equips the hero on the home stage. Robot is now supported
by CharacterRoster along with Gladiator. FightAvatar still uses the existing
gender-based fight models; retargeting collection skins for combat is separate.

New cases retain their crystal reward and also roll one unowned Aura hero:
Common 1–3 cards, Rare 3–6, Epic 6–10, Legendary 10–20. Drops are capped at the
remaining requirement and persisted on open. Claiming commits crystals, cards,
ownership and case removal together. Already opened old cases keep their original
prizes. If all eligible heroes are owned, cases continue granting crystals only.
Overlapping pending drops are capped on claim; a hero purchased before claiming
its pending cards receives no duplicate unlock or refund.

The offline ledger now stores Aura, crystals, hero progress and receipt IDs in
version 3 under the existing PlayerPrefs key, migrating versions 1 and 2 without
discarding rewards. Aura starts at zero: the old Home value 660 was display data,
not a balance. Existing Aura grants can integrate through
`CaseRewards.TryCreditAura(stableReceiptId, amount)`; earning/server synchronization
and Aura IAP are not connected by this change. Spending and ownership share one
save. No player currency or cards are granted by validation.

Catalog entries can instead be Gems or Dollars. Neither accepts cards nor enters
the case drop pool. Gems uses its fixed price and the crystal balance. Dollar
prices are stored in cents, with a StoreProductId reserved for IAP; purchasing
remains disabled until a real store/receipt flow is connected. No current included
hero has been silently converted to a premium product.

Validation: `AvatarUnlockRegression.Run()` (including existing case regressions)
and `AvatarUnlockPlayValidation.Run()`. Reports: `Logs/avatar-unlocks-regression.txt`
and `output/avatar-collection/unlocks-validation.txt`; phone captures are in that
output directory. `AvatarUnlockSetup.Run()` updates the saved collection in place
and backs up Main first. The older notes below describe earlier iterations.

The home character opens the collection on a tap. A drag continues to rotate the
character and does not open the collection. The saved Main scene owns the UI:
`MainCanvas/AvatarCollection` (with its own safe area). Edit the saved objects in
Unity; the installer preserves an existing collection rather than rebuilding it.

The screen uses the profile blue (#1A47D3), faint lightning, and the supplied
card, footer, and information-button PNGs. Four slots are shown: Sonic preview,
Madam Engry, Fighter, and Robot preview. Opened filters to the two
currently selectable bodies. Premium has an honest coming-soon state; no prices,
purchases, or new unlock conditions have been introduced.

Selecting either existing body uses CharacterRoster and its existing saved
gender. Sonic and Robot are intentionally not equipped on the home or fight stage yet.

## Sonic assets

`Assets/Character/Sonic/Happy Idle.fbx` includes the model and main looping idle.
`Offensive Idle.fbx` imports only the first third of its original take, as the
non-looping OffensiveIntro state. Sonic.prefab uses its own controller and
SonicIdleBehaviour: first accent after 0.5 seconds, 0.35-second crossfades,
then a random 12–19-second interval after each return to idle. Root motion is off.

The source FBX and supplied PNGs were copied from Downloads. Originals are intact.
Card portraits are rendered from the actual prefabs by AvatarCollectionSetup.
Those PNGs now serve only as edit-mode fallbacks. Runtime cards use
AvatarCardPreview: one animated prefab, transparent 512x512 MSAA render target,
and tightly framed camera per visible card. They share Main's existing key/fill
lights and copy its rim-light property block, while retaining the original model
materials. The AvatarCards layer and separated camera positions isolate the
four portraits. No extra lights affect the home stage. Hidden cards stop their
camera and model; closing the screen pauses all four; scene teardown frees the
render textures and temporary objects.

The supplied Ellipse 184.png is stored as AvatarCollection/Glow.png and layered
behind each live portrait, inside the card frame and beneath its footer/title.

Card outlines and footers now use AvatarCardSurface geometry: a consistent dark
rounded border, a four-unit bottom shadow, and a level footer sharing the exact
inner card bounds. This removes the white highlight baked into the supplied frame
and the slanted edge of the supplied footer PNG. The original PNGs and serialized
Image references remain intact; the originals no longer draw over the clean
surfaces. Both surfaces follow selection, and the action label is centered within
the 39-unit footer.

The background now clones the existing Main LightningField (its sprite, drift,
spacing and edge fade), replacing the initial custom bolt mesh. The AVATARS title
is upright Rubik Bold at 22 points. Supplied Group 625 buttons are used for the
collection tabs; Group 624 is used for the preview action.

Each info button opens AvatarPreviewPage, a separate full-body presentation
inside the collection overlay. It shows the correct avatar name, CHARACTER SKIN,
an animated model, cyan glow, floor ellipse, and back/home controls. The four card
cameras stop while the detail camera is visible. Back restores the current
filter; Home closes the overlay. Existing bodies can be selected here; Sonic and
Robot remain previews with COMING SOON. The price in the design reference is not
treated as an actual store product or enabled purchase flow.

Robot uses the same setup and timing in `Assets/Character/Robot/Robot.prefab`.
Its source files are `Happy Idle (1).fbx` (with the robot model) and
`Offensive Idle (1).fbx`, copied from Downloads into the Robot folder under the
standard clip names. `RobotAvatarSetup` imports only Robot and updates the fourth
card in the saved Main scene, preserving the rest of the screen.

## Editor tools

- Tools / Push Stars / Character / Prepare Sonic and Avatar Collection
- Tools / Push Stars / Character / Prepare Robot and Update Collection
- Tools / Push Stars / UI / Install Animated Avatar Cards
- Tools / Push Stars / UI / Fix Avatar Card Edges
- Tools / Push Stars / UI / Install Avatar Preview Page
- Tools / Push Stars / UI / Install Avatar Collection
- Tools / Push Stars / Validate Avatar Collection

The installer updates Main in place and backs up the previous scene under
Library/AvatarCollectionBackup. The Play Mode validator checks tap versus drag,
body selection, Sonic preview isolation, filters, info, home/back, and the first
and subsequent accents for both Sonic and Robot. It restores the original saved gender afterwards.
Its report and phone-size renders are in output/avatar-collection.
The validator also checks that all four live poses change over time and that
hidden cards stop rendering. collection-animated.png is a later animation frame.
