using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using PushStars.UI;

namespace PushStars.Editor
{
    /// <summary>
    /// One-click import for the owner's own main characters (<c>Assets/Character/Main_man</c> and
    /// <c>Assets/Character/Main_woman</c>) — the models that replace the blockman placeholder on
    /// the main VS screen and the Mixamo Ch36 stand-in in the CV avatar stand.
    ///
    /// <para><b>Why a tool and not just import settings.</b> The bodies and the clips come from
    /// different pipelines and only meet through Unity's Humanoid layer — and the two bodies no
    /// longer even share a pipeline with each other:</para>
    /// <list type="bullet">
    ///   <item>the man is rigged by Mixamo itself (<c>mixamorig:*</c>, 66 bones), so his bind pose
    ///         is the same T-pose the clips were authored against;</item>
    ///   <item>the woman is still an AccuRig / Character Creator export (<c>CC_Base_*</c>, 141
    ///         bones), which binds knock-kneed — see the stance line in the rig report;</item>
    ///   <item>the clips are Mixamo, on their own skeleton again (<c>mixamorig9:*</c>, 65 bones).</item>
    /// </list>
    /// <para>Humanoid is what lets one table of clips dress all three. Nothing below is specific to
    /// a rig except the CC bone-name table, which simply finds nothing on a Mixamo body.</para>
    /// <para>Setting <i>both</i> sides to Humanoid is the retarget: Mecanim plays the clip on the
    /// generic muscle space and solves it back onto whatever rig the Animator carries. Nothing is
    /// baked, so re-exporting a body from AccuRig only needs a re-run of this menu — and it is why
    /// one folder of clips dresses both characters instead of one set per figure.</para>
    ///
    /// Menu: Tools → Push Stars → Character → Import Main Characters (rig + retarget clips).
    /// </summary>
    public static class MainCharacterSetup
    {
        /// <summary>The Mixamo clips, shared. They are retargeted onto whichever rig the Animator
        /// carries, so they live beside the characters rather than inside one of them.</summary>
        private const string AnimDir = "Assets/Character/Animations";

        /// <summary>Mask that keeps the idle accent off the legs. See <see cref="AccentLayer"/>.</summary>
        private const string MaskPath = AnimDir + "/UpperBody.mask";

        /// <summary>The layer the accent plays on, masked to the upper body.
        ///
        /// <para><b>Why the accent cannot be a full-body state.</b> Every clip here is a Mixamo
        /// idle authored for a Mixamo skeleton, and retargeted onto these legs the ankles do not
        /// stay put: the plain idle drifts a tolerable 3–4 cm, but the warrior idle used as the
        /// break shifts its weight and drags the feet 7–9 cm across the floor. Played full-body
        /// every few loops, that is a character standing still whose legs periodically slide out
        /// from under him — which is what "the legs are loose" actually looks like. Masked to the
        /// upper body, the break becomes what it was meant to be: he shifts his shoulders while
        /// his feet stay where the idle put them.</para></summary>
        public const string AccentLayer = "Accent";

        /// <summary>Everything one character needs, in one place. Adding a third figure — an
        /// opponent, a boss — is a row in <see cref="Characters"/> and nothing else.</summary>
        public sealed class CharacterDef
        {
            public CharacterGender Gender;
            /// <summary>Name used in logs, dialogs and the prefab's root GameObject.</summary>
            public string Name;
            /// <summary>Where AccuRig exports it ("Export Directory" in the character's .json), so
            /// a re-export from the modelling tool lands on top of the same files instead of
            /// beside them.</summary>
            public string Dir;
            public string BodyFbx;
            public string PrefabPath;
            public string ControllerPath;
            public string MatDir  => Dir + "/Materials";
            public string MatPath => $"{MatDir}/{Name}.mat";
            /// <summary>Where <see cref="ExtractEmbeddedTextures"/> unpacks the maps the FBX
            /// carries inside itself.</summary>
            public string TexDir => Dir + "/Textures";
            /// <summary>The sidecar folder an older AccuRig export wrote its maps into instead of
            /// embedding them. Still read, so a re-export in either style lands correctly.</summary>
            public string LegacyTexDir => Path.ChangeExtension(BodyFbx, ".fbm");
        }

        /// <summary>The playable figures. Male is the default everything falls back to.</summary>
        public static readonly CharacterDef[] Characters =
        {
            new CharacterDef
            {
                Gender         = CharacterGender.Male,
                Name           = "MainMan",
                Dir            = "Assets/Character/Main_man",
                BodyFbx        = "Assets/Character/Main_man/main_man.fbx",
                PrefabPath     = "Assets/Character/Main_man/MainMan.prefab",
                ControllerPath = "Assets/Character/Main_man/MainMan.controller",
            },
            new CharacterDef
            {
                Gender         = CharacterGender.Female,
                Name           = "MainWoman",
                Dir            = "Assets/Character/Main_woman",
                BodyFbx        = "Assets/Character/Main_woman/main_woman.fbx",
                PrefabPath     = "Assets/Character/Main_woman/MainWoman.prefab",
                ControllerPath = "Assets/Character/Main_woman/MainWoman.controller",
            },
        };

        public static CharacterDef Definition(CharacterGender gender)
            => Characters.FirstOrDefault(c => c.Gender == gender) ?? Characters[0];

        /// <summary>The state the character rests in. Named for its role, not for the clip that
        /// fills it, so swapping the idle for another Mixamo download changes one row of the table
        /// below and nothing else.</summary>
        public const string IdleState = "Idle";

        /// <summary>The break played every few idle loops — an arm stretch. Imported one-shot, and
        /// scheduled at runtime by <see cref="PushStars.UI.CharacterIdleAccent"/>.</summary>
        public const string AccentState = "WarriorIdle";

        /// <summary>The stylised character shader (flat shading + inverted-hull outline), at
        /// Assets/_Project/Art/Shaders/CharacterToon.shader.</summary>
        public const string ToonShaderName = "Push Stars/Character Toon";

        /// <summary>The characters are modelled at whatever size the exporter felt like, and every
        /// stage that shows them is framed for an adult (the CV mirror even compares the torso
        /// against the user's). Import scales each one to this height instead of hard-coding a
        /// factor, so a re-export at any size still lands on its feet — and so the man and the
        /// woman stand eye to eye when the switch flips between them.</summary>
        private const float TargetHeightMeters = 1.80f;

        /// <summary>Mixamo source file → Animator state name → loops. The state names are what
        /// <c>Animator.Play</c> callers use; the Mixamo file names never leak past this table.
        ///
        /// <para><b>Download every clip as "Without Skin".</b> Mixamo's default packs a full mesh
        /// and its textures into the FBX, which makes a two-second take a 53 MB file — and the mesh
        /// is dead weight, because clips reach the characters through Humanoid retargeting, never
        /// as geometry. Two clips (Victory, SadIdle) were downloaded the wrong way and dropped
        /// again for that reason; re-add them here when a result screen needs them, at ~2 MB
        /// each.</para></summary>
        private static readonly (string file, string state, bool loop)[] Clips =
        {
            ("Standing W_Briefcase Idle.fbx", IdleState,   true),
            // Loop OFF: it is a one-shot break, and the accent scheduler waits for it to end.
            ("Warrior Idle.fbx",              AccentState, false),
        };

        [MenuItem("Tools/Push Stars/Character/Import Main Characters (rig + retarget clips)", priority = 320)]
        public static void ImportAll()
        {
            AssetDatabase.Refresh();

            // Clips first: they are shared, and every character's controller is built from them.
            ConfigureClips();

            var imported = new List<CharacterDef>();
            foreach (var def in Characters)
            {
                if (AssetImporter.GetAtPath(def.BodyFbx) as ModelImporter == null)
                {
                    Debug.LogWarning($"[MainCharacter] {def.Name}: FBX not found, skipped:\n{def.BodyFbx}");
                    continue;
                }
                if (Import(def)) imported.Add(def);
            }

            if (imported.Count == 0)
            {
                Fail("No character FBX found under Assets/Character. Expected:\n" +
                     string.Join("\n", Characters.Select(c => "  " + c.BodyFbx)));
                return;
            }

            AssetDatabase.SaveAssets();
            foreach (var def in imported) Report(def);
        }

        /// <summary>Imports one character end to end. Deliberately not batched with
        /// StartAssetEditing: every step below reads back the result of the previous import
        /// (material names, the model's real height).</summary>
        private static bool Import(CharacterDef def)
        {
            ConfigureBody(def);
            RepairAvatar(def);
            ExtractEmbeddedTextures(def);
            ConfigureTextures(def);
            RemapBodyMaterial(def);

            var controller = BuildController(def);
            if (controller == null) return false;

            BuildPrefab(def, controller);
            return true;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  IMPORT SETTINGS
        // ════════════════════════════════════════════════════════════════════════

        // ════════════════════════════════════════════════════════════════════════
        //  AVATAR REPAIR  (what the auto-mapper gets wrong on this rig)
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>The CC / AccuRig skeleton, which is a fixed naming convention. Used only to
        /// <i>fill</i> humanoid slots the auto-mapper left empty — never to override a bone it
        /// did choose, because it reads the hierarchy and this table only reads names.</summary>
        private static readonly (HumanBodyBones bone, string cc)[] CcCentreBones =
        {
            (HumanBodyBones.Hips,       "CC_Base_Hip"),
            (HumanBodyBones.Spine,      "CC_Base_Waist"),
            (HumanBodyBones.Chest,      "CC_Base_Spine01"),
            (HumanBodyBones.UpperChest, "CC_Base_Spine02"),
            (HumanBodyBones.Neck,       "CC_Base_NeckTwist01"),
            (HumanBodyBones.Head,       "CC_Base_Head"),
        };

        /// <summary>Left/right pairs, written once. Eyes and jaw are deliberately absent: nothing
        /// in this project animates a face, and a wrongly-axed eye bone is a new bug for no gain.</summary>
        private static readonly (HumanBodyBones left, HumanBodyBones right, string suffix)[] CcSideBones =
        {
            (HumanBodyBones.LeftUpperLeg,  HumanBodyBones.RightUpperLeg,  "Thigh"),
            (HumanBodyBones.LeftLowerLeg,  HumanBodyBones.RightLowerLeg,  "Calf"),
            (HumanBodyBones.LeftFoot,      HumanBodyBones.RightFoot,      "Foot"),
            (HumanBodyBones.LeftToes,      HumanBodyBones.RightToes,      "ToeBase"),
            (HumanBodyBones.LeftShoulder,  HumanBodyBones.RightShoulder,  "Clavicle"),
            (HumanBodyBones.LeftUpperArm,  HumanBodyBones.RightUpperArm,  "Upperarm"),
            (HumanBodyBones.LeftLowerArm,  HumanBodyBones.RightLowerArm,  "Forearm"),
            (HumanBodyBones.LeftHand,      HumanBodyBones.RightHand,      "Hand"),
            (HumanBodyBones.LeftThumbProximal,     HumanBodyBones.RightThumbProximal,     "Thumb1"),
            (HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.RightThumbIntermediate, "Thumb2"),
            (HumanBodyBones.LeftThumbDistal,       HumanBodyBones.RightThumbDistal,       "Thumb3"),
            (HumanBodyBones.LeftIndexProximal,     HumanBodyBones.RightIndexProximal,     "Index1"),
            (HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.RightIndexIntermediate, "Index2"),
            (HumanBodyBones.LeftIndexDistal,       HumanBodyBones.RightIndexDistal,       "Index3"),
            (HumanBodyBones.LeftMiddleProximal,     HumanBodyBones.RightMiddleProximal,     "Mid1"),
            (HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.RightMiddleIntermediate, "Mid2"),
            (HumanBodyBones.LeftMiddleDistal,       HumanBodyBones.RightMiddleDistal,       "Mid3"),
            (HumanBodyBones.LeftRingProximal,       HumanBodyBones.RightRingProximal,       "Ring1"),
            (HumanBodyBones.LeftRingIntermediate,   HumanBodyBones.RightRingIntermediate,   "Ring2"),
            (HumanBodyBones.LeftRingDistal,         HumanBodyBones.RightRingDistal,         "Ring3"),
            (HumanBodyBones.LeftLittleProximal,     HumanBodyBones.RightLittleProximal,     "Pinky1"),
            (HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.RightLittleIntermediate, "Pinky2"),
            (HumanBodyBones.LeftLittleDistal,       HumanBodyBones.RightLittleDistal,       "Pinky3"),
        };

        /// <summary>Bone chains straightened in the T-pose reference, parent before child so a
        /// corrected thigh carries the calf with it and the calf is then measured where it landed.
        ///
        /// <para><b>Legs only, on purpose.</b> Unity's auto-mapper does pull the arms into a T on
        /// this rig — the report measures them 2–5° off the clip, which is as good as retargeting
        /// gets — but it leaves the legs in the model's own bind pose, and these figures bind
        /// knock-kneed: knees together, ankles touching, toes turned in. That splay is not a pose,
        /// it is where <i>muscle zero</i> points, so every retargeted clip inherits it and the legs
        /// read as rubbery no matter what the animation does. Straightening them here costs a
        /// little skin deformation at the knees and fixes every clip at once.</para>
        ///
        /// <para><b>Why each correction names an axis</b> rather than simply aiming the bone where
        /// a T-pose wants it. The shortest rotation onto a target direction also rolls the bone
        /// about its own length, and a leg's roll is what decides which way the knee bends. Aimed
        /// that way, the first attempt did straighten the stance — and then played the warrior idle
        /// with the legs swinging fore-and-aft instead of apart, because the clip's spread had been
        /// rotated onto the wrong axis. Each correction is therefore confined to the one plane its
        /// defect lives in: knock-knees are a splay seen from the front, turned-in toes are a yaw
        /// seen from above, and nothing else about the bone is touched.</para></summary>
        private static readonly (HumanBodyBones from, HumanBodyBones to, Vector3 direction, Vector3 axis)[] TPoseChains =
        {
            (HumanBodyBones.LeftUpperLeg,  HumanBodyBones.LeftLowerLeg,  Vector3.down,    Vector3.forward),
            (HumanBodyBones.LeftLowerLeg,  HumanBodyBones.LeftFoot,      Vector3.down,    Vector3.forward),
            (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, Vector3.down,    Vector3.forward),
            (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,     Vector3.down,    Vector3.forward),
            (HumanBodyBones.LeftFoot,      HumanBodyBones.LeftToes,      Vector3.forward, Vector3.up),
            (HumanBodyBones.RightFoot,     HumanBodyBones.RightToes,     Vector3.forward, Vector3.up),
        };

        /// <summary>Whether the import straightens the legs in the T-pose reference, so the
        /// character stands the way the Mixamo clip was authored rather than the way this
        /// particular body happens to have been modelled.
        ///
        /// <para><b>Why this is the thing that makes the pose match.</b> Retargeting carries muscle
        /// values, which are angles measured <i>from each rig's own T-pose</i>. Mixamo's standard
        /// figure stands with its legs vertical, so "muscle zero" there means legs together. This
        /// body was auto-rigged by Mixamo into a mesh modelled with the legs apart, so its muscle
        /// zero is already 17° open — and every clip lands 17° wider than the animation it came
        /// from, no matter how faithfully the retarget does its job. Straightening the reference is
        /// what makes the two rigs agree on where zero is.</para>
        ///
        /// <para>Kept as a switch because it does move the skin: the mesh was bound with the legs
        /// apart, and the corrected reference asks it to stand with them together. Half that much
        /// correction on the previous AccuRig body read as unnatural. Turn it off and re-import to
        /// compare.</para></summary>
        public static bool StraightenLegsInTPose
        {
            get => EditorPrefs.GetBool(StraightenLegsKey, true);
            set => EditorPrefs.SetBool(StraightenLegsKey, value);
        }

        private const string StraightenLegsKey = "pushstars.character.straightenLegs";

        [MenuItem("Tools/Push Stars/Character/Straighten legs in T-pose", priority = 324)]
        private static void ToggleStraightenLegs()
        {
            StraightenLegsInTPose = !StraightenLegsInTPose;
            Debug.Log($"[MainCharacter] Leg straightening is now " +
                      $"{(StraightenLegsInTPose ? "ON" : "OFF")} — re-run the import menu to apply.");
        }

        [MenuItem("Tools/Push Stars/Character/Straighten legs in T-pose", validate = true)]
        private static bool ToggleStraightenLegsValidate()
        {
            Menu.SetChecked("Tools/Push Stars/Character/Straighten legs in T-pose", StraightenLegsInTPose);
            return true;
        }

        /// <summary>Corrects the humanoid description Unity just generated, then rebuilds the
        /// avatar from it. Two things the auto-mapper leaves wrong here, and neither shows up in
        /// the import settings: finger joints it silently skipped, and a leg T-pose taken straight
        /// from however the body happened to be modelled — see
        /// <see cref="StraightenLegsInTPose"/>.</summary>
        private static void RepairAvatar(CharacterDef def)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(def.BodyFbx);
            var description = importer.humanDescription;
            if (description.human.Length == 0 || description.skeleton.Length == 0)
            {
                Debug.LogWarning($"[MainCharacter] {def.Name}: Unity produced no humanoid " +
                                 "description to repair — skipping.");
                return;
            }

            var model = AssetDatabase.LoadMainAssetAtPath(def.BodyFbx) as GameObject;
            if (model == null) return;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                // Bone names are unique in these rigs; first-wins keeps a duplicate from throwing.
                var byName = new Dictionary<string, Transform>();
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                    if (!byName.ContainsKey(t.name)) byName.Add(t.name, t);

                var human = description.human.ToList();
                var filled = FillMissingBones(human, byName);

                // Posed into the reference Unity built — including whatever it already got right on
                // the arms — so the correction starts from that and not from the raw bind pose.
                ApplySkeleton(description.skeleton, byName);

                int straightened = 0;
                if (StraightenLegsInTPose)
                {
                    // The rig the idle was authored on is the reference to match: it is what the
                    // animation's muscle values were measured against.
                    string idleFile = Clips.First(c => c.state == IdleState).file;
                    var aims = ReferenceDirections($"{AnimDir}/{idleFile}", out _, out var orientations);
                    straightened = AlignLegsToSource(human, byName, aims, orientations);
                }

                description.human    = human.ToArray();
                description.skeleton = ReadSkeleton(description.skeleton, byName);
                importer.humanDescription = description;
                importer.SaveAndReimport();

                Debug.Log($"[MainCharacter] {def.Name}: avatar repaired — " +
                          $"{filled.Count} bone(s) the auto-mapper missed filled in" +
                          (filled.Count == 0 ? "" : $" ({string.Join(", ", filled)})") +
                          (StraightenLegsInTPose
                              ? $", {straightened} leg joint(s) straightened into the T-pose."
                              : ", legs left as the auto-mapper built them."));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Adds a mapping for every humanoid slot still empty whose conventional CC bone
        /// is actually in the rig. Returns what it filled, for the log.</summary>
        private static List<string> FillMissingBones(List<HumanBone> human, Dictionary<string, Transform> byName)
        {
            var mapped = new HashSet<string>(human.Select(h => h.humanName));
            var filled = new List<string>();

            foreach (var (bone, cc) in CcBoneTable())
            {
                string humanName = HumanTrait.BoneName[(int)bone];
                if (mapped.Contains(humanName)) continue;
                if (!byName.ContainsKey(cc)) continue;

                human.Add(new HumanBone
                {
                    humanName = humanName,
                    boneName  = cc,
                    limit     = new HumanLimit { useDefaultValues = true },
                });
                mapped.Add(humanName);
                filled.Add($"{bone}→{cc}");
            }
            return filled;
        }

        private static IEnumerable<(HumanBodyBones bone, string cc)> CcBoneTable()
        {
            foreach (var entry in CcCentreBones) yield return entry;
            foreach (var (left, right, suffix) in CcSideBones)
            {
                yield return (left,  "CC_Base_L_" + suffix);
                yield return (right, "CC_Base_R_" + suffix);
            }
        }

        /// <summary>Aims each leg joint at the direction the <i>source clip's own rig</i> holds it
        /// in at its T-pose, so that muscle zero means the same pose on both skeletons.
        ///
        /// <para><b>Why the source's T-pose and not an ideal one.</b> Aiming at a textbook T-pose —
        /// legs vertical, toes forward — closes the gap this body was modelled with and then opens
        /// a fresh one, because Mixamo's own reference is not that: its thighs sit a few degrees
        /// forward and its feet turn out about 17°. Every clip then plays with that difference
        /// baked in as a constant offset, which is small, never resolves, and reads as the legs
        /// drifting under an animation that is otherwise correct. Matching the source instead makes
        /// the retarget an identity for a rig that came from the same place.</para>
        ///
        /// <para>Falls back to the ideal direction for any bone the source cannot supply. The
        /// rotation's axis is the cross product of the two directions and therefore perpendicular
        /// to the bone, so this aims a joint without twisting it about its own length — the roll
        /// that decides which way a knee bends is left alone.</para></summary>
        private static int AlignLegsToSource(List<HumanBone> human, Dictionary<string, Transform> byName,
                                             Dictionary<HumanBodyBones, Vector3> aims,
                                             Dictionary<HumanBodyBones, Quaternion> orientations)
        {
            int corrected = 0;
            // Parent before child: setting a bone's world rotation carries its children with it,
            // so a leg has to be aligned from the hip down or the work is undone below.
            foreach (var (from, to, ideal, _) in TPoseChains)
            {
                var parent = BoneTransform(human, byName, from);
                if (parent == null) continue;

                // Whole orientation where the source can supply it. Aiming the bone leaves its roll
                // untouched, and roll is not a detail here: for the ankle it is the toe-in/toe-out
                // axis, so a reference that is aimed perfectly but rolled 12° hands Unity's
                // foot-twist muscle a frame the animation was never measured in. The foot then
                // turns about itself for the length of the clip — motion that exists in neither the
                // model nor the animation, which is exactly how it looks.
                if (orientations.TryGetValue(from, out var goalRotation))
                {
                    if (Quaternion.Angle(parent.rotation, goalRotation) < 0.5f) continue;

                    parent.rotation = goalRotation;
                    corrected++;
                    continue;
                }

                var child = BoneTransform(human, byName, to);
                if (child == null) continue;

                Vector3 goal = aims.TryGetValue(from, out var fromSource) ? fromSource : ideal;
                Vector3 current = child.position - parent.position;
                if (current.sqrMagnitude < 1e-8f || goal.sqrMagnitude < 1e-8f) continue;
                if (Vector3.Angle(current, goal) < 0.5f) continue;

                parent.rotation = Quaternion.FromToRotation(current.normalized, goal.normalized)
                                * parent.rotation;
                corrected++;
            }
            return corrected;
        }

        private static Transform BoneTransform(List<HumanBone> human, Dictionary<string, Transform> byName,
                                               HumanBodyBones bone)
        {
            string humanName = HumanTrait.BoneName[(int)bone];
            var entry = human.FirstOrDefault(h => h.humanName == humanName);
            if (string.IsNullOrEmpty(entry.boneName)) return null;
            return byName.TryGetValue(entry.boneName, out var t) ? t : null;
        }

        /// <summary>Poses a model instance into the avatar's T-pose reference — where muscle zero
        /// points. The one pose worth looking at when a retarget is suspect: the clips only ever
        /// move away from it, so a defect visible here is in every clip by construction, and one
        /// that isn't here came from the animation.</summary>
        public static void PoseAtReference(CharacterDef def, GameObject instance)
        {
            if (AssetImporter.GetAtPath(def.BodyFbx) is not ModelImporter importer) return;

            var byName = new Dictionary<string, Transform>();
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name)) byName.Add(t.name, t);

            // The skeleton array's first entry is the model root, and applying it would move the
            // instance itself — the caller placed it where it wants it, and a pose must not
            // relocate what it is posing.
            var root = instance.transform;
            var (position, rotation, scale) = (root.localPosition, root.localRotation, root.localScale);
            ApplySkeleton(importer.humanDescription.skeleton, byName);
            root.localPosition = position;
            root.localRotation = rotation;
            root.localScale    = scale;
        }

        /// <summary>Poses the hierarchy into a stored T-pose reference.</summary>
        private static void ApplySkeleton(SkeletonBone[] skeleton, Dictionary<string, Transform> byName)
        {
            foreach (var bone in skeleton)
            {
                if (!byName.TryGetValue(bone.name, out var t)) continue;
                t.localPosition = bone.position;
                t.localRotation = bone.rotation;
                t.localScale    = bone.scale;
            }
        }

        /// <summary>Reads the hierarchy back into a skeleton array, keeping the original entries'
        /// order and names so the description stays the one Unity generated, only corrected.</summary>
        private static SkeletonBone[] ReadSkeleton(SkeletonBone[] template, Dictionary<string, Transform> byName)
        {
            var result = new SkeletonBone[template.Length];
            for (int i = 0; i < template.Length; i++)
            {
                var bone = template[i];
                if (byName.TryGetValue(bone.name, out var t))
                {
                    bone.position = t.localPosition;
                    bone.rotation = t.localRotation;
                    bone.scale    = t.localScale;
                }
                result[i] = bone;
            }
            return result;
        }

        /// <summary>Unpacks the maps the FBX carries inside itself into real texture assets.
        ///
        /// <para><b>Why this step is not optional.</b> AccuRig has exported the Tripo bake two
        /// different ways: older files wrote a <c>&lt;name&gt;.fbm</c> folder beside the FBX, newer
        /// ones embed the PNG in the binary and point the material at a path inside AccuRig's own
        /// temp directory — a folder that does not exist on this machine. Left embedded, the map
        /// is a sub-asset with no importer of its own: it cannot be capped at 2048, and the
        /// material lookup below has nothing on disk to find, so the character comes out flat grey
        /// while every import setting still reads as correct.</para></summary>
        private static void ExtractEmbeddedTextures(CharacterDef def)
        {
            if (AssetDatabase.FindAssets("t:Texture2D", new[] { def.TexDir }).Length > 0) return;
            if (AssetDatabase.IsValidFolder(def.LegacyTexDir)) return; // older export, already on disk

            if (!AssetDatabase.IsValidFolder(def.TexDir))
                AssetDatabase.CreateFolder(def.Dir, Path.GetFileName(def.TexDir));

            var importer = (ModelImporter)AssetImporter.GetAtPath(def.BodyFbx);
            if (!importer.ExtractTextures(def.TexDir))
            {
                Debug.LogWarning($"[MainCharacter] {def.Name}: no embedded textures to extract — " +
                                 "the body will fall back to a flat colour.");
                return;
            }

            AssetDatabase.Refresh();
            importer.SaveAndReimport(); // re-binds the model's materials to the extracted assets
        }

        /// <summary>The Tripo bake ships at 4096² (17 MB PNG) — far past what a character that
        /// covers a third of a phone screen can show. 2048 halves the memory and is invisible.</summary>
        private static void ConfigureTextures(CharacterDef def)
        {
            foreach (string path in AssetDatabase.FindAssets("t:Texture2D", new[] { def.Dir })
                                                 .Select(AssetDatabase.GUIDToAssetPath))
            {
                if (AssetImporter.GetAtPath(path) is not TextureImporter tex) continue;
                if (tex.maxTextureSize <= 2048 && tex.mipmapEnabled) continue;

                tex.maxTextureSize    = 2048;
                tex.mipmapEnabled     = true;
                tex.textureCompression = TextureImporterCompression.Compressed;
                tex.SaveAndReimport();
            }
        }

        /// <summary>Body: Humanoid rig built from its own skeleton (that avatar is the retarget
        /// target), no clips inside the file, bones left exposed — the CV mirror walks them with
        /// <c>Animator.GetBoneTransform</c>, which returns null on an optimised hierarchy.</summary>
        private static void ConfigureBody(CharacterDef def)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(def.BodyFbx);

            // Drop the rig to Generic first. Unity caches the humanoid mapping AND its skeleton
            // pose in the .meta, then reuses them on every later reimport instead of re-running
            // the auto-mapper — so a description built for a different body outlives it, and the
            // avatar's humanScale comes out in the thousands. Mecanim multiplies every retargeted
            // bone translation by that number, which leaves the bone angles looking perfect while
            // the skin is flung kilometres apart. The round trip clears the stored description so
            // the mapping below is built against the model as it stands now.
            //
            // This is exactly the case when a new export replaces an old one under the same file
            // name — the .meta survives, and with it a mapping for a body that is gone.
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.SaveAndReimport();
                importer = (ModelImporter)AssetImporter.GetAtPath(def.BodyFbx);
            }
            importer.humanDescription = new HumanDescription();

            importer.animationType   = ModelImporterAnimationType.Human;
            importer.avatarSetup     = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.optimizeGameObjects = false;
            importer.materialImportMode  = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation    = ModelImporterMaterialLocation.InPrefab;

            // The AccuRig export ships a scene camera and a light inside the FBX. Left on, they
            // become live objects under the character — a second camera rendering over the game.
            importer.importCameras = false;
            importer.importLights  = false;

            // Import scale stays at the file's own: a globalScale other than 1 leaves the humanoid
            // avatar and the mesh's bind pose disagreeing about how big this character is, and the
            // skin tears itself apart the moment a clip plays (bones read fine, geometry does not).
            // Sizing happens on the prefab's Transform instead — see BuildPrefab.
            importer.globalScale  = 1f;
            importer.useFileScale = true;

            // Fully welded normals. The outline is an inverted hull expanded along the normals, so
            // anywhere the export split them — UV seams, hard edges — the hull tears open and the
            // black shows through as scratches across the chest and abs. Averaging across shared
            // positions closes it, and the flat cartoon shading has no hard edges to lose.
            importer.importNormals        = ModelImporterNormals.Calculate;
            importer.normalCalculationMode = ModelImporterNormalCalculationMode.AreaAndAngleWeighted;
            importer.normalSmoothingAngle  = 180f;

            importer.SaveAndReimport();
        }

        /// <summary>Clips: Humanoid so Mecanim can retarget them off the Mixamo skeleton, one
        /// avatar per file (every clip FBX carries its own full <c>mixamorig9</c> rig), no
        /// materials — two of these files were downloaded <i>with</i> the Mixamo skin and would
        /// otherwise drag a second character's textures into the project.
        ///
        /// <para>Run once for the whole cast: the clips are not per-character, and re-importing a
        /// 55 MB FBX per figure would only cost time.</para></summary>
        private static void ConfigureClips()
        {
            foreach (var (file, state, loop) in Clips)
            {
                string path = $"{AnimDir}/{file}";
                if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
                {
                    Debug.LogWarning($"[MainCharacter] Clip FBX missing, skipped: {path}");
                    continue;
                }

                // Same stale-cache trap the body has to dodge, and it bites harder here. The .meta
                // holds a humanoid description built from whatever skeleton the file used to
                // carry; re-download the clip from a re-rigged character and the bone names change
                // wholesale (mixamorig9:* → mixamorig:*), so not one of the stored mappings
                // resolves. Unity builds an empty avatar, cannot express the take in muscle space,
                // and reports the file as having no animation at all — the clip simply is not
                // there, and nothing says why.
                if (importer.animationType != ModelImporterAnimationType.Generic)
                {
                    importer.animationType = ModelImporterAnimationType.Generic;
                    importer.SaveAndReimport();
                    importer = (ModelImporter)AssetImporter.GetAtPath(path);
                }
                importer.humanDescription = new HumanDescription();

                importer.animationType  = ModelImporterAnimationType.Human;
                importer.avatarSetup    = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;

                // Drop any clip ranges the .meta is carrying, then reimport before asking the file
                // what takes it has.
                //
                // Both halves matter. A stored first/last frame outlives the take it was measured
                // on: replace a Mixamo download in place — a re-download from a re-rigged
                // character, say — and the old range can run past the end of the new take, so
                // Unity imports no clip, the state vanishes from the controller, and the character
                // stands in his bind pose with nothing in the console to explain it. And
                // defaultClipAnimations reports the takes as of the last completed import, not the
                // settings just assigned, so reading it before the reimport answers a question
                // about the file as it used to be.
                importer.clipAnimations = new ModelImporterClipAnimation[0];
                importer.SaveAndReimport();
                importer = (ModelImporter)AssetImporter.GetAtPath(path);

                var clips = importer.defaultClipAnimations;
                if (clips.Length == 0)
                    Debug.LogWarning($"[MainCharacter] {file}: Unity reports no animation take in " +
                                     "this file. A Mixamo download with no animation on it, or an " +
                                     "export whose take is empty.");
                foreach (var clip in clips)
                {
                    clip.name     = state;
                    clip.loopTime = loop;
                    // Loop Time alone only says "play me again"; Loop Pose is what makes the seam
                    // match, by referencing the whole clip to its first frame. Without it a Mixamo
                    // idle whose last frame is a few degrees off its first jumps that difference
                    // once per cycle — a small, perfectly periodic twitch in whichever limb
                    // carries the mismatch, which reads as the leg flicking for no reason.
                    clip.loopPose = loop;

                    // Mixamo idles are authored in place: bake the root into the pose so the
                    // character never drifts off the stage mark, whatever root motion says.
                    clip.lockRootRotation    = true;
                    clip.keepOriginalOrientation = true;
                    clip.lockRootHeightY     = true;
                    clip.keepOriginalPositionY   = true;
                    clip.lockRootPositionXZ  = true;
                    clip.keepOriginalPositionXZ = true;
                }
                if (clips.Length > 0) importer.clipAnimations = clips;

                importer.SaveAndReimport();
            }
        }

        /// <summary>The FBX materials import on built-in shaders and render pink under URP. One
        /// URP/Lit material carries the whole character (single Tripo atlas), remapped onto the
        /// model so the asset itself is correct — no per-instance patching in the scene tools.</summary>
        private static void RemapBodyMaterial(CharacterDef def)
        {
            var shader = CharacterShader();
            if (shader == null)
            {
                Debug.LogWarning($"[MainCharacter] {def.Name}: no usable lit shader found — material left as imported.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(def.MatDir))
                AssetDatabase.CreateFolder(def.Dir, Path.GetFileName(def.MatDir));

            var material = AssetDatabase.LoadAssetAtPath<Material>(def.MatPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, def.MatPath);
            }
            material.shader = shader;

            var diffuse = FindDiffuse(def);
            if (diffuse == null)
                Debug.LogWarning($"[MainCharacter] {def.Name}: no albedo found — the body falls " +
                                 "back to a flat colour.");
            ApplyCharacterSurface(material, diffuse);
            EditorUtility.SetDirty(material);

            var importer = (ModelImporter)AssetImporter.GetAtPath(def.BodyFbx);
            var model = AssetDatabase.LoadMainAssetAtPath(def.BodyFbx) as GameObject;
            if (model == null) return;

            var names = new HashSet<string>();
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                foreach (var m in renderer.sharedMaterials)
                    if (m != null && m != material) names.Add(m.name);

            if (names.Count == 0) return;

            foreach (string name in names)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), material);
            importer.SaveAndReimport();
        }

        /// <summary>The character's albedo, wherever this export happened to put it: extracted
        /// beside the model, in the legacy <c>.fbm</c> sidecar, or — when extraction was refused —
        /// still riding inside the FBX as a sub-asset. Tripo bakes everything into one atlas, but
        /// an export that also ships a normal or roughness map must not win the draw, hence the
        /// ranking by name.</summary>
        private static Texture2D FindDiffuse(CharacterDef def)
        {
            foreach (string dir in new[] { def.TexDir, def.LegacyTexDir })
            {
                if (!AssetDatabase.IsValidFolder(dir)) continue;

                var found = AssetDatabase.FindAssets("t:Texture2D", new[] { dir })
                                         .Select(AssetDatabase.GUIDToAssetPath)
                                         .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
                                         .Where(t => t != null)
                                         .OrderByDescending(LooksLikeAlbedo)
                                         .FirstOrDefault();
                if (found != null) return found;
            }

            return AssetDatabase.LoadAllAssetsAtPath(def.BodyFbx)
                                .OfType<Texture2D>()
                                .OrderByDescending(LooksLikeAlbedo)
                                .FirstOrDefault();
        }

        private static int LooksLikeAlbedo(Texture2D texture)
        {
            string name = texture.name.ToLowerInvariant();
            if (name.Contains("diffuse") || name.Contains("basecolor") || name.Contains("albedo")) return 2;
            if (name.Contains("normal") || name.Contains("rough") || name.Contains("metal")
                || name.Contains("occlusion") || name.Contains("_ao")) return 0;
            return 1;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  CONTROLLER + PREFAB
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>Flat state machine, no transitions — every consumer drives the character with
        /// <c>Animator.Play</c>/<c>CrossFade</c> by state name, same as the CV stand's controller.
        /// Both characters get their own copy of the same table, so a controller can later diverge
        /// (a different idle for the woman) without the switch having to know.
        ///
        /// <para>The asset is rewritten in place, never deleted and recreated. Deleting it hands
        /// the replacement a fresh GUID, and every reference already pointing at the old one — the
        /// prefab, the scene instance built from it — resolves to "Missing (Runtime Animator
        /// Controller)". The character then stands in his bind pose on Play with no error in the
        /// console to explain it.</para></summary>
        private static AnimatorController BuildController(CharacterDef def)
        {
            var motions = new List<(string state, AnimationClip clip)>();
            foreach (var (file, state, _) in Clips)
            {
                var clip = LoadClip($"{AnimDir}/{file}");
                if (clip == null)
                {
                    Debug.LogWarning($"[MainCharacter] No AnimationClip inside {file} after reimport.");
                    continue;
                }
                motions.Add((state, clip));
            }

            if (motions.Count == 0)
            {
                Fail($"No animation clips found under:\n{AnimDir}");
                return null;
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(def.ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(def.ControllerPath);

            var sm = controller.layers[0].stateMachine;
            foreach (var existing in sm.states) sm.RemoveState(existing.state);

            AnimatorState defaultState = null;
            foreach (var (state, clip) in motions)
            {
                var s = sm.AddState(state);
                s.motion = clip;
                if (state == IdleState) defaultState = s;
            }
            sm.defaultState = defaultState ?? sm.states[0].state;

            // No IK pass. It was here for a foot-pinning experiment that is gone: pinning the
            // ankles makes the knees solve for the pin rather than play the clip, which is a way
            // of hiding a bad retarget instead of fixing one. With the T-pose matched to the
            // source rig there is nothing left for it to hide.
            var baseLayers = controller.layers;
            baseLayers[0].iKPass = false;
            controller.layers = baseLayers;

            BuildAccentLayer(controller, motions);

            int pruned = PruneOrphans(controller);
            if (pruned > 0)
                Debug.Log($"[MainCharacter] {Path.GetFileName(def.ControllerPath)}: " +
                          $"removed {pruned} orphaned sub-asset(s) left by earlier rebuilds.");

            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>Deletes states, state machines and transitions no layer reaches any more.
        ///
        /// <para>Rebuilding the controller in place — which it must be, so its GUID survives — drops
        /// the old states from their state machine but leaves them in the asset file, and a layer
        /// swapped out takes its whole sub-tree with it into the same limbo. Ten imports later the
        /// file carries ten copies of every state. Nothing plays them, but they are saved, diffed
        /// and merged like anything else, and a controller full of duplicates named after the
        /// states that <i>are</i> live is a poor thing to read when something goes wrong.</para></summary>
        private static int PruneOrphans(AnimatorController controller)
        {
            var reachable = new HashSet<Object>();
            foreach (var layer in controller.layers) CollectReachable(layer.stateMachine, reachable);

            int removed = 0;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller)))
            {
                if (asset == null || asset == controller) continue;
                if (reachable.Contains(asset)) continue;

                bool belongsToAStateMachine = asset is AnimatorState
                                           || asset is AnimatorStateMachine
                                           || asset is AnimatorTransitionBase
                                           || asset is BlendTree;
                if (!belongsToAStateMachine) continue;

                Object.DestroyImmediate(asset, true);
                removed++;
            }
            return removed;
        }

        private static void CollectReachable(AnimatorStateMachine machine, HashSet<Object> into)
        {
            if (machine == null || !into.Add(machine)) return;

            foreach (var child in machine.states)
            {
                into.Add(child.state);
                if (child.state == null) continue;

                if (child.state.motion is BlendTree tree) into.Add(tree);
                foreach (var transition in child.state.transitions) into.Add(transition);
            }
            foreach (var transition in machine.anyStateTransitions) into.Add(transition);
            foreach (var transition in machine.entryTransitions) into.Add(transition);
            foreach (var child in machine.stateMachines) CollectReachable(child.stateMachine, into);
        }

        /// <summary>Adds the masked layer the idle break plays on. Its weight rests at zero and is
        /// raised by <see cref="PushStars.UI.CharacterIdleAccent"/> only while the break runs, so
        /// the base layer's idle owns the body — and the legs in particular — the rest of the time.
        ///
        /// <para>Rebuilt in place like the rest of the controller: the layer is dropped and re-added
        /// rather than accumulated, so re-running the import twice does not leave two of them.</para></summary>
        private static void BuildAccentLayer(AnimatorController controller,
                                             List<(string state, AnimationClip clip)> motions)
        {
            var accentClip = motions.FirstOrDefault(m => m.state == AccentState).clip;
            if (accentClip == null) return;

            var baseLayer = controller.layers[0];
            foreach (var extra in controller.layers.Skip(1))
                if (extra.stateMachine != null) Object.DestroyImmediate(extra.stateMachine, true);
            controller.layers = new[] { baseLayer };

            controller.AddLayer(AccentLayer);
            var layers = controller.layers;
            var accent = layers[layers.Length - 1];
            accent.avatarMask   = UpperBodyMask();
            accent.defaultWeight = 0f;
            accent.blendingMode  = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;

            var sm = controller.layers[controller.layers.Length - 1].stateMachine;
            foreach (var existing in sm.states) sm.RemoveState(existing.state);

            // Default state carries no motion: at weight zero it is never seen, and it gives the
            // layer somewhere to sit between breaks without holding the last frame of one.
            var rest = sm.AddState("Rest");
            sm.defaultState = rest;

            var play = sm.AddState(AccentState);
            play.motion = accentClip;
        }

        /// <summary>Everything above the hips. Rewritten in place so the controllers that reference
        /// it keep resolving after a re-import.</summary>
        private static AvatarMask UpperBodyMask()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, MaskPath);
            }

            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part, true);

            // Root off as well as the legs: a masked layer that still writes the root would move
            // the whole character, which is the drift this exists to prevent.
            foreach (var off in new[]
                     {
                         AvatarMaskBodyPart.Root,
                         AvatarMaskBodyPart.LeftLeg,  AvatarMaskBodyPart.RightLeg,
                         AvatarMaskBodyPart.LeftFootIK, AvatarMaskBodyPart.RightFootIK,
                     })
                mask.SetHumanoidBodyPartActive(off, false);

            EditorUtility.SetDirty(mask);
            return mask;
        }

        /// <summary>The shared character prefab. The main VS stage, the gender switch and the CV
        /// stand all instantiate these assets; the stand swaps its own controller on the
        /// instance.</summary>
        private static GameObject BuildPrefab(CharacterDef def, AnimatorController controller)
        {
            var model = AssetDatabase.LoadMainAssetAtPath(def.BodyFbx) as GameObject;
            if (model == null) return null;

            // Left as a model-prefab instance on purpose: saving it produces a prefab VARIANT of
            // the body FBX, so a re-export with new bones or a new mesh flows through while the
            // controller and animator settings below stay as overrides on top.
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = def.Name;

            var animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            // The character is rendered by an off-screen camera into a RenderTexture; the default
            // culling mode would freeze it whenever the main camera can't see it.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            DisableExportDebris(instance);
            FixSkinnedBounds(instance);
            NormalizeScale(def, instance);
            PoseAtRest(instance);

            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, def.PrefabPath);
            Object.DestroyImmediate(instance);
            return prefab;
        }

        /// <summary>Sizes the character to <see cref="TargetHeightMeters"/> on the prefab's own
        /// Transform. Measured rather than hard-coded: the FBX carries no meaningful unit scale
        /// (AccuRig writes 1.0 whatever the figure's size), so the only honest number is the one
        /// the imported mesh actually occupies.
        ///
        /// <para>Uniform Transform scale is the one place a humanoid can be resized safely. Doing
        /// it on the importer instead desynchronises the avatar from the mesh's bind pose, and the
        /// skin explodes as soon as a retargeted clip plays. Retargeting itself is scale-free; the
        /// size matters to the stage cameras, the CV mirror's torso comparison, and — now that two
        /// bodies share one stage — to the switch not resizing the shot when it flips.</para></summary>
        private static void NormalizeScale(CharacterDef def, GameObject instance)
        {
            float height = InstanceBounds(instance).size.y;
            if (height < 0.01f) return;

            float scale = TargetHeightMeters / height;
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f) return;

            instance.transform.localScale = Vector3.one * scale;
            Debug.Log($"[MainCharacter] {def.Name} modelled at {height:F2} m — prefab scaled ×{scale:F3} " +
                      $"to stand {TargetHeightMeters:F2} m.");
        }

        /// <summary>Saves the prefab holding the first frame of its idle instead of the pose the
        /// FBX rests in.
        ///
        /// <para>Only the resting pose is affected — an Animator overwrites it on the first frame
        /// of Play — but that pose is what every editor window shows: the Scene view of the main
        /// screen, the prefab thumbnail, the inspector preview. Mixamo's with-skin export rests
        /// with the skin offset a metre above its own skeleton, so left alone the character floats
        /// over the stage in edit mode and drops into place the moment you press Play, which reads
        /// as a bug in the scene rather than as a property of the file.</para></summary>
        private static void PoseAtRest(GameObject instance)
        {
            var clip = LoadClip($"{AnimDir}/{Clips.First(c => c.state == IdleState).file}");
            if (clip == null) return;

            clip.SampleAnimation(instance, 0f);
        }


        /// <summary>The FBX ships bind-pose bounds that sit ~2 m to one side of the body, so the
        /// character is frustum-culled the moment a camera frames where he actually stands — he
        /// renders in edit mode, then vanishes on Play. Recomputing the bounds from the live pose
        /// fixes it for every clip and any future re-export; the per-frame cost is irrelevant for
        /// the one or two characters ever on screen here.</summary>
        private static void FixSkinnedBounds(GameObject character)
        {
            foreach (var skin in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skin.updateWhenOffscreen = true;
        }

        /// <summary>The AccuRig export keeps its scene's camera and light rig as nodes. Their
        /// components are dropped at import, but the empty transforms survive — nothing renders
        /// under them, and a stray "Camera" in a character's hierarchy only misleads whoever reads
        /// it next. A variant cannot delete objects it inherits, so they are switched off instead.
        /// Scoped to childless, component-free nodes so no real rig part can match.</summary>
        private static void DisableExportDebris(GameObject character)
        {
            foreach (Transform child in character.transform)
            {
                if (child.childCount > 0) continue;
                if (child.GetComponents<Component>().Length > 1) continue; // Transform only

                child.gameObject.SetActive(false);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  DIAGNOSTICS
        // ════════════════════════════════════════════════════════════════════════

        [MenuItem("Tools/Push Stars/Character/Report Main Character rigs", priority = 321)]
        public static void ReportAll()
        {
            foreach (var def in Characters) Report(def);
        }


        /// <summary>Prints everything that decides whether the retarget actually looks right —
        /// and settles the one question that cannot be answered by reading import settings.
        ///
        /// <para><b>Why it samples clips.</b> These rigs bind with the arms hanging ~57° below
        /// horizontal, not in a T-pose. Unity's auto-mapper corrects that when it builds the
        /// avatar — but when it doesn't, every retargeted clip comes out with drooping arms while
        /// the import settings still read as perfectly correct. The only honest test is to play a
        /// clip on both rigs and compare the poses, which is what the fidelity lines below do.</para>
        /// </summary>
        public static void Report(CharacterDef def)
        {
            var prefab = LoadCharacterPrefab(def.Gender);
            if (prefab == null)
            {
                Debug.LogWarning($"[MainCharacter] {def.Name}: nothing imported yet.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var animator = instance.GetComponent<Animator>();
                var avatar = animator != null ? animator.avatar : null;
                var log = new System.Text.StringBuilder($"[MainCharacter] {def.Name} rig report\n");
                log.AppendLine($"  avatar: valid={avatar != null && avatar.isValid} human={avatar != null && avatar.isHuman}");

                if (animator == null || avatar == null || !avatar.isHuman)
                {
                    Debug.LogWarning(log.ToString());
                    return;
                }

                var missing = new List<string>();
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    var bone = (HumanBodyBones)i;
                    if (animator.GetBoneTransform(bone) == null) missing.Add(bone.ToString());
                }
                log.AppendLine($"  unmapped bones ({missing.Count}): " +
                               (missing.Count == 0 ? "none" : string.Join(", ", missing)));

                var importer = (ModelImporter)AssetImporter.GetAtPath(def.BodyFbx);
                var described = importer.humanDescription;
                log.AppendLine($"  humanDescription in .meta: {described.human.Length} mappings, " +
                               $"{described.skeleton.Length} skeleton bones " +
                               "(0 = the auto-mapper's guess, rebuilt on every import)");
                log.AppendLine("  bone map:");
                log.AppendLine(BoneMap(animator));

                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                var lUp  = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var rUp  = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                if (hips != null && lUp != null && rUp != null)
                {
                    Vector3 shoulderMid = (lUp.position + rUp.position) * 0.5f;
                    log.AppendLine($"  hips height={hips.position.y:F3} m, " +
                                   $"torso (shoulder-mid → hips)={Vector3.Distance(shoulderMid, hips.position):F3} m");
                }
                log.AppendLine($"  height: {InstanceBounds(instance).size.y:F3} m (target {TargetHeightMeters:F2} m)");

                // humanScale is the multiplier Mecanim puts on every retargeted bone
                // translation. Times the root's own scale it has to land near 1 for a
                // human-sized character; a stale avatar description sends it into the thousands.
                float effectiveScale = animator.humanScale * animator.transform.lossyScale.y;
                float bindSpan = SkinSpan(instance);
                log.AppendLine($"  humanScale={animator.humanScale:F4} × root {animator.transform.lossyScale.y:F3}" +
                               $" = {effectiveScale:F3}");
                log.AppendLine($"  skin at bind pose: {DescribeSkin(instance)}");

                // The verdict on the legs is taken here and not from the clips. Muscle zero is what
                // a splayed reference corrupts, and it is the same for every clip — whereas a
                // Mixamo animation-only download can carry a skewed reference of its own, which
                // makes any single clip's stance an unreliable thing to be judged against.
                //
                // Measured on a throwaway body: writing the reference onto the transforms leaves a
                // hierarchy that SampleAnimation no longer fully overrides, and the clip loop below
                // would then be measuring this pose instead of the clips.
                float referenceStance = ReferenceStance(def);
                log.AppendLine($"  stance at muscle zero: {referenceStance:F1}° (0° = legs parallel) " +
                               $"— leg straightening is {(StraightenLegsInTPose ? "ON" : "OFF")}");

                // The comparison that decides whether the character can match the clip at all:
                // muscle zero has to mean the same pose on both rigs.
                string idleFile = Clips.First(c => c.state == IdleState).file;
                var sourceDirs = ReferenceDirections($"{AnimDir}/{idleFile}", out float sourceStance,
                                                     out var sourceRots);
                var targetDirs = ReferenceDirections(def.BodyFbx, out _, out var targetRots);
                log.AppendLine($"  source rig's own muscle zero: {sourceStance:F1}° stance");
                foreach (var pair in sourceDirs)
                {
                    if (!targetDirs.TryGetValue(pair.Key, out var mine)) continue;

                    // Aim and roll reported apart. Aim can read zero while the bone is rolled a
                    // quarter turn about its own length, and for an ankle that roll is the whole
                    // toe-in/toe-out axis.
                    string roll = sourceRots.TryGetValue(pair.Key, out var sr)
                               && targetRots.TryGetValue(pair.Key, out var tr)
                        ? $", orientation {Quaternion.Angle(sr, tr):F1}° apart"
                        : "";
                    log.AppendLine($"    T-pose {pair.Key}: aim {Vector3.Angle(pair.Value, mine):F1}° " +
                                   $"apart{roll}");
                }

                float worstArm = 0f;
                float worstSpanError = 0f;
                float worstStance = 0f;
                MotionTrack idleStand = null;
                foreach (var (file, state, loops) in Clips)
                {
                    var delta = RetargetDelta(instance, file);
                    if (delta == null) { log.AppendLine($"  {state}: clip missing"); continue; }

                    var motion = MeasureMotion(instance, file, loops);
                    if (motion != null)
                    {
                        log.AppendLine($"  {state} in motion: feet slide {motion.LeftSlide * 100f:F1}/" +
                                       $"{motion.RightSlide * 100f:F1} cm, lift {motion.FootLift * 100f:F1} cm, " +
                                       $"hips bob {motion.HipsBob * 100f:F1} cm" +
                                       (loops ? $", loop seam {motion.LoopSeamDeg:F1}° ({motion.LoopSeamLabel})"
                                              : ", one-shot"));

                        // Distance from the state the character rests in. Everything the accent
                        // scheduler cross-fades into is measured against the idle, because that
                        // gap is exactly how far the feet must travel during the blend.
                        if (state == IdleState) idleStand = motion;
                        else if (idleStand != null)
                            log.AppendLine($"    stands {StandGap(idleStand, motion) * 100f:F1} cm " +
                                           $"from {IdleState} — a cross-fade drags the feet that far");
                    }

                    worstArm = Mathf.Max(worstArm, delta.WorstArmDeg);
                    worstStance = Mathf.Max(worstStance, delta.WorstStanceDeg);
                    if (bindSpan > 0.01f)
                        worstSpanError = Mathf.Max(worstSpanError,
                                                   Mathf.Abs(delta.SkinSpan / bindSpan - 1f));

                    log.AppendLine($"  {state}: arms within {delta.WorstArmDeg:F1}°, " +
                                   $"rest within {delta.WorstOtherDeg:F1}° (worst: {delta.WorstLabel}); " +
                                   $"stance {delta.StanceTarget:F1}° vs source {delta.StanceSource:F1}° " +
                                   $"(worst frame {delta.WorstStanceDeg:F1}° apart); " +
                                   $"ankles at {delta.FootFloorTarget:F3} m; " +
                                   $"skin {delta.SkinDescription}");
                }

                // Informational only. A Mixamo animation-only download can be auto-mapped against
                // its own first frame rather than a T-pose, which shows up here as a stance the
                // character "fails" to copy while looking perfectly right on screen.
                log.AppendLine($"  worst stance gap against a source clip: {worstStance:F1}° " +
                               "(not judged — the source's own reference is not guaranteed)");

                bool armsOk = worstArm <= ArmToleranceDeg;
                // Only a failure when the import claims to have fixed it. With the switch off the
                // splay is a known, accepted property of the rig, not a defect to shout about.
                bool stanceOk = !StraightenLegsInTPose || referenceStance <= StanceToleranceDeg;
                bool skinOk = worstSpanError <= SkinSpanTolerance
                           && effectiveScale > 0.4f && effectiveScale < 2.5f;

                if (!skinOk)
                    log.AppendLine("  => SKIN BREAKS UNDER ANIMATION. The avatar's scale disagrees " +
                                   "with the mesh's bind pose, so the bones read fine while the " +
                                   "geometry is flung apart — invisible in Play, correct in edit " +
                                   "mode. Re-run the import menu: it rebuilds the avatar from " +
                                   "scratch, which is what clears this.");
                else if (!armsOk)
                    log.AppendLine($"  => ARMS DRIFT. Select {Path.GetFileName(def.BodyFbx)} ▸ Rig ▸ " +
                                   "Configure… ▸ Pose ▸ Enforce T-Pose ▸ Apply, then re-run this menu " +
                                   "— this rig binds with the arms down, and Unity's auto-mapper did " +
                                   "not correct it.");
                else if (!stanceOk)
                    log.AppendLine($"  => LEGS SPLAY. Muscle zero has the thighs {referenceStance:F0}° " +
                                   "apart, so every clip inherits a knock-kneed stance however the " +
                                   "animation is posed — this is the T-pose reference, not the " +
                                   "animation. Re-run the import menu: RepairAvatar straightens the " +
                                   "leg chain before the avatar is built.");
                else
                    log.AppendLine("  => Retarget is sound: the skin holds its size through every " +
                                   "clip and the arms land where the clip puts them." +
                                   (StraightenLegsInTPose
                                       ? " Muscle zero stands with the legs parallel."
                                       : $" Muscle zero keeps the rig's own {referenceStance:F0}° " +
                                         "knock-kneed bind, which is deliberate — straightening it " +
                                         "measures better and looks worse.") +
                                   " Any residual on legs and spine is the rigs' proportions, " +
                                   "which retargeting solves rather than copies.");

                if (skinOk && armsOk && stanceOk) Debug.Log(log.ToString());
                else Debug.LogWarning(log.ToString());
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Limb directions the fidelity check compares. The <c>arm</c> flag marks the
        /// ones that decide the verdict: a T-pose reference Unity failed to build shows up on the
        /// arms and only there, because it is the arms this rig binds out of position. Legs and
        /// spine are measured too, but they legitimately differ — retargeting solves a pose onto
        /// another body's proportions instead of copying joint angles.</summary>
        private static readonly (HumanBodyBones from, HumanBodyBones to, string label, bool arm)[] Limbs =
        {
            (HumanBodyBones.LeftUpperArm,  HumanBodyBones.LeftLowerArm,  "left upper arm",  true),
            (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, "right upper arm", true),
            (HumanBodyBones.LeftLowerArm,  HumanBodyBones.LeftHand,      "left forearm",    true),
            (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,     "right forearm",   true),
            (HumanBodyBones.LeftUpperLeg,  HumanBodyBones.LeftLowerLeg,  "left thigh",      false),
            (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, "right thigh",     false),
            // The calves were missing here, and with them the one thing that would show a leg
            // driven from the wrong joint: a thigh can read correct while everything below the
            // knee swings free.
            (HumanBodyBones.LeftLowerLeg,  HumanBodyBones.LeftFoot,      "left calf",       false),
            (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,     "right calf",      false),
            (HumanBodyBones.Hips,          HumanBodyBones.Head,          "spine",           false),
        };

        /// <summary>An arm this far off the source pose is still the same gesture. A broken T-pose
        /// reference on this rig would put them 40°+ out — nowhere near the boundary.</summary>
        private const float ArmToleranceDeg = 20f;

        /// <summary>How much wider than the source the character may stand. Retargeting onto
        /// different hip widths moves it a few degrees; a T-pose reference taken from a knock-kneed
        /// bind moved it by twenty, in every clip at once.</summary>
        private const float StanceToleranceDeg = 10f;

        /// <summary>How far the skin's overall span may move between the bind pose and an animated
        /// one. A pose change alters it a little (arms down are narrower than a T-pose); a bind
        /// pose the avatar disagrees with alters it by four orders of magnitude.</summary>
        private const float SkinSpanTolerance = 0.4f;

        /// <summary>How far one clip's pose lands from the source, split so the arms — the only
        /// limbs that expose a bad T-pose reference — can be judged apart from the rest.</summary>
        private sealed class PoseDelta
        {
            public float WorstArmDeg;
            public float WorstOtherDeg;
            public string WorstLabel;
            public string SkinDescription;
            public float SkinSpan;
            /// <summary>Lowest the character's ankles get during the clip, and where the source's
            /// ankles get once its height is scaled to this character's. A standing idle puts both
            /// on the floor; a gap between them is the character hanging above (or sunk into) the
            /// ground the clip was authored for.</summary>
            public float FootFloorTarget;
            public float FootFloorSource;
            /// <summary>Angle between the two thighs — how far apart the character stands. The
            /// number that separates "retargeting solved this onto different proportions" from
            /// "the avatar's T-pose reference has the legs apart, so every clip inherits it":
            /// proportions change where a limb <i>is</i>, a bad reference changes where muscle
            /// zero <i>points</i>, and the second shows up as the stance never closing.</summary>
            public float StanceTarget;
            public float StanceSource;
            /// <summary>Worst gap between the two stances on the same frame — the honest one.</summary>
            public float WorstStanceDeg;
        }

        /// <summary>Plays one clip on the character and on the Mixamo body it was authored on, and
        /// measures how far apart the two poses land. Directions are taken in root-local space, so
        /// the rigs' positions and sizes cancel and only the pose is compared. Null when the clip
        /// or its source model is missing.</summary>
        private static PoseDelta RetargetDelta(GameObject character, string clipFile)
        {
            string path = $"{AnimDir}/{clipFile}";
            var clip = LoadClip(path);
            var sourceModel = AssetDatabase.LoadMainAssetAtPath(path) as GameObject;
            if (clip == null || sourceModel == null) return null;

            var source = (GameObject)PrefabUtility.InstantiatePrefab(sourceModel);
            try
            {
                var a = character.GetComponent<Animator>();
                var b = source.GetComponent<Animator>();
                if (a == null || b == null || b.avatar == null || !b.avatar.isHuman) return null;

                var delta = new PoseDelta();
                delta.FootFloorTarget = float.MaxValue;
                delta.FootFloorSource = float.MaxValue;
                float sourceToTarget = SkinSpan(character) / Mathf.Max(SkinSpan(source), 1e-4f);

                float worstAny = 0f;
                foreach (float t in new[] { 0f, 0.33f, 0.66f })
                {
                    clip.SampleAnimation(character, clip.length * t);
                    clip.SampleAnimation(source, clip.length * t);

                    delta.FootFloorTarget = Mathf.Min(delta.FootFloorTarget, AnkleFloor(a));
                    delta.FootFloorSource = Mathf.Min(delta.FootFloorSource,
                                                      AnkleFloor(b) * sourceToTarget);
                    // Compared frame by frame. Each side's own widest moment can fall on a
                    // different frame, so two independent maxima invent a gap no single pose ever
                    // had — which is exactly how the first repair attempt read as a regression.
                    float stanceA = Stance(a), stanceB = Stance(b);
                    delta.StanceTarget = Mathf.Max(delta.StanceTarget, stanceA);
                    delta.StanceSource = Mathf.Max(delta.StanceSource, stanceB);
                    delta.WorstStanceDeg = Mathf.Max(delta.WorstStanceDeg, Mathf.Abs(stanceA - stanceB));

                    foreach (var (from, to, label, arm) in Limbs)
                    {
                        Vector3 da = LocalLimbDir(a, from, to);
                        Vector3 db = LocalLimbDir(b, from, to);
                        if (da == Vector3.zero || db == Vector3.zero) continue;

                        float angle = Vector3.Angle(da, db);
                        if (arm) delta.WorstArmDeg = Mathf.Max(delta.WorstArmDeg, angle);
                        else delta.WorstOtherDeg = Mathf.Max(delta.WorstOtherDeg, angle);

                        if (angle <= worstAny) continue;
                        worstAny = angle;
                        delta.WorstLabel = $"{label} {angle:F1}°";
                    }
                }
                delta.SkinDescription = DescribeSkin(character);
                delta.SkinSpan = SkinSpan(character);
                return delta;
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        /// <summary>The size and centre of the character's actual deformed geometry, baked from
        /// the pose it currently holds. Bone angles can read perfectly while the skin itself is
        /// broken — a bind pose that disagrees with the animated skeleton blows the mesh up or
        /// collapses it, and this is the only line in the report that would show it.</summary>
        private static string DescribeSkin(GameObject character)
        {
            var size = BakedSkinBounds(character)?.size;
            if (size == null) return "no skinned mesh";
            if (float.IsNaN(size.Value.x)) return "NaN vertices — skin is broken";

            return $"size {size.Value.x:F2}×{size.Value.y:F2}×{size.Value.z:F2} m";
        }

        /// <summary>The skin's largest dimension, in metres. Pose-independent enough to compare
        /// across clips — a T-pose's arm span and a standing figure's height are within a few per
        /// cent of each other — while a torn skin overshoots it by thousands.</summary>
        private static float SkinSpan(GameObject character)
        {
            var bounds = BakedSkinBounds(character);
            if (bounds == null) return 0f;

            Vector3 size = bounds.Value.size;
            if (float.IsNaN(size.x) || float.IsNaN(size.y) || float.IsNaN(size.z)) return 0f;
            return Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        }

        /// <summary>Bakes the character's skinned mesh as it is posed right now and returns the
        /// geometry's own bounds. Bone angles can read perfectly while the skin is broken, so this
        /// is the only measurement in the report that sees the mesh the player would see.</summary>
        private static Bounds? BakedSkinBounds(GameObject character)
        {
            var skin = character.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skin == null || skin.sharedMesh == null) return null;

            var baked = new Mesh();
            try
            {
                skin.BakeMesh(baked);
                return baked.bounds;
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
        }

        /// <summary>What a clip does to the character over its whole length, rather than at three
        /// sampled instants. The rig report answers "is this pose right"; a viewer watching an idle
        /// loop is asking something else entirely — whether the feet stay put, whether the hips
        /// hold still, and whether the loop closes. Legs that read as loose are almost always one
        /// of those three, and none of them shows up in a single frame.</summary>
        private sealed class MotionTrack
        {
            /// <summary>How far each ankle wanders horizontally, in the rig's own space. A planted
            /// idle is a centimetre or two; anything more is the foot skating along the floor.</summary>
            public float LeftSlide, RightSlide;
            /// <summary>How far the ankles come off the floor across the clip.</summary>
            public float FootLift;
            /// <summary>How much the hips rise and fall.</summary>
            public float HipsBob;
            /// <summary>Worst limb-direction jump between the clip's last frame and its first. A
            /// looping clip that does not close pops here once per cycle, which reads as a twitch
            /// rather than as the bad frame it is.</summary>
            public float LoopSeamDeg;
            public string LoopSeamLabel = "—";
            /// <summary>Where the clip parks the ankles on average, in the rig's own space. Two
            /// clips that stand in different places are the reason a cross-fade between them drags
            /// the feet across the floor — a defect that belongs to neither clip on its own, so no
            /// per-clip measurement can see it.</summary>
            public Vector3 LeftStand, RightStand;
        }

        /// <summary>Walks one clip densely and measures what the eye actually complains about.</summary>
        private static MotionTrack MeasureMotion(GameObject character, string clipFile, bool loops)
        {
            var clip = LoadClip($"{AnimDir}/{clipFile}");
            var animator = character.GetComponent<Animator>();
            if (clip == null || animator == null) return null;

            const int steps = 24;
            var track = new MotionTrack();
            var left = new List<Vector3>();
            var right = new List<Vector3>();
            var hips = new List<float>();

            for (int i = 0; i < steps; i++)
            {
                clip.SampleAnimation(character, clip.length * i / steps);
                left.Add(LocalBone(animator, HumanBodyBones.LeftFoot));
                right.Add(LocalBone(animator, HumanBodyBones.RightFoot));
                hips.Add(LocalBone(animator, HumanBodyBones.Hips).y);
            }

            track.LeftSlide  = HorizontalSpread(left);
            track.RightSlide = HorizontalSpread(right);
            track.LeftStand  = Average(left);
            track.RightStand = Average(right);
            track.FootLift   = Mathf.Max(VerticalSpread(left), VerticalSpread(right));
            track.HipsBob    = hips.Max() - hips.Min();

            if (loops)
            {
                // The seam the loop actually plays: the frame before the end, against the first.
                clip.SampleAnimation(character, clip.length * (steps - 1) / steps);
                var end = Limbs.Select(l => LocalLimbDir(animator, l.from, l.to)).ToArray();
                clip.SampleAnimation(character, 0f);

                for (int i = 0; i < Limbs.Length; i++)
                {
                    Vector3 start = LocalLimbDir(animator, Limbs[i].from, Limbs[i].to);
                    if (start == Vector3.zero || end[i] == Vector3.zero) continue;

                    float angle = Vector3.Angle(end[i], start);
                    if (angle <= track.LoopSeamDeg) continue;
                    track.LoopSeamDeg = angle;
                    track.LoopSeamLabel = Limbs[i].label;
                }
            }
            return track;
        }

        private static Vector3 LocalBone(Animator animator, HumanBodyBones bone)
        {
            var t = animator.GetBoneTransform(bone);
            return t == null ? Vector3.zero : animator.transform.InverseTransformPoint(t.position);
        }

        private static float HorizontalSpread(List<Vector3> points)
        {
            if (points.Count == 0) return 0f;
            float x = points.Max(p => p.x) - points.Min(p => p.x);
            float z = points.Max(p => p.z) - points.Min(p => p.z);
            return Mathf.Max(x, z);
        }

        private static float VerticalSpread(List<Vector3> points)
            => points.Count == 0 ? 0f : points.Max(p => p.y) - points.Min(p => p.y);

        /// <summary>How far apart two clips park the feet — the worse of the two ankles.</summary>
        private static float StandGap(MotionTrack a, MotionTrack b)
            => Mathf.Max(Vector3.Distance(a.LeftStand, b.LeftStand),
                         Vector3.Distance(a.RightStand, b.RightStand));

        private static Vector3 Average(List<Vector3> points)
        {
            if (points.Count == 0) return Vector3.zero;

            var sum = Vector3.zero;
            foreach (var p in points) sum += p;
            return sum / points.Count;
        }

        /// <summary>Limb directions at a model's own T-pose reference, in its root's space.
        ///
        /// <para>The thing to compare two rigs by. Retargeting carries muscle values, and a muscle
        /// value is an angle measured from the rig's T-pose — so two rigs only agree on what a pose
        /// means to the extent their T-poses agree. Straightening the target's legs to an idealised
        /// vertical closes most of the gap and then leaves whatever the <i>source</i> rig's own
        /// T-pose is away from vertical as a fresh, self-inflicted bias.</para></summary>
        private static Dictionary<HumanBodyBones, Vector3> ReferenceDirections(
            string modelPath, out float stance)
            => ReferenceDirections(modelPath, out stance, out _);

        /// <summary>As above, and also the bones' full orientations at that T-pose.
        ///
        /// <para>Direction is only half of what a rig's reference says. A bone aimed correctly can
        /// still be rolled about its own length, and for the ankle that roll <i>is</i> the
        /// toe-in/toe-out axis: get it wrong and Unity's foot-twist muscle maps onto a frame the
        /// source never used, so the foot rotates about itself through the whole clip — a motion
        /// that is in neither the animation nor the model.</para></summary>
        private static Dictionary<HumanBodyBones, Vector3> ReferenceDirections(
            string modelPath, out float stance, out Dictionary<HumanBodyBones, Quaternion> rotations)
        {
            stance = 0f;
            var directions = new Dictionary<HumanBodyBones, Vector3>();
            rotations = new Dictionary<HumanBodyBones, Quaternion>();

            var model = AssetDatabase.LoadMainAssetAtPath(modelPath) as GameObject;
            if (model == null || AssetImporter.GetAtPath(modelPath) is not ModelImporter importer)
                return directions;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                var animator = instance.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                    return directions;

                var byName = new Dictionary<string, Transform>();
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                    if (!byName.ContainsKey(t.name)) byName.Add(t.name, t);
                ApplySkeleton(importer.humanDescription.skeleton, byName);

                foreach (var (from, to, _, _) in TPoseChains)
                {
                    var direction = LocalLimbDir(animator, from, to);
                    if (direction != Vector3.zero) directions[from] = direction;

                    var bone = animator.GetBoneTransform(from);
                    if (bone != null)
                        rotations[from] = Quaternion.Inverse(animator.transform.rotation) * bone.rotation;
                }
                stance = Stance(animator);
                return directions;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>The stance the avatar calls muscle zero — the pose every clip is measured
        /// from, and the one place a knock-kneed bind does its damage.</summary>
        private static float ReferenceStance(CharacterDef def)
        {
            var prefab = LoadCharacterPrefab(def.Gender);
            if (prefab == null) return 0f;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var animator = instance.GetComponent<Animator>();
                if (animator == null) return 0f;

                PoseAtReference(def, instance);
                return Stance(animator);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static string Describe(Vector3 direction)
            => $"({direction.x:F2},{direction.y:F2},{direction.z:F2})";

        /// <summary>Angle between the two thighs, in degrees. Zero is a figure standing with its
        /// legs parallel; a splayed bind pose leaks through as a stance that never closes.</summary>
        private static float Stance(Animator animator)
        {
            Vector3 left  = LocalLimbDir(animator, HumanBodyBones.LeftUpperLeg,  HumanBodyBones.LeftLowerLeg);
            Vector3 right = LocalLimbDir(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg);
            if (left == Vector3.zero || right == Vector3.zero) return 0f;
            return Vector3.Angle(left, right);
        }

        /// <summary>Height of the lower ankle above the rig's own root, in the root's space.
        /// Both figures stand at the origin with the root on the floor, so this is how far off the
        /// ground the character's feet are in the pose it currently holds.</summary>
        private static float AnkleFloor(Animator animator)
        {
            float floor = float.MaxValue;
            foreach (var bone in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
            {
                var foot = animator.GetBoneTransform(bone);
                if (foot == null) continue;
                floor = Mathf.Min(floor, animator.transform.InverseTransformPoint(foot.position).y);
            }
            return floor == float.MaxValue ? 0f : floor;
        }

        /// <summary>Every humanoid slot and the rig bone Unity put in it. The one line that
        /// settles a bad retarget: an auto-mapper that picked a twist bone for the knee, or left a
        /// finger joint empty, is invisible in every other measurement here.</summary>
        private static string BoneMap(Animator animator)
        {
            var lines = new List<string>();
            var row = new List<string>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = (HumanBodyBones)i;
                var t = animator.GetBoneTransform(bone);
                row.Add($"{bone}={(t == null ? "—" : t.name)}");
                if (row.Count < 4) continue;

                lines.Add("    " + string.Join("  ", row));
                row.Clear();
            }
            if (row.Count > 0) lines.Add("    " + string.Join("  ", row));
            return string.Join("\n", lines);
        }

        /// <summary>Direction from one humanoid bone to another, expressed in the rig root's own
        /// space so two differently placed and differently sized characters stay comparable.</summary>
        private static Vector3 LocalLimbDir(Animator animator, HumanBodyBones from, HumanBodyBones to)
        {
            var a = animator.GetBoneTransform(from);
            var b = animator.GetBoneTransform(to);
            if (a == null || b == null) return Vector3.zero;

            Vector3 dir = animator.transform.InverseTransformDirection(b.position - a.position);
            return dir.sqrMagnitude < 1e-8f ? Vector3.zero : dir.normalized;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>The stylised look: flat cartoon shading and a drawn black outline. Written
        /// for the built-in pipeline, so it is only handed out while the project is actually on it
        /// — assigning a URP asset in Graphics settings drops the character back to plain lit
        /// rather than to magenta, and re-running the import menu is all it takes to re-apply
        /// whichever of the two fits.</summary>
        public static Shader CharacterShader()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                var toon = Shader.Find(ToonShaderName);
                if (toon != null) return toon;
            }
            return LitShader();
        }

        /// <summary>The plain lit shader for anything that isn't the stylised character — the
        /// blockman placeholder, the Mixamo stand-in body. URP/Lit draws magenta under the built-in
        /// pipeline and Standard draws magenta under URP, so the choice follows the pipeline asset
        /// actually assigned in Graphics settings — which in this project is none, whatever the URP
        /// packages in the manifest suggest.</summary>
        public static Shader LitShader()
        {
            var shader = GraphicsSettings.currentRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Lit")
                : Shader.Find("Standard");
            return shader != null ? shader : Shader.Find("Standard");
        }

        /// <summary>True when a material's shader belongs to the pipeline currently rendering the
        /// project. False means it would draw magenta and needs replacing.</summary>
        public static bool RendersInThisPipeline(Material material)
        {
            if (material == null || material.shader == null) return false;

            bool scriptablePipeline = GraphicsSettings.currentRenderPipeline != null;
            bool urpShader = material.shader.name.StartsWith("Universal Render Pipeline");
            return scriptablePipeline == urpShader;
        }

        /// <summary>Paints a character material: the albedo under whichever name the chosen
        /// shader uses, leaving every look-defining property alone.</summary>
        public static void ApplyCharacterSurface(Material material, Texture albedo)
        {
            if (material == null) return;

            if (albedo != null)
            {
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", albedo);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", albedo);
            }
            else
            {
                var fallback = new Color(0.75f, 0.75f, 0.78f);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", fallback);
                if (material.HasProperty("_Color"))     material.SetColor("_Color", fallback);
            }

            // Only meaningful on the plain lit fallback: the Tripo bake already has light and
            // occlusion painted into the albedo, so any gloss on top reads as plastic. The toon
            // shader has none of these properties — its look lives in the material's own outline
            // and shade settings, which a re-import must leave exactly as the artist tuned them.
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.1f);
            if (material.HasProperty("_Metallic"))   material.SetFloat("_Metallic", 0f);
        }

        /// <summary>Loads one character's prefab, falling back to the raw FBX when the import tool
        /// has not been run yet. Null when the character is not in the project.</summary>
        public static GameObject LoadCharacterPrefab(CharacterGender gender = CharacterGender.Male)
        {
            var def = Definition(gender);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(def.PrefabPath);
            if (prefab != null) return prefab;
            return AssetDatabase.LoadMainAssetAtPath(def.BodyFbx) as GameObject;
        }

        private static AnimationClip LoadClip(string fbxPath)
            => AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                            .OfType<AnimationClip>()
                            .FirstOrDefault(c => !c.name.StartsWith("__preview"));

        private static Bounds InstanceBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[MainCharacter] {message}");
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("Push Stars — Main Character", message, "OK");
        }
    }
}
