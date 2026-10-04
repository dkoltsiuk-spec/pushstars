# Arena selection

The home MAPS button uses the former spare slot under SHOP. The collection contains the original Crystal Gym and nine supplied locations. Selection is saved as `arena.selected`, changes the PvP home background immediately and highlights its card in gold. Training retains its own home backdrop; boss presentation retains its authored location.

`Resources/ArenaCatalog.asset` contains stable IDs, titles, Home sprites, optional Battle sprites, access type and price. Current entries are free for preview. Purchase/Aura entries require a validated external grant through `ArenaProfile.Grant`; no store purchase or currency deduction is fabricated here. Importing again retains existing access, price and dedicated battle assignments.

On a local ghost match, FightRequest snapshots the selected home arena and the arena saved with the opponent recording. Legacy recordings fall back to Crystal Gym. After READY, identical choices go straight to the battle scene. Differing choices roll once (equal probability), display a short alternating gold-card animation using the supplied panel, settle on the winning card, then load battle. The match snapshot persists across scene loads and resets on leaving or starting another mode. Re-entering a scene cannot reroll the result.

Crystal Gym uses its existing authored split image. Other arenas currently render two crops of the supplied location, split at the existing 0.48 seam, with the player's lower half mirrored horizontally. Assign a dedicated Battle sprite to replace this composition without changing its home art or ID.

Live PvP room transport does not exist in this repository yet. `ArenaMatch.AcceptConfirmed(matchId, playerId, opponentId, winnerId)` accepts one server/room-authoritative result, rejects non-candidates and conflicting replays. A future adapter must send each player's equipped arena ID and resolve the coin once on the authority; it must never call independent local rolls on both phones. This API is an integration boundary, not an implemented multiplayer service.

Validation: Tools > Push Stars > Arenas > Validate And Capture. Captures and report are in output/arenas. Tests cover both coin outcomes, same-map short circuit, one-roll behavior, selection persistence, legacy IDs, confirmed results, mode resets and mobile UI sizes. The real Play Mode flow is verified separately without camera tracking or battle rewards.

## Preparation presentation

Preparation shows the opponent's equipped home arena on top with a red overlay and the player's arena below with a blue overlay. The old moving lightning field is disabled only on this screen. Stats fade/scale in and count to their final values from 0.12–0.94 seconds; unknown values remain dashes. VS begins falling at 1.05 seconds, hits at 1.35 seconds and settles by 1.67 seconds. Closing/interruption restores label values and transforms. MatchFoundImpactValidation checks the ordering and can export a 2.5-second preview with ExportPreview.
