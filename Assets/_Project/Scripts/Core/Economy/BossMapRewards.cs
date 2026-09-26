namespace PushStars.Core
{
    public enum BossMapRewardState { Locked, Ready, Collected }

    /// <summary>
    /// The boss map's collectibles. Each island has a gem islet (opens after its third boss) and a
    /// case chest (opens after its main boss). Both are paid once per player through wallet
    /// receipts, so a repeated tap, a replayed boss or a restart can never pay twice. Bosses
    /// themselves grant no trophies; the per-rep XP and <see cref="FightConfig.BossWinXpBonus"/>
    /// still come from the duel.
    /// </summary>
    public static class BossMapRewards
    {
        public static BossMapRewardState Gems(BossChapter chapter)
        {
            if (chapter == null) return BossMapRewardState.Locked;
            if (CaseRewards.HasGemReceipt(chapter.GemReceipt)) return BossMapRewardState.Collected;
            return BossCatalog.IsGemIsletReached(chapter) ? BossMapRewardState.Ready : BossMapRewardState.Locked;
        }

        public static BossMapRewardState Chest(BossChapter chapter)
        {
            if (chapter == null) return BossMapRewardState.Locked;
            if (CaseRewards.HasCaseReceipt(chapter.ChestReceipt)) return BossMapRewardState.Collected;
            return BossCatalog.IsChapterCleared(chapter) ? BossMapRewardState.Ready : BossMapRewardState.Locked;
        }

        public static bool TryCollectGems(BossChapter chapter)
            => Gems(chapter) == BossMapRewardState.Ready && CaseRewards.TryCreditGems(chapter.GemReceipt, chapter.GemReward);

        /// <summary>Grants an ordinary case (upgradable by taps like any other) whose ID is the
        /// chest receipt; an unopened one waits in the CASES inventory.</summary>
        public static bool TryCollectChest(BossChapter chapter, out string caseId)
        {
            caseId = null;
            if (Chest(chapter) != BossMapRewardState.Ready || !CaseRewards.TryGrantCase(chapter.ChestReceipt)) return false;
            caseId = chapter.ChestReceipt;
            return true;
        }
    }
}
