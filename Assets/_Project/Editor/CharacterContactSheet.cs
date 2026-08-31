using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>
    /// Renders each character in each of its clips to a single PNG contact sheet.
    ///
    /// <para><b>Why measurements were not enough.</b> The rig report compares bone directions and
    /// baked skin bounds, and both can read perfectly while the character still looks wrong: bone
    /// angles say nothing about how the mesh is weighted to those bones, and the skin's overall
    /// bounds barely move when one limb deforms badly. A picture of the posed character is the
    /// only check that sees what the player sees.</para>
    ///
    /// Menu: Tools → Push Stars → Character → Contact sheet (render every clip).
    /// </summary>
    public static class CharacterContactSheet
    {
        private const int CellW = 260, CellH = 400;
        private static readonly float[] Samples = { 0f, 0.33f, 0.66f };

        /// <summary>Label, camera height, and distance. The second framing exists because a leg
        /// that is 10° out of place is two pixels wide in a full-figure shot.</summary>
        private static readonly (string name, float height, float distance)[] Framings =
        {
            ("full", 0.95f, 3.9f),
            ("legs", 0.45f, 1.7f),
        };

        /// <summary>Written outside Assets on purpose: these are throwaway diagnostics, and a PNG
        /// under Assets would be imported, given a GUID and committed by accident.</summary>
        private static string OutDir =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Temp", "ContactSheets");

        [MenuItem("Tools/Push Stars/Character/Contact sheet (render every clip)", priority = 323)]
        public static void RenderAll()
        {
            Directory.CreateDirectory(OutDir);
            foreach (var def in MainCharacterSetup.Characters)
                foreach (var framing in Framings)
                    Render(def, framing);
            Debug.Log($"[ContactSheet] Written to {OutDir}");
        }

        /// <summary>One clip walked across its whole length, a frame per column.
        ///
        /// <para>The contact sheet answers "is this pose right" and that turned out to be the wrong
        /// question: an idle can be correct in every sampled instant and still look loose while it
        /// plays, because what the eye objects to is the movement between the frames — feet that
        /// skate, hips that bob, a loop that does not close. Three columns cannot show that; a
        /// strip across the whole cycle can.</para></summary>
        [MenuItem("Tools/Push Stars/Character/Filmstrip (one clip across its cycle)", priority = 325)]
        public static void RenderFilmstrips()
        {
            Directory.CreateDirectory(OutDir);
            var frames = new float[10];
            for (int i = 0; i < frames.Length; i++) frames[i] = (float)i / frames.Length;

            foreach (var def in MainCharacterSetup.Characters)
                Render(def, ("legs", 0.5f, 2.1f), frames, "filmstrip", animatedOnly: true);
            Debug.Log($"[ContactSheet] Filmstrips written to {OutDir}");
        }

        private static void Render(MainCharacterSetup.CharacterDef def,
                                   (string name, float height, float distance) framing)
            => Render(def, framing, Samples, framing.name, animatedOnly: false);

        private static void Render(MainCharacterSetup.CharacterDef def,
                                   (string name, float height, float distance) framing,
                                   float[] samples, string suffix, bool animatedOnly)
        {
            var prefab = MainCharacterSetup.LoadCharacterPrefab(def.Gender);
            if (prefab == null) { Debug.LogWarning($"[ContactSheet] {def.Name}: not imported."); return; }

            // Parked far from anything else in the open scene, so a plain camera pointed at it
            // sees the character and nothing else — no layer juggling, no scene of its own.
            var origin = new Vector3(1000f, 0f, 0f);
            var character = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            character.transform.position = origin;
            // Turned to face the lens. These rigs face +Z, and the camera looks that way too, so
            // an unrotated character shows the sheet nothing but his back — the fingers and the
            // knees, the two things worth looking at, are both on the other side.
            character.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            var lightGo = new GameObject("ContactSheetLight");
            lightGo.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;

            var camGo = new GameObject("ContactSheetCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.13f, 0.13f, 0.17f);
            cam.fieldOfView     = 30f;
            cam.nearClipPlane   = 0.05f;
            cam.farClipPlane    = 50f;

            var skins = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Debug.Log($"[ContactSheet] {def.Name}: {skins.Length} skinned mesh(es) — " +
                      string.Join(", ", skins.Select(s => $"{s.name}({s.bones.Length} bones, " +
                                                          $"{s.sharedMesh.vertexCount} verts)")));

            var clips = ClipsOf(def);
            if (animatedOnly) clips = clips.Where(c => c.clip != null).ToArray();
            var sheet = new Texture2D(CellW * samples.Length, CellH * clips.Length, TextureFormat.RGB24, false);

            for (int row = 0; row < clips.Length; row++)
            {
                // A fresh body per row. Posing one instance through the whole sheet looked like an
                // obvious saving and quietly corrupted it: writing the T-pose reference onto the
                // transforms leaves a hierarchy that SampleAnimation no longer fully overrides, so
                // every row after it rendered the T-pose while claiming to be a clip — a sheet that
                // is wrong in exactly the way it is meant to detect.
                Object.DestroyImmediate(character);
                character = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                character.transform.position = origin;
                character.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

                for (int col = 0; col < samples.Length; col++)
                {
                    var (name, clip) = clips[row];
                    // "bind" is the mesh as it was skinned; "tpose" is where the avatar puts muscle
                    // zero. The pair separates a modelling problem from a retargeting one.
                    if (clip != null) clip.SampleAnimation(character, clip.length * samples[col]);
                    else if (name == "tpose") MainCharacterSetup.PoseAtReference(def, character);

                    var cell = Shoot(cam, origin, framing);
                    // Rows top-down in the sheet; Texture2D's origin is bottom-left.
                    sheet.SetPixels(col * CellW, (clips.Length - 1 - row) * CellH, CellW, CellH,
                                    cell.GetPixels());
                    Object.DestroyImmediate(cell);
                }
            }
            sheet.Apply();

            string path = Path.Combine(OutDir, $"{def.Name}-{suffix}.png");
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Debug.Log($"[ContactSheet] {def.Name}: rows top-to-bottom = " +
                      string.Join(", ", clips.Select(c => c.name)) + $" → {path}");

            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
            Object.DestroyImmediate(character);
        }

        /// <summary>Bind pose first, then every clip the controller carries — the bind row is the
        /// reference: a limb already wrong there is a skinning problem, not a retarget one.</summary>
        private static (string name, AnimationClip clip)[] ClipsOf(MainCharacterSetup.CharacterDef def)
        {
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                def.ControllerPath);
            var states = controller == null
                ? new (string, AnimationClip)[0]
                : controller.layers[0].stateMachine.states
                            .Select(s => (s.state.name, s.state.motion as AnimationClip))
                            .Where(s => s.Item2 != null)
                            .ToArray();

            return new (string, AnimationClip)[] { ("tpose", null), ("bind", null) }
                   .Concat(states).ToArray();
        }

        /// <summary>One frame, framed on the character as he currently stands.</summary>
        private static Texture2D Shoot(Camera cam, Vector3 origin,
                                       (string name, float height, float distance) framing)
        {
            cam.transform.position = origin + new Vector3(0f, framing.height, -framing.distance);
            cam.transform.LookAt(origin + new Vector3(0f, framing.height, 0f));

            var rt = new RenderTexture(CellW, CellH, 24) { antiAliasing = 4 };
            var previous = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                var shot = new Texture2D(CellW, CellH, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, CellW, CellH), 0, 0);
                shot.Apply();
                return shot;
            }
            finally
            {
                RenderTexture.active = previous;
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
