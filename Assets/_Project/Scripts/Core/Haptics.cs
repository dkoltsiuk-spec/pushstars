using System;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>
    /// One-call tactile feedback for UI touch. <see cref="Selection"/> is the light "tick" the
    /// bottom-nav tabs fire on tap; <see cref="Impact"/> (and the <see cref="Light"/> /
    /// <see cref="Medium"/> / <see cref="Heavy"/> shorthands) are weightier knocks for confirms and
    /// duel beats as those screens adopt them.
    ///
    /// <para>Honours the Settings "Вибрация" switch through <see cref="ISettingsStore"/> — the same
    /// flag the toggle writes — so switching it off silences every call here with no extra wiring.
    /// The flag is read live: the toggle saves on change and taps are far too rare for the read to
    /// cost anything.</para>
    ///
    /// <para><b>Per platform.</b> iOS drives the Taptic Engine (<c>UISelectionFeedbackGenerator</c> /
    /// <c>UIImpactFeedbackGenerator</c>) through the small <c>PushStarsHaptics.mm</c> plugin. Android
    /// drives the system <c>Vibrator</c> over JNI — predefined <c>VibrationEffect</c> primitives on
    /// API 29+, <c>createOneShot</c> on API 26–28, and a plain <c>Handheld.Vibrate()</c> on the few
    /// API 24–25 devices left (that call is also what makes Unity add the VIBRATE permission the JNI
    /// paths rely on). Everywhere else, the Editor included, every method is a silent no-op.</para>
    /// </summary>
    public static class Haptics
    {
        /// <summary>Maps 1:1 to iOS <c>UIImpactFeedbackStyle</c>; the int crosses the plugin boundary as-is.</summary>
        public enum ImpactStyle { Light = 0, Medium = 1, Heavy = 2 }

        // The Settings screen news up its own PlayerPrefsSettingsStore; matching that keeps one
        // source of truth for the flag without threading a service through here.
        private static readonly ISettingsStore Settings = new PlayerPrefsSettingsStore();

        // Tripped if a platform call ever throws — after that we stop trying rather than log per tap.
        private static bool _disabled;

        /// <summary>Whether a call would actually buzz right now: preference on, platform still healthy.</summary>
        public static bool Enabled => !_disabled && Settings.VibrationEnabled;

        /// <summary>The lightest feedback — a selection moving. What the menu tabs fire on tap.</summary>
        public static void Selection()
        {
            if (_disabled || !Settings.VibrationEnabled) return;
            try { PlaySelection(); }
            catch (Exception e) { Disable(e); }
        }

        /// <summary>A discrete knock, heavier than <see cref="Selection"/> — for confirms and events.</summary>
        public static void Impact(ImpactStyle style)
        {
            if (_disabled || !Settings.VibrationEnabled) return;
            try { PlayImpact(style); }
            catch (Exception e) { Disable(e); }
        }

        public static void Light()  => Impact(ImpactStyle.Light);
        public static void Medium() => Impact(ImpactStyle.Medium);
        public static void Heavy()  => Impact(ImpactStyle.Heavy);

        private static void Disable(Exception e)
        {
            _disabled = true;
            Debug.LogWarning($"[Haptics] turned off after a platform error: {e}");
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void _pushStarsHapticSelection();
        [DllImport("__Internal")] private static extern void _pushStarsHapticImpact(int style);

        private static void PlaySelection()             => _pushStarsHapticSelection();
        private static void PlayImpact(ImpactStyle s)   => _pushStarsHapticImpact((int)s);

#elif UNITY_ANDROID && !UNITY_EDITOR
        // VibrationEffect.EFFECT_* — frozen public constants, used by createPredefined on API 29+.
        private const int EffectClick      = 0;
        private const int EffectTick       = 2;
        private const int EffectHeavyClick = 5;

        private static bool _probed;
        private static bool _hasVibrator;
        private static int _api;
        private static AndroidJavaObject _vibrator;
        private static AndroidJavaClass _effect;

        private static void PlaySelection() => Vibrate(10, 55, EffectTick);

        private static void PlayImpact(ImpactStyle style)
        {
            switch (style)
            {
                case ImpactStyle.Light: Vibrate(12, 90,  EffectClick);      break;
                case ImpactStyle.Heavy: Vibrate(26, 220, EffectHeavyClick); break;
                default:                Vibrate(18, 150, EffectClick);      break;
            }
        }

        // ms / amplitude → createOneShot (API 26–28); effect → createPredefined (API 29+).
        private static void Vibrate(long ms, int amplitude, int effect)
        {
            if (!Probe()) return;

            if (_api >= 29)
            {
                using (var e = _effect.CallStatic<AndroidJavaObject>("createPredefined", effect))
                    _vibrator.Call("vibrate", e);
            }
            else if (_api >= 26)
            {
                using (var e = _effect.CallStatic<AndroidJavaObject>("createOneShot", ms, amplitude))
                    _vibrator.Call("vibrate", e);
            }
            else
            {
                // API 24–25 has no VibrationEffect. Handheld.Vibrate is a coarse ~500 ms buzz, but it
                // is all these devices reliably expose — and referencing it here is what makes Unity
                // add <uses-permission android:name="android.permission.VIBRATE"> to the manifest,
                // which every branch above needs too.
                Handheld.Vibrate();
            }
        }

        private static bool Probe()
        {
            if (_probed) return _hasVibrator;
            _probed = true;

            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                _api = version.GetStatic<int>("SDK_INT");

            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");

            _hasVibrator = _vibrator != null && _vibrator.Call<bool>("hasVibrator");
            if (_hasVibrator && _api >= 26)
                _effect = new AndroidJavaClass("android.os.VibrationEffect");
            return _hasVibrator;
        }

#else
        private static void PlaySelection() { }
        private static void PlayImpact(ImpactStyle style) { }
#endif
    }
}
