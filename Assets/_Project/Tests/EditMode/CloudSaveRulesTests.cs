using NUnit.Framework;
using PushStars.Core;

namespace PushStars.Tests
{
    /// <summary>Who owns the phone's save after every way the account can change.</summary>
    public sealed class CloudSaveRulesTests
    {
        private const string Guest = "guest-uid", Account = "account-uid", Other = "other-uid";
        private static readonly ProgressRank Small = new ProgressRank(10000, 300, 2);
        private static readonly ProgressRank Big = new ProgressRank(250000, 9000, 40);

        private static LocalSaveState Local(string owner, bool ownerAnonymous, long rev = 0, bool dirty = false, ProgressRank rank = default)
            => new LocalSaveState { OwnerUid = owner, OwnerAnonymous = ownerAnonymous, Rev = rev, Dirty = dirty, Rank = rank };

        private static CloudSaveAction Decide(LocalSaveState local, string uid, bool anonymous, CloudSaveState cloud)
            => CloudSaveRules.Decide(local, uid, anonymous, cloud);

        [Test]
        public void NoSession_DoesNothing()
            => Assert.AreEqual(CloudSaveAction.None, Decide(Local(Guest, true, rank: Small), null, true, CloudSaveState.Unknown));

        [Test]
        public void FirstGuestSession_ClaimsTheSaveWithoutTheCloud()
            => Assert.AreEqual(CloudSaveAction.AdoptLocal, Decide(Local("", true, rank: Small), Guest, true, CloudSaveState.Unknown));

        [Test]
        public void SameGuest_DoesNothing()
            => Assert.AreEqual(CloudSaveAction.None, Decide(Local(Guest, true, dirty: true, rank: Small), Guest, true, CloudSaveState.Unknown));

        [Test]
        public void LostGuestSession_NewGuestKeepsTheProgress()
            => Assert.AreEqual(CloudSaveAction.AdoptLocal, Decide(Local(Guest, true, rank: Small), Other, true, CloudSaveState.Unknown));

        [Test]
        public void LinkedAccountReplacedByGuest_PhoneStartsOver()
            => Assert.AreEqual(CloudSaveAction.ResetFresh, Decide(Local(Account, false, rev: 3, rank: Big), Guest, true, CloudSaveState.Unknown));

        [Test]
        public void GuestLinked_SameUid_FirstUpload()
            => Assert.AreEqual(CloudSaveAction.Upload, Decide(Local(Guest, true, dirty: true, rank: Small), Guest, false, CloudSaveState.Missing));

        [Test]
        public void GuestLinked_NothingEarnedYet_NothingToUpload()
            => Assert.AreEqual(CloudSaveAction.None, Decide(Local(Guest, true), Guest, false, CloudSaveState.Missing));

        [Test]
        public void SignedIn_CloudUnreachable_WaitsForALaterRead()
        {
            Assert.AreEqual(CloudSaveAction.None, Decide(Local(Account, false, 2, true, Small), Account, false, CloudSaveState.Unknown));
            Assert.AreEqual(CloudSaveAction.None, Decide(Local(Guest, true, rank: Small), Account, false, CloudSaveState.Unknown));
        }

        [Test]
        public void SameAccount_InStep_UploadsOnlyChanges()
        {
            Assert.AreEqual(CloudSaveAction.None, Decide(Local(Account, false, 4, false, Big), Account, false, CloudSaveState.At(4, Big)));
            Assert.AreEqual(CloudSaveAction.Upload, Decide(Local(Account, false, 4, true, Big), Account, false, CloudSaveState.At(4, Big)));
        }

        [Test]
        public void SameAccount_OtherPhoneMovedOn_UnchangedPhoneTakesTheCloud()
            => Assert.AreEqual(CloudSaveAction.ApplyCloud, Decide(Local(Account, false, 4, false, Small), Account, false, CloudSaveState.At(7, Big)));

        [Test]
        public void SameAccount_BothPhonesChanged_MoreProgressedCopyWins()
        {
            Assert.AreEqual(CloudSaveAction.ApplyCloud, Decide(Local(Account, false, 4, true, Small), Account, false, CloudSaveState.At(7, Big)));
            Assert.AreEqual(CloudSaveAction.Upload, Decide(Local(Account, false, 4, true, Big), Account, false, CloudSaveState.At(7, Small)));
        }

        [Test]
        public void SameAccount_CloudCopyDeleted_UploadsAgain()
            => Assert.AreEqual(CloudSaveAction.Upload, Decide(Local(Account, false, 4, false, Big), Account, false, CloudSaveState.Missing));

        [Test]
        public void SwitchFromGuest_ToAccountWithProgress_TakesTheCloud()
            => Assert.AreEqual(CloudSaveAction.ApplyCloud, Decide(Local(Guest, true, rank: Big), Account, false, CloudSaveState.At(5, Small)));

        [Test]
        public void SwitchFromGuest_ToAccountWithoutProgress_GuestProgressMovesIn()
            => Assert.AreEqual(CloudSaveAction.AdoptLocal, Decide(Local(Guest, true, rank: Small), Account, false, CloudSaveState.Missing));

        [Test]
        public void SwitchFromAccount_ToAnotherAccount_TakesItsCloud()
            => Assert.AreEqual(CloudSaveAction.ApplyCloud, Decide(Local(Account, false, 9, rank: Big), Other, false, CloudSaveState.At(1, Small)));

        [Test]
        public void SwitchFromAccount_ToAccountWithoutProgress_NeverCopiesTheWallet()
            => Assert.AreEqual(CloudSaveAction.ResetFresh, Decide(Local(Account, false, 9, rank: Big), Other, false, CloudSaveState.Missing));

        [Test]
        public void Reinstall_SessionRestored_FreshSaveTakesTheCloud()
            => Assert.AreEqual(CloudSaveAction.ApplyCloud, Decide(Local("", true, rank: Small), Account, false, CloudSaveState.At(12, Big)));

        [Test]
        public void SaveFromBeforeCloudSaves_AlreadySignedIn_IsUploaded()
        {
            Assert.AreEqual(CloudSaveAction.AdoptLocal, Decide(Local("", true, rank: Big), Account, false, CloudSaveState.Missing));
            Assert.AreEqual(CloudSaveAction.AdoptLocal, Decide(Local("", true, rank: Big), Account, false, CloudSaveState.At(2, Small)));
        }

        [Test]
        public void Rank_OrdersByPeakThenXpThenWorkouts()
        {
            Assert.Greater(new ProgressRank(2, 0, 0).CompareTo(new ProgressRank(1, 999, 999)), 0);
            Assert.Greater(new ProgressRank(1, 2, 0).CompareTo(new ProgressRank(1, 1, 999)), 0);
            Assert.Greater(new ProgressRank(1, 1, 2).CompareTo(new ProgressRank(1, 1, 1)), 0);
            Assert.AreEqual(0, new ProgressRank(1, 1, 1).CompareTo(new ProgressRank(1, 1, 1)));
            Assert.IsTrue(default(ProgressRank).IsEmpty);
        }
    }
}
