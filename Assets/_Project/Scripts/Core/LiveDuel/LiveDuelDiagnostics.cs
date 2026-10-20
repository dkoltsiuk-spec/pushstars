using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PushStars.Core
{
    /// <summary>
    /// Finds out where a phone froze in a live duel. The duel marks its steps
    /// (<see cref="Mark"/>); a background timer watches the main thread while a live link is open,
    /// and if the main thread stops for <see cref="HangSec"/> it writes the last step to disk. A
    /// frozen app cannot report anything itself, so the next launch sends it to analytics as
    /// <c>live_duel_hang</c> (step, how long the main thread had been stuck, scene, seconds since
    /// the step).
    /// </summary>
    public static class LiveDuelDiagnostics
    {
        public const float HangSec = 8f, LongHangSec = 20f;
        private static string _step = "", _scene = "", _path;
        private static int _stepAt, _beatAt;
        private static Timer _watch;
        private static bool _written;

        /// <summary>Records the duel's current step (main thread).</summary>
        public static void Mark(string step)
        {
            Volatile.Write(ref _step, step ?? "");
            Volatile.Write(ref _stepAt, Environment.TickCount);
        }

        /// <summary>Called every frame while a live link is pumped: the main thread is alive.</summary>
        public static void Beat()
        {
            int now = Environment.TickCount, last = Volatile.Read(ref _beatAt);
            Volatile.Write(ref _beatAt, now);
            if (_written) Recovered(now - last);
            if (_watch != null) return;
            _path ??= Path.Combine(Application.persistentDataPath, "live-duel-hang.txt");
            _scene = SceneManager.GetActiveScene().name;
            SceneManager.activeSceneChanged -= OnScene;
            SceneManager.activeSceneChanged += OnScene;
            _written = false;
            _watch = new Timer(Watch, null, 1000, 1000);
        }

        /// <summary>The main thread came back. A short stall (a slow scene load) is no freeze and
        /// is forgotten; a long one is kept, marked as recovered.</summary>
        private static void Recovered(int stalledMs)
        {
            _written = false;
            try
            {
                if (!File.Exists(_path)) return;
                if (stalledMs < LongHangSec * 1000) File.Delete(_path);
                else File.AppendAllText(_path, "\nrecovered:" + stalledMs / 1000);
            }
            catch (Exception) { }
        }

        /// <summary>The live link is gone, or the app went to the background: stop watching.</summary>
        public static void Stop()
        {
            _watch?.Dispose();
            _watch = null;
        }

        private static void OnScene(Scene from, Scene to) => Volatile.Write(ref _scene, to.name ?? "");

        private static void Watch(object _)
        {
            try
            {
                int now = Environment.TickCount;
                int stalled = now - Volatile.Read(ref _beatAt);
                if (_written || stalled < HangSec * 1000) return;
                _written = true;
                File.WriteAllText(_path, string.Join("\n", Volatile.Read(ref _step), stalled / 1000,
                    Volatile.Read(ref _scene), (now - Volatile.Read(ref _stepAt)) / 1000));
            }
            catch (Exception) { /* a diagnostic must never hurt */ }
        }

        /// <summary>Sends a hang recorded by a previous run, once.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ReportPrevious()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "live-duel-hang.txt");
                if (!File.Exists(path)) return;
                var lines = File.ReadAllText(path).Split('\n');
                File.Delete(path);
                Analytics.Track("live_duel_hang", new Dictionary<string, object>
                {
                    ["step"] = lines.Length > 0 ? lines[0] : "",
                    ["stalled_sec"] = lines.Length > 1 && int.TryParse(lines[1], out int s) ? s : 0,
                    ["scene"] = lines.Length > 2 ? lines[2] : "",
                    ["since_step_sec"] = lines.Length > 3 && int.TryParse(lines[3], out int t) ? t : 0,
                    ["recovered"] = lines.Length > 4 ? lines[4] : "no",
                });
                Debug.LogWarning("[LiveDuel] The previous run froze in a live duel at: " + (lines.Length > 0 ? lines[0] : "?"));
            }
            catch (Exception e) { Debug.LogWarning("[LiveDuel] hang report: " + e.Message); }
        }
    }
}
