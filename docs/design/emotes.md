# Emotes

Paid and free avatar emotes, Clash Royale-style: a round emote button on the pre-fight card
(match found) and on the one-screen fight result opens a panel of the emotes the player owns;
a tap plays the emote on the player's hero with a short stinger and puts the button on a ~2.4 s
cooldown. The ghost/bot opponent answers (a wave for a wave, a laugh for a taunt) and opens
with a taunt or GG on the result screen — sometimes with a paid emote, which is where players
first see the paid moves.

## Catalog

| id | name | rarity | gems | clip |
|---|---|---|---|---|
| hello | HELLO | free | – | mocap: high reach, wave beside the head |
| laugh | HA-HA | free | – | Mixamo Laughing (1.6–5.2 s) |
| loser | LOSER | free | – | mocap: "L" on the forehead, other hand points at the viewer |
| boo | BOO | free | – | mocap: arm swings out, thumb-down fist raised beside the head |
| you | YOU! | free | – | mocap: fist to the chest, then a point at the viewer |
| gg | GG | free | – | mocap: bicep flex, then thumb up in front of the chest |
| flex | FLEX | rare | 80 | Mixamo Mutant Flexing Muscles |
| warmup | WARM-UP | rare | 80 | Mixamo Warming Up |
| hiphop | HIP-HOP | rare | 100 | Mixamo Hip Hop Dancing (5 s) |
| threat | COME AT ME | rare | 120 | Mixamo Threatening |
| snake | SNAKE | epic | 200 | Mixamo Snake Hip Hop Dance (5 s) |
| giddyup | GIDDY-UP | epic | 250 | Mixamo "Gangnam Style" — renamed, no third-party name in the app |
| backflip | BACKFLIP | legendary | 450 | Mixamo Backflip |

Ids are saved in the wallet — never rename a shipped id. Prices, start/length and the
silhouette pose time live in the `Table` in `Assets/_Project/Editor/EmoteSetup.cs`.

## Shop

EMOTES section under the gem packs (`ShopEmoteSection`, built at runtime). Each card shows a
black silhouette of the **player's currently selected hero** frozen in the emote's key pose —
the shop sells the move, not a hero. Tapping a card opens a preview where the hero on the home
stage performs the emote on loop, with the buy button (gems). Purchases go through the case
ledger (`CaseRewards.TryBuyEmote`), which saves the spend and ownership together.

## How it works

- `EmotePlayer` plays a humanoid clip in its own PlayableGraph, output sorted after the
  Animator's controller and blended over it by weight. No controller needs an emote state; the
  controller keeps running underneath, so the fade-out lands on the live idle/victory pose.
- `EmoteThumbnails` clones the live hero under an inactive holder (no scripts wake), poses it
  with a one-frame playable and renders an orthographic silhouette, cached per hero.
- `EmoteBar` is the button + panel; `PreparationScreen` and `RewardScreen` attach it with one call.
- Free gestures are **motion-captured** from the owner's reference videos of our hero
  (Higgsfield, green screen): MediaPipe body + hand landmarks per frame, retargeted onto the
  humanoid by a muscle solve, legs from the idle with the hips kept planted. Pipeline and tuning
  notes: `Tools/EmoteMocap/README.md`.
- Stingers are synthesized (`AudioSource/Emotes/build_emote_sfx.py`, instrumental only) into
  `Assets/_Project/Art/Emotes/Sounds/emote_<id>.wav`, RMS-matched.

## Rebuild

1. `python AudioSource/Emotes/build_emote_sfx.py` (sounds); gesture tracks per `Tools/EmoteMocap/README.md`
2. `Tools ▸ Push Stars ▸ Emotes ▸ Build Emotes` (import clips, author gestures, write
   `Resources/EmoteCatalog.asset`)
3. `Tools ▸ Push Stars ▸ Emotes ▸ Render Contact Sheet` → `Temp/EmoteSheet.png` for review.

Adding a paid emote: drop the Mixamo FBX (without skin) into `Assets/_Project/Art/Emotes/Clips`,
add a `Table` row and a cue in the sound script, rebuild.
