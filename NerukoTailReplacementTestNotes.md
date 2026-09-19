# NerukoTail Cat Replacement Test

Date: 2026-07-08

## Created assets
- Test scene: `Assets/Scenes/TitleScene_NerukoTailTest.unity`
- Test prefab: `Assets/Prefab/NerukoTailCat_LOD0_OB.prefab`
- Normal test object: `Normal_Cat_NerukoTail_Test`
- OP test object: `OP_Cat_B_NerukoTail_Test`

## Source references observed
- Source FBX: `Assets/DL Assets/Red_Deer/Cats/Cat_Simple/Cat/FBX/Assets_Models_CatSimple_NerukoTail_LOD0_OB.fbx`
- Material assigned to renderers: `Assets/DL Assets/Red_Deer/Cats/Cat_Simple/Cat/Materials/CatSim_color_6.mat`
- Normal original Animator Controller: `Assets/DL Assets/Red_Deer/Cats/Cat_Simple/Cat/FBX/NormalCatSimple_AnimContr 1.controller`
- OP original Animator Controller: `Assets/DL Assets/Red_Deer/Cats/Cat_Simple/Cat/FBX/OPCat_CatSimple_AnimContr.controller`
- Normal test Avatar: `Assets/DL Assets/Red_Deer/Cats/Cat_Simple/Cat/FBX/Assets_Models_CatSimple_NerukoTail_LOD0_OB.fbx`
- OP test Avatar: `Assets/DL Assets/Red_Deer/Cats/Cat_Simple/Cat/FBX/Assets_Models_CatSimple_NerukoTail_LOD0_OB.fbx`

## Scene changes in test scene only
- Original `Normal_Cat` set inactive, not deleted.
- Original `OP_Cat B` set inactive, not deleted.
- Copied matching hierarchy components from originals to new full-FBX-derived objects.
- Remapped serialized object references from original cats to test cats: 0
- Remapped PlayableDirector generic bindings from original cats to test cats: 0
- Reconnected `CatMotionController` on `Normal_Cat_NerukoTail_Test` to its own Animator and `CatPositionController`.
- Reconnected `CatAnimationRuntime` on `Normal_Cat_NerukoTail_Test` to `OP_Cat_B_NerukoTail_Test`, its own Animator, its own `CatPositionController`, and its own `NekomataLookRigController`.
- Reconnected `CatPresentationModeController` to the new OP/Normal test cats.
- Reconnected `NekomataLookRigController` eye bone references to the new model's `eye.L` and `eye.R` transforms.

## Quick structural checks
- Normal test Animator Controller: `Assets/DL Assets/Red_Deer/Cats/Cat_Simple/Cat/FBX/NormalCatSimple_AnimContr 1.controller`
- OP test Animator Controller: `Assets/DL Assets/Red_Deer/Cats/Cat_Simple/Cat/FBX/OPCat_CatSimple_AnimContr.controller`
- Normal test SkinnedMeshRenderer count: 1
- OP test SkinnedMeshRenderer count: 1

## Play Mode check
- Entered Play Mode in `TitleScene_NerukoTailTest.unity` after clearing Console.
- No `CatPositionController`, `CatMotionController`, `CatPurrAudioController`, `CatPresentationModeController`, Timeline, or `PlayableDirector` errors were logged during the short smoke test.
- Remaining warnings were two existing `The referenced script (Unknown) on this Behaviour is missing!` messages and MCP bridge noise.

## Manual visual checks still required
- Confirm the model does not collapse visually.
- Confirm Idle animation is playing on `Normal_Cat_NerukoTail_Test`.
- Confirm the forked tail is visible from the intended camera angle.
- Confirm material appearance is acceptable.
- Confirm OP Timeline framing, position, rotation, scale, and visibility in the Game view.
