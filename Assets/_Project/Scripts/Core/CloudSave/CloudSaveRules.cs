namespace PushStars.Core
{
    public enum CloudSaveAction
    {
        /// <summary>Nothing to do (or nothing can be decided yet: the cloud did not answer).</summary>
        None,
        /// <summary>The device save becomes the account's: claim it, and upload it unless a guest.</summary>
        AdoptLocal,
        /// <summary>Send the device save to the cloud over the copy it holds.</summary>
        Upload,
        /// <summary>Replace the device save with the account's cloud copy.</summary>
        ApplyCloud,
        /// <summary>Start this phone from a fresh save: the progress here belongs to another account.</summary>
        ResetFresh,
    }

    /// <summary>What this phone knows about its own save.</summary>
    public struct LocalSaveState
    {
        /// <summary>UID whose progress the device save holds. Empty: not claimed yet (a save from
        /// before cloud saves, a fresh install, or a phone that was just signed out).</summary>
        public string OwnerUid;
        public bool OwnerAnonymous;
        /// <summary>Cloud revision the device save was last uploaded as or restored from.</summary>
        public long Rev;
        /// <summary>The save changed since that revision.</summary>
        public bool Dirty;
        public ProgressRank Rank;
    }

    /// <summary>The account's cloud copy, as far as the last read found out.</summary>
    public struct CloudSaveState
    {
        public bool Known, Exists;
        public long Rev;
        public ProgressRank Rank;
        public static CloudSaveState Unknown => default;
        public static CloudSaveState Missing => new CloudSaveState { Known = true };
        public static CloudSaveState At(long rev, ProgressRank rank) => new CloudSaveState { Known = true, Exists = true, Rev = rev, Rank = rank };
    }

    /// <summary>
    /// Who owns the progress on this phone, decided whenever the signed-in account might have
    /// changed. Pure, so every row of the table is an EditMode test.
    ///
    /// <list type="bullet">
    /// <item>A guest keeps a device-only save; guests never upload (their UID dies with the app).</item>
    /// <item>Linking a guest keeps its UID, so the guest's progress simply becomes the account's.</item>
    /// <item>Switching to an account that already has a cloud copy takes that copy. With no cloud
    /// copy yet, a guest's progress moves in; another account's progress never does, or one
    /// phone could copy a wallet into a second account.</item>
    /// <item>The same account changed on two phones: the more progressed copy wins
    /// (<see cref="ProgressRank"/>); nothing is merged field by field, so nothing is paid twice.</item>
    /// </list>
    /// </summary>
    public static class CloudSaveRules
    {
        /// <summary>A guest is decided without the cloud; a signed-in account needs
        /// <paramref name="cloud"/> known, else nothing happens until a later read succeeds.
        /// <see cref="CloudSaveAction.AdoptLocal"/> for a signed-in account means: claim, then upload
        /// over whatever revision the cloud holds.</summary>
        public static CloudSaveAction Decide(LocalSaveState local, string uid, bool anonymous, CloudSaveState cloud)
        {
            if (string.IsNullOrEmpty(uid)) return CloudSaveAction.None;
            bool unclaimed = string.IsNullOrEmpty(local.OwnerUid);
            bool same = local.OwnerUid == uid;

            if (anonymous)
            {
                // A guest never has a cloud copy. A linked account's progress stays with it: the
                // phone starts over rather than handing it to a stranger's guest session.
                if (same) return CloudSaveAction.None;
                if (unclaimed || local.OwnerAnonymous) return CloudSaveAction.AdoptLocal;
                return CloudSaveAction.ResetFresh;
            }

            if (!cloud.Known) return CloudSaveAction.None;

            if (same)
            {
                if (!cloud.Exists) return local.Rank.IsEmpty && !local.Dirty ? CloudSaveAction.None : CloudSaveAction.Upload;
                if (cloud.Rev == local.Rev) return local.Dirty ? CloudSaveAction.Upload : CloudSaveAction.None;
                // Another phone moved the account on.
                if (!local.Dirty) return CloudSaveAction.ApplyCloud;
                return local.Rank.CompareTo(cloud.Rank) >= 0 ? CloudSaveAction.Upload : CloudSaveAction.ApplyCloud;
            }

            if (unclaimed)
            {
                // A save from before cloud saves, or a reinstall whose sign-in survived (iOS
                // keeps it in the keychain). Nothing to lose when the cloud has nothing.
                if (!cloud.Exists) return CloudSaveAction.AdoptLocal;
                return local.Rank.CompareTo(cloud.Rank) > 0 ? CloudSaveAction.AdoptLocal : CloudSaveAction.ApplyCloud;
            }

            // Switched from another profile.
            if (cloud.Exists) return CloudSaveAction.ApplyCloud;
            return local.OwnerAnonymous ? CloudSaveAction.AdoptLocal : CloudSaveAction.ResetFresh;
        }
    }
}
