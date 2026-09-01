using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>
    /// Lets specific Main.unity elements be dragged/resized by hand in the Scene view and have it
    /// stick — even though <see cref="MainVsScreenSetup.BuildScene"/> wipes and regenerates the
    /// whole screen on every rebuild.
    ///
    /// <para><b>The problem this closes.</b> The bottom-nav plate and tab positions were hand-tuned
    /// in the Editor, then had to be read back out of a screenshot and typed into
    /// <see cref="MainVsScreenSetup"/> by hand before the next rebuild would keep them — a full
    /// round-trip through conversation for a number that was already sitting in the Inspector. For
    /// anything on <see cref="TrackedPaths"/>, that round-trip is gone: move it, press Ctrl+S (or
    /// File ▸ Save), and the new numbers are written to disk as part of that same save — no rebuild,
    /// no menu item, nobody to ask. The next rebuild, whenever and for whatever reason it happens,
    /// puts them right back.</para>
    ///
    /// <para><b>Why a whitelist, not "capture everything."</b> An override recorded once always wins
    /// over whatever the code says at build time — that is the whole point. Recording every
    /// RectTransform in the scene would mean a future, deliberate layout change in code silently
    /// stops doing anything the moment it touches an object that was ever captured, with no error and
    /// no obvious symptom beyond "I changed the number but nothing moved." Keeping the list short and
    /// explicit means only the handful of elements actually meant to be hand-owned behave that way;
    /// everything else stays exactly as governed by code, same as before this file existed.</para>
    ///
    /// <para><b>Adding to the list:</b> append its path (as printed by Tools ▸ Push Stars ▸ Dump Main
    /// Screen Layout, relative to MainCanvas) to <see cref="TrackedPaths"/>. It starts being captured
    /// on the next save and applied on the next rebuild.</para>
    /// </summary>
    static class MainVsLayoutOverrides
    {
        const string OverridesPath = "Assets/_Project/Editor/MainVsLayoutOverrides.json";
        const string MainScenePath = "Assets/_Project/Scenes/Main.unity";

        /// <summary>Paths (relative to MainCanvas, same format as Dump Main Screen Layout) of the
        /// elements the Scene view is allowed to own. Everything else is governed by
        /// <see cref="MainVsScreenSetup"/> alone, as before — the bottom nav plate/tabs and the three
        /// action-row plates plus their icons, exactly the elements that have needed hand-tuning
        /// so far.</summary>
        static readonly string[] TrackedPaths =
        {
            "MirrorRoot/SafeArea/BottomNav/Bg",
            "MirrorRoot/SafeArea/BottomNav/Nav_League",
            "MirrorRoot/SafeArea/BottomNav/Nav_Duel",
            "MirrorRoot/SafeArea/BottomNav/Nav_Profile",
            "MirrorRoot/SafeArea/DuelPanel/ActionRow/PvpButton",
            "MirrorRoot/SafeArea/DuelPanel/ActionRow/PvpButton/Icon",
            "MirrorRoot/SafeArea/DuelPanel/ActionRow/BattleButton",
            "MirrorRoot/SafeArea/DuelPanel/ActionRow/PushupButton",
            "MirrorRoot/SafeArea/DuelPanel/ActionRow/PushupButton/Icon",
        };

        [Serializable]
        class Entry
        {
            public string path;
            public float posX, posY;
            public float sizeX, sizeY;
            public float scaleX = 1, scaleY = 1, scaleZ = 1;
        }

        [Serializable]
        class Table
        {
            public List<Entry> entries = new List<Entry>();
        }

        // ── Auto-capture on save ─────────────────────────────────────────────────
        // A stock AssetModificationProcessor: Unity calls OnWillSaveAssets with every asset about to
        // be written, right before it writes them. Returning the array unmodified lets the save
        // proceed exactly as asked — this only piggybacks a second, unrelated write (the JSON file)
        // onto the same keystroke.
        class SaveHook : AssetModificationProcessor
        {
            static string[] OnWillSaveAssets(string[] paths)
            {
                foreach (var p in paths)
                {
                    if (p == MainScenePath) { Capture(); break; }
                }
                return paths;
            }
        }

        [MenuItem("Tools/Push Stars/Main VS Screen/Capture Layout Overrides Now", priority = 22)]
        static void CaptureFromMenu() => Capture();

        /// <summary>Reads the tracked objects out of the currently open scene and writes their
        /// current numbers to disk. Silently does nothing to any path it can't find — the open scene
        /// might not be Main.unity, or an object might not exist yet under a name on the list.</summary>
        public static void Capture()
        {
            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (canvas == null) return; // Main.unity isn't the open scene right now

            var table = new Table();
            foreach (var path in TrackedPaths)
            {
                var rt = FindByPath(canvas.transform, path) as RectTransform;
                if (rt == null) continue; // not built yet on this screen — fine, just skip it

                table.entries.Add(new Entry
                {
                    path   = path,
                    posX   = rt.anchoredPosition.x,
                    posY   = rt.anchoredPosition.y,
                    sizeX  = rt.sizeDelta.x,
                    sizeY  = rt.sizeDelta.y,
                    scaleX = rt.localScale.x,
                    scaleY = rt.localScale.y,
                    scaleZ = rt.localScale.z,
                });
            }

            if (table.entries.Count == 0) return;

            File.WriteAllText(OverridesPath, JsonUtility.ToJson(table, true));
            Debug.Log($"[LayoutOverrides] Captured {table.entries.Count} element(s) to {OverridesPath}.");
        }

        /// <summary>Re-applies whatever was last captured on top of a freshly built scene. Called
        /// once, at the end of <see cref="MainVsScreenSetup.BuildScene"/> — after every object exists
        /// with its code-driven default, so there is always something to find and overwrite. A
        /// rebuild with nothing captured yet (a clean checkout, or nothing on the list has ever been
        /// touched) is a silent no-op — code's own defaults stand exactly as before this file
        /// existed.</summary>
        public static void Apply(Transform canvasRoot)
        {
            if (!File.Exists(OverridesPath)) return;

            Table table;
            try { table = JsonUtility.FromJson<Table>(File.ReadAllText(OverridesPath)); }
            catch (Exception e)
            {
                Debug.LogWarning($"[LayoutOverrides] Could not read {OverridesPath} — skipped ({e.Message}).");
                return;
            }
            if (table?.entries == null) return;

            int applied = 0;
            foreach (var e in table.entries)
            {
                var rt = FindByPath(canvasRoot, e.path) as RectTransform;
                if (rt == null)
                {
                    Debug.LogWarning($"[LayoutOverrides] '{e.path}' not found in the rebuilt scene — " +
                                     "skipped (renamed or removed since it was captured?).");
                    continue;
                }
                rt.anchoredPosition = new Vector2(e.posX, e.posY);
                rt.sizeDelta        = new Vector2(e.sizeX, e.sizeY);
                rt.localScale       = new Vector3(e.scaleX, e.scaleY, e.scaleZ);
                applied++;
            }
            if (applied > 0)
                Debug.Log($"[LayoutOverrides] Re-applied {applied} hand-tuned element(s) from {OverridesPath}.");
        }

        static Transform FindByPath(Transform root, string path)
        {
            var current = root;
            foreach (var part in path.Split('/'))
            {
                current = current.Find(part);
                if (current == null) return null;
            }
            return current;
        }
    }
}
