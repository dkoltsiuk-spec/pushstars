using System;
using System.Collections.Generic;

namespace PushStars.Core
{
    /// <summary>
    /// Aura for the daily rhythm: the daily goal (once per local day) and the streak milestones
    /// (once ever). Paid by receipt, so whichever screen notices first — a fight result, or Home
    /// after a training set — collects it, and nothing is paid twice. No Aura for simply opening
    /// the app, and none for raw reps. See <c>docs/design/economy.md</c> §3–4.
    /// </summary>
    public static class DailyRewards
    {
        public static string StreakReceipt(int milestoneDay) => "streak:" + milestoneDay + ":aura:v1";

        /// <summary>Unpaid daily Aura as grants plus their captions, in display order.
        /// <paramref name="hasReceipt"/> defaults to the wallet's receipts.</summary>
        public static List<(AuraGrant Grant, AuraMoment Moment)> Pending(DailyGoalStatus goal, int streakDays,
            Func<string, bool> hasReceipt = null)
        {
            hasReceipt = hasReceipt ?? CaseRewards.HasAuraReceipt;
            var pending = new List<(AuraGrant, AuraMoment)>();
            if (goal.Done && !string.IsNullOrEmpty(goal.DayKey))
            {
                string receipt = DailyGoal.AuraReceipt(goal.DayKey);
                if (!hasReceipt(receipt))
                    pending.Add((new AuraGrant(receipt, EconomyConfig.AuraDailyGoal),
                                 new AuraMoment("DAILY GOAL", EconomyConfig.AuraDailyGoal)));
            }
            // "Reached", not "exactly on": a milestone missed by a day without a fight result
            // (training only) is still collected at the next settlement.
            foreach (int day in new[] { EconomyConfig.StreakMilestoneA, EconomyConfig.StreakMilestoneB })
            {
                long amount = AuraRewards.ForStreakDay(day);
                string receipt = StreakReceipt(day);
                if (streakDays < day || amount <= 0 || hasReceipt(receipt)) continue;
                pending.Add((new AuraGrant(receipt, amount), new AuraMoment(day + "-DAY STREAK", amount)));
            }
            return pending;
        }

        /// <summary>
        /// Pays whatever is pending outside a fight (Home after training). Returns the applied
        /// change and captions; an empty result when there was nothing to pay.
        /// </summary>
        public static AuraSettlementResult CollectPending(DailyGoalStatus goal, int streakDays)
        {
            var result = new AuraSettlementResult();
            var pending = Pending(goal, streakDays);
            if (pending.Count == 0) return result;
            var grants = new List<AuraGrant>();
            foreach (var (grant, moment) in pending) { grants.Add(grant); result.Moments.Add(moment); }
            var applied = CaseRewards.ApplyAura(grants);
            result.Change = applied.Change;
            result.Unlocked.AddRange(applied.Unlocked);
            return result;
        }
    }
}
