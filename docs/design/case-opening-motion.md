# Case opening motion

Reference: https://www.pinterest.com/pin/924434261028350342/ (seven-second Duolingo case clip, inspected in the browser).

Adapted choreography: anticipation squash; upward stretch and tilted hop; lagging ground shadow; tapered white ribbons orbiting behind and in front of the case; small diamonds, four-point glints and oval flecks; rarity/palette change at the impulse; a settling bounce. The final reveal keeps the case intact: a modest stretch and flash lead straight to the prize screen, as requested. The artwork remains Push Stars' existing four case sprites.

`CaseOpeningMotion` samples a single unscaled timeline. Upgrade feedback lasts 0.80 seconds: compression, hop, a rarity change at 0.27 seconds inside the flash, then settle. The final reveal lasts 0.90 seconds: charge, release at 0.38 seconds, a 14% vertical stretch, outgoing glow and the existing prize scene. Sound cues follow these beats. Stars, title and the consumed attempt pip react to the same timeline. The hint distinguishes upgrade attempts from the final opening.

The case uses its original `Image`, without mesh modifiers or separate parts. `CaseMotionGraphic` draws the grounded shadow, glow, tapered front/back ribbons and glints without allocating particle objects or reading Unity's random generator. `CaseMotion.shader` clips the travelling highlight and flash to the sprite alpha.

All scene references and effect layers are authored in `CaseOpening.unity`; runtime code creates only a private material instance, disposed on destruction. The whole-screen button stays above the decorative layers. Reset restores authored position, scale, rotation and UI colors. Reward storage, rarity odds, three-tap rules, prize generation and claiming are unchanged.

Reconfigure: **Tools → Push Stars → Rewards → Configure Case Motion**. The setup opens the case scene additively and preserves other open scenes; it refuses to overwrite unsaved edits in an already-open case scene.

Validate: `PushStars.Editor.CaseOpeningMotionSetup.ConfigureAndValidate` checks the serialized rig, depth order, raycasts, all four rarities, intact artwork and motion, interrupted-pose reset and gameplay random-state preservation, renders timeline frames and runs the existing case economy regression. Visual output: `output/case-motion`. Existing play-mode attention and full reward-route checks remain applicable.
