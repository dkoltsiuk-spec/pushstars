using System;
using System.Threading.Tasks;

namespace PushStars.Core
{
    /// <summary>
    /// The OS notification permission as the rest of the app sees it. The Reminders assembly
    /// (Mobile Notifications) fills these in when it boots; until then — and in the Editor — the
    /// permission counts as granted, so the screens that ask behave as on a phone that said yes.
    /// <see cref="Granted"/> fires whenever a request comes back yes, so the push token can follow.
    /// </summary>
    public static class NotificationAccess
    {
        public enum State { Unknown, Granted, Denied }

        /// <summary>Current permission without asking.</summary>
        public static Func<State> Current = () => State.Granted;

        /// <summary>Asks the OS (once it has said no, only Settings can change it).</summary>
        public static Func<Task<bool>> Request = () => Task.FromResult(true);

        /// <summary>Opens this app's notification settings.</summary>
        public static Action OpenSettings = () => { };

        public static event Action Granted;

        public static async Task<bool> Ask()
        {
            bool granted;
            try { granted = await Request(); }
            catch (Exception) { granted = false; }
            if (granted) Granted?.Invoke();
            return granted;
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntime() => Granted = null;
    }
}
