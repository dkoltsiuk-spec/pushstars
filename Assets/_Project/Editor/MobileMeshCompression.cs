using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>
    /// Turns on mesh compression for the character and boss models.
    ///
    /// <para>The Tripo/AccuRig bodies are ~50k vertices each and were imported with compression
    /// Off: about 77 MB of mesh data in the player. Low quantizes positions, normals and UVs on
    /// disk only (bone weights untouched) and cut a hero from 6.3 to 4.1 MB, a golem from 8.9 to
    /// 6.4 MB; Medium and High saved only a few percent more, so the gentlest setting it is.</para>
    ///
    /// <para>Model files only, and only those still at Off. Standalone .asset meshes are left
    /// alone: setting compression on one rewrites the source asset itself in quantized form.</para>
    ///
    /// <para>Not run by the build: a reimport from script re-serializes the whole importer, and the
    /// golems' .meta files then gain the auto-mapped humanoid description they never stored (the
    /// avatar-cache trap). Run it by hand for new models and check that the .meta diff is just
    /// <c>meshCompression: 1</c>.</para>
    ///
    /// Menu: Tools → Push Stars → Compress Meshes For Mobile.
    /// </summary>
    public static class MobileMeshCompression
    {
        private static readonly string[] Roots = { "Assets/_Project", "Assets/Character" };

        private const ModelImporterMeshCompression Level = ModelImporterMeshCompression.Low;

        [MenuItem("Tools/Push Stars/Compress Meshes For Mobile", priority = 205)]
        public static void Apply()
        {
            var changed = new List<string>();
            var seen = new HashSet<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Mesh", Roots))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!seen.Add(path)) continue;
                    if (!(AssetImporter.GetAtPath(path) is ModelImporter model)) continue;
                    if (model.meshCompression != ModelImporterMeshCompression.Off) continue;
                    model.meshCompression = Level;
                    model.SaveAndReimport();
                    changed.Add(path);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            Debug.Log($"[MobileMeshCompression] {Level} compression for {changed.Count} model(s)" +
                      (changed.Count > 0 ? ":\n" + string.Join("\n", changed) : "."));
        }
    }
}
