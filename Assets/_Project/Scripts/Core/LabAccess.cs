using UnityEngine;

namespace PushStars.Core
{
    /// <summary>
    /// Who sees the private authoring and diagnostic tools: BOT LAB and EMOTE LAB on the home
    /// screen, and the fight screen's hidden diagnostics corner (camera skeleton, five-tap reset).
    ///
    /// <para>Off for players. They show in Development builds, and in a release build only for an
    /// account on the server allowlist <c>botRecorderAccess/{uid}</c> (signed in, so the check has
    /// a uid to ask about). Set <see cref="OpenToEveryone"/> only for a private test build.</para>
    /// </summary>
    public static class LabAccess
    {
        public const bool OpenToEveryone = false;

        /// <summary>The server allowlist answer for the signed-in account, kept for screens that do
        /// not ask themselves (the fight screen). Set by the home-screen labs once they hear back.</summary>
        public static bool Allowlisted { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Allowlisted = false;

        /// <summary>Whether the lab entries show, given the server allowlist answer for this account.</summary>
        public static bool Visible(bool allowlisted) => OpenToEveryone || Debug.isDebugBuild || allowlisted;
    }
}
