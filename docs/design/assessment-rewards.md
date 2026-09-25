# Assessment reward

A positive 60-second assessment goes directly to the existing character reward summary. The headline reads **ASSESSMENT COMPLETE!** and the encouragement reads **Great result. Strong start!**. Actual reps, technique and earned XP populate the screen; no fitness tier is displayed. A zero-rep assessment retains the retry/skip recovery screen.

The first successful assessment grants a welcome case with exactly **200 Aura**, independently of the daily workout quota. The receipt `assessment:welcome:v1` survives restarts and retakes. The purple case is ready to open immediately, without random upgrades. Its saved prize contains no gems or avatar cards. Opening persists the reveal; claiming credits the Aura wallet and removes the case in one save. Leaving before claiming keeps it available in inventory. Ledger v4 preserves v1–v3 inventories, wallets and receipts.

The summary labels the bonus **200 AURA / INSIDE YOUR CASE** so it is not confused with already credited XP. Its action reads **OPEN CASE** and points to this assessment's case. Subsequent assessments still show encouragement and their recorded XP, with a home action after the welcome case has been claimed.

The final case charge builds a violet fire vortex. Three twisting flame streams, fine lilac trails, hot pink tips and rotating embers surround a luminous eye on a nearly black background. At release the eye expands and the flame emblem emerges; the 1.6-second entrance counts to +200 and unlocks **TAP TO COLLECT**. The fire continues to flow behind the settled reward. The explanatory paragraph below the amount is hidden.

`AuraVortex.shader` draws the fire on one full-screen UI quad, including purple light at the physical screen edges. It uses directional procedural noise and additive soft glow without a post-processing camera or texture sequence. `AuraEnergyGraphic` owns a private instance of the serialized `Rewards/AuraVortex` material and updates explicit unscaled time, strength and reveal phase without rebuilding the mesh every frame. The effects do not intercept taps or consume gameplay randomness and clear on interruption. The earned Aura flies into the home Aura counter after claiming.

Author scenes: **Tools → Push Stars → Rewards → Configure Assessment Aura**. Only RewardSummary, CaseOpening and CaseReward are modified; dirty target scenes are rejected and other open scenes remain intact.

Validation menus: **Validate Assessment Aura** runs the 16-case economy regression and captures the reveal at 390×844, 320×568 and 430×932. **Validate Assessment Flow In Play Mode** exercises real screen actions with a wallet-free preview, including repeated taps and early claiming. Reports and images are in `output/assessment-rewards`; economy report is `Logs/case-rewards-regression.txt`.

**Render Aura Fire** checks the shader and material bindings and exports the case, prize and a four-second sequence without rerunning economy checks.
