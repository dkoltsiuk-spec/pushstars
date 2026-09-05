using System.IO;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>
    /// Runs a setup tool once, on the next compile, without anybody having to click a menu.
    ///
    /// <para><b>Why this exists.</b> A new AccuRig export lands on disk while the editor is open
    /// and idle. Unity only notices on its next refresh, and the rig work that has to follow —
    /// clearing the cached humanoid description, rebuilding the avatar, extracting the embedded
    /// maps, retargeting the clips — is a menu item somebody has to remember to click. This closes
    /// that gap: the import runs itself as soon as the editor comes back to life.</para>
    ///
    /// <para>Armed by a sentinel in <c>Temp/</c>, which Unity wipes on startup, so a task can only
    /// ever fire for the refresh it was armed for — nothing here runs by itself on a normal
    /// reload. The scene rebuild is armed separately and never by the import, because it
    /// regenerates <c>Main.unity</c> from scratch and would take unsaved edits with it.</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorTaskBootstrap
    {
        private const string Sentinel = "Temp/pushstars.editor-task";

        /// <summary>Sentinel line → the tool it runs. Unknown lines are reported, not guessed at.</summary>
        private const string TaskImportCharacters = "import-characters";
        private const string TaskReportRigs        = "report-rigs";
        private const string TaskContactSheet     = "contact-sheet";
        private const string TaskFilmstrip       = "filmstrip";
        private const string TaskBuildMainVs      = "build-main-vs";
        private const string TaskRebuildFlow      = "rebuild-flow-scenes";
        private const string TaskConfigureSprites = "configure-sprites";
        private const string TaskRetargetRegression = "retarget-regression";

        private static double _nextPoll;

        static EditorTaskBootstrap()
        {
            if (File.Exists(Sentinel)) EditorApplication.delayCall += Run;

            // Also watched on a timer, not only on this reload. The sentinel is written by
            // something outside the editor, and a domain reload is not something that process can
            // arrange — waiting for one meant an armed task sat there until a script happened to
            // change, which looks exactly like the task having silently failed.
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 1.0;

            if (File.Exists(Sentinel)) Run();
        }

        [MenuItem("Tools/Push Stars/Character/Arm auto-import on next refresh", priority = 322)]
        private static void ArmImport() => Arm(TaskImportCharacters);

        private static void Arm(string task)
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Sentinel, task);
            Debug.Log($"[EditorTask] '{task}' armed — it runs on the next script compile.");
        }

        private static void Run()
        {
            // A tool kicked off mid-compile reads assemblies that are being replaced under it.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Run;
                return;
            }

            if (!File.Exists(Sentinel)) return;
            string[] tasks = File.ReadAllLines(Sentinel);

            // Disarm before doing the work, never after: a tool that throws would otherwise
            // re-arm itself into the next reload and keep throwing.
            File.Delete(Sentinel);

            foreach (string line in tasks)
            {
                string task = line.Trim();
                if (task.Length == 0) continue;

                switch (task)
                {
                    case TaskImportCharacters:
                        Debug.Log("[EditorTask] Importing main characters …");
                        MainCharacterSetup.ImportAll();
                        break;

                    case TaskReportRigs:
                        Debug.Log("[EditorTask] Reporting character rigs …");
                        MainCharacterSetup.ReportAll();
                        break;

                    case TaskContactSheet:
                        Debug.Log("[EditorTask] Rendering character contact sheets …");
                        CharacterContactSheet.RenderAll();
                        break;

                    case TaskFilmstrip:
                        Debug.Log("[EditorTask] Rendering clip filmstrips …");
                        CharacterContactSheet.RenderFilmstrips();
                        break;

                    case TaskRebuildFlow:
                        // Same hazard as the Main rebuild below, for the same reason: these scenes
                        // are regenerated from scratch and take any unsaved edits with them.
                        Debug.Log("[EditorTask] Rebuilding Boot + Onboarding + Fight …");
                        BuildScript.RebuildFlowScenes();
                        break;

                    case TaskBuildMainVs:
                        Debug.Log("[EditorTask] Rebuilding the Main VS screen …");
                        // RunHeadless, not Run: Run's success dialog has no one to click it here,
                        // and blocks any task queued after it in the same sentinel pass forever.
                        MainVsScreenSetup.RunHeadless(out _);
                        break;

                    case TaskConfigureSprites:
                        Debug.Log("[EditorTask] Configuring new Figma sprites …");
                        SpriteImporter.ConfigureAll();
                        break;

                    case TaskRetargetRegression:
                        Debug.Log("[EditorTask] Validating avatar retargeting in isolated preview scenes …");
                        RetargetRegression.Run();
                        break;

                    default:
                        Debug.LogWarning($"[EditorTask] Unknown task '{task}' — ignored.");
                        break;
                }
            }
        }
    }
}
