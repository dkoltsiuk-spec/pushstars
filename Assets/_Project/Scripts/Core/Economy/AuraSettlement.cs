using System.Collections.Generic;

namespace PushStars.Core
{
    public sealed class AuraSettlementResult
    {
        /// <summary>Captioned lines for the Aura screen, fight first, then progression and unlocks.</summary>
        public readonly List<AuraMoment> Moments = new List<AuraMoment>();
        /// <summary>Actual balance change (after the zero floor). The stamp shows this.</summary>
        public long Change;
        public readonly List<string> Unlocked = new List<string>();
    }

    /// <summary>
    /// Settles a finished fight's Aura in one save: the fight's moments under its own receipt, then
    /// any progression Aura not yet paid (every level reached, every league entered), then the Aura
    /// heroes whose goal that reached. Progression is paid by receipt whenever a result is settled,
    /// so levels earned in training are collected at the next fight rather than lost.
    /// </summary>
    public static class AuraSettlement
    {
        public static string FightReceipt(string fightId) => "fight:" + fightId + ":aura:v1";

        public static AuraSettlementResult Settle(string fightId, IReadOnlyList<AuraMoment> fightMoments, long xp, long trophies)
        {
            var result = new AuraSettlementResult();
            var grants = new List<AuraGrant>();
            if (fightMoments != null)
            {
                result.Moments.AddRange(fightMoments);
                long total = AuraCalculator.Total(fightMoments);
                if (total != 0 && !string.IsNullOrWhiteSpace(fightId)) grants.Add(new AuraGrant(FightReceipt(fightId), total));
            }

            // Levels: one line for all unpaid levels, so a veteran's first settlement stays readable.
            int level = LevelCalculator.LevelFromXp(xp), paidLevels = 0, lastLevel = 0;
            long levelAura = 0;
            for (int l = 2; l <= level; l++)
            {
                string receipt = AuraRewards.LevelReceipt(l);
                if (CaseRewards.HasAuraReceipt(receipt)) continue;
                long amount = AuraRewards.ForLevel(l);
                grants.Add(new AuraGrant(receipt, amount));
                levelAura += amount; paidLevels++; lastLevel = l;
            }
            if (paidLevels > 0) result.Moments.Add(new AuraMoment(paidLevels == 1 ? "LEVEL " + lastLevel : "LEVEL UP x" + paidLevels, levelAura));

            var league = Leagues.ForTrophies(trophies);
            foreach (var tier in Leagues.All)
            {
                if (tier.MinTrophies > league.MinTrophies) break;
                long amount = AuraRewards.ForPromotion(tier.Id);
                string receipt = AuraRewards.LeagueReceipt(tier.Id);
                if (amount <= 0 || CaseRewards.HasAuraReceipt(receipt)) continue;
                grants.Add(new AuraGrant(receipt, amount));
                result.Moments.Add(new AuraMoment(tier.DisplayName.ToUpperInvariant() + " LEAGUE", amount));
            }

            var applied = CaseRewards.ApplyAura(grants);
            result.Change = applied.Change;
            foreach (string id in applied.Unlocked)
            {
                result.Unlocked.Add(id);
                result.Moments.Add(new AuraMoment((AvatarCatalog.Find(id)?.Name ?? id.ToUpperInvariant()) + " UNLOCKED!", 0));
            }
            return result;
        }
    }
}
