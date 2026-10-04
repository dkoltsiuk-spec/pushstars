using UnityEngine;

namespace PushStars.Core
{
    /// <summary>
    /// Who sees the private authoring tools on the home screen (BOT LAB, EMOTE LAB).
    ///
    /// <para><b>Turn <see cref="OpenToEveryone"/> off before the app goes to a store.</b> While the
    /// owner is the only tester the labs show in every build, so an ordinary (fast) release build
    /// can record. With it off they show in Development builds, and in a release build only for an
    /// account on the server allowlist <c>botRecorderAccess/{uid}</c>.</para>
    /// </summary>
    public static class LabAccess
    {
        public const bool OpenToEveryone = true;

        /// <summary>Whether the lab entries show, given the server allowlist answer for this account.</summary>
        public static bool Visible(bool allowlisted) => OpenToEveryone || Debug.isDebugBuild || allowlisted;
    }
}
