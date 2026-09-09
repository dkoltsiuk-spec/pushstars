# Rejected Higgsfield Victory Flex experiment

Archived editable source and raw output from a rejected animation experiment. This clip is not assigned to a controller, prefab, scene, or `MainCharacterSetup`; the game continues to use `Warrior Idle.fbx` for its Accent state.

- Higgsfield job: `7ed2b736-ab53-46d5-abe8-024fe2b3e206`
- Model/job type: Ardy Core H8 / `kimodo`
- Charged: `0.1` credit (balance `160.79` -> `160.69`)
- Duration: 3 seconds, 60 frames at 20 fps
- Prompt: "A person stands firmly in place, performs a sharp athletic double-biceps victory flex with a confident chest lift, then smoothly returns to the original neutral stance. No walking or running; both feet stay planted."

Files:

- `Higgsfield_VictoryFlex_RAW.npz` is the untouched generated 27-joint motion.
- `Higgsfield_VictoryFlex_Editable.blend` contains both `Higgsfield_VictoryFlex_RAW` and the locally rebuilt `VictoryFlex` draft action on the source rig.
- `Higgsfield_VictoryFlex_UnityDraft.fbx` is an unassigned animation-only Unity draft retargeted onto MainMan's 65-bone Mixamo skeleton.

The raw take leaned and moved a foot near its end. The `VictoryFlex` draft keeps a restrained amount of the generated upper-body motion, but its arm motion was substantially rebuilt locally. The user rejected the displayed result, so neither version should be enabled without a new explicit creative decision.

To re-export, open the editable `.blend` in Blender 5.1+, select `Higgsfield_VictoryFlex_Rig`, make `VictoryFlex` active, and export an animation-only FBX with leaf bones disabled, animation baking enabled, `-Z Forward`, and `Y Up`.
