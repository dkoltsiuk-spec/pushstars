using System;
using System.Collections.Generic;

namespace PushStars.Core
{
    /// <summary>One captioned line of an Aura payout ("VICTORY +1000"). Amount 0 is an
    /// information line such as "GAVE IT ALL" (a waived minus) or "ROBOT UNLOCKED".</summary>
    public readonly struct AuraMoment
    {
        public readonly string Caption;
        public readonly long Amount;
        public AuraMoment(string caption, long amount) { Caption = caption; Amount = amount; }
        public override string ToString() => Amount == 0 ? Caption : Caption + " " + AuraFormat.Delta(Amount);
    }

    public enum AuraFightKind { Duel, Boss }

    /// <summary>Everything a finished fight needs to be priced in Aura. Values are measured
    /// before the fight is recorded (personal best, fights played, league) unless named "After".</summary>
    public struct AuraFightInput
    {
        public AuraFightKind Kind;
        public bool Won, Draw;
        /// <summary>Left a live fight before the end.</summary>
        public bool Quit;
        public int MyReps, OpponentReps;
        public float AverageForm;
        public int ClapReps;
        /// <summary>Best reps ever before this fight (0 = none yet).</summary>
        public int PersonalBestBefore;
        public int WinStreakAfter;
        public int RatedFightsBefore;
        /// <summary>Largest opponent lead in reps from <see cref="EconomyConfig.AuraComebackAfterSec"/> on.</summary>
        public int LateDeficit;
        public string LeagueId;
        /// <summary>Boss only: the boss had never been beaten before this win.</summary>
        public bool FirstBossWin;
        /// <summary>Boss only: the island's final boss.</summary>
        public bool IslandKing;
    }

    /// <summary>
    /// Prices a fight in Aura moments. Pure and deterministic; crediting (with the zero floor and
    /// the peak) is the ledger's job. See docs/design/economy.md §3.
    /// </summary>
    public static class AuraCalculator
    {
        public static float Stakes(string leagueId)
        {
            switch (leagueId)
            {
                case "silver":  return EconomyConfig.AuraStakesSilver;
                case "gold":    return EconomyConfig.AuraStakesGold;
                case "diamond": return EconomyConfig.AuraStakesDiamond;
                default:        return EconomyConfig.AuraStakesBronze;
            }
        }

        public static List<AuraMoment> ForFight(AuraFightInput f)
        {
            var moments = new List<AuraMoment>();
            if (f.Kind == AuraFightKind.Boss)
            {
                // PvE never costs Aura, quit or loss.
                if (f.Quit) return moments;
                if (f.Won)
                    moments.Add(f.FirstBossWin && f.IslandKing ? new AuraMoment("ISLAND KING DOWN", EconomyConfig.AuraIslandKingFirstWin)
                        : f.FirstBossWin ? new AuraMoment("BOSS DOWN", EconomyConfig.AuraBossFirstWin)
                        : new AuraMoment("BOSS DOWN AGAIN", EconomyConfig.AuraBossRepeatWin));
                AddPerformance(moments, f, 1f);
                return moments;
            }

            float stakes = Stakes(f.LeagueId);
            bool rookie = f.RatedFightsBefore < EconomyConfig.AuraRookieFights;
            if (f.Quit)
            {
                moments.Add(rookie ? new AuraMoment("ROOKIE SHIELD", 0) : Staked("RAGE QUIT", -EconomyConfig.AuraQuit, stakes));
                return moments;
            }

            int margin = f.MyReps - f.OpponentReps;
            if (f.Won)
            {
                bool crush = f.MyReps >= f.OpponentReps * EconomyConfig.AuraCrushRatio && margin >= EconomyConfig.AuraCrushMargin;
                moments.Add(crush ? Staked("DOMINATION", EconomyConfig.AuraCrushWin, stakes) : Staked("VICTORY", EconomyConfig.AuraWin, stakes));
                if (f.LateDeficit >= EconomyConfig.AuraComebackDeficit) moments.Add(Staked("COMEBACK", EconomyConfig.AuraComeback, stakes));
                if (margin == 1) moments.Add(Staked("CLUTCH", EconomyConfig.AuraClutch, stakes));
            }
            else if (f.Draw)
                moments.Add(Staked("DRAW", EconomyConfig.AuraDraw, stakes));
            else
            {
                bool crushed = f.OpponentReps >= f.MyReps * EconomyConfig.AuraCrushRatio && -margin >= EconomyConfig.AuraCrushMargin;
                bool gaveItAll = f.PersonalBestBefore > 0 && f.MyReps >= Math.Ceiling(f.PersonalBestBefore * EconomyConfig.AuraEffortShare);
                if (rookie) moments.Add(new AuraMoment("ROOKIE SHIELD", 0));
                else if (gaveItAll) moments.Add(new AuraMoment("GAVE IT ALL · NO AURA LOST", 0));
                else moments.Add(crushed ? Staked("DESTROYED", -EconomyConfig.AuraCrushedLoss, stakes) : Staked("DEFEAT", -EconomyConfig.AuraLoss, stakes));
            }
            AddPerformance(moments, f, stakes);
            if (f.Won) AddStreak(moments, f.WinStreakAfter, stakes);
            return moments;
        }

        private static void AddPerformance(List<AuraMoment> moments, AuraFightInput f, float stakes)
        {
            if (f.AverageForm >= EconomyConfig.AuraPerfectFormMin && f.MyReps >= EconomyConfig.AuraPerfectFormMinReps)
                moments.Add(Staked("PERFECT FORM", EconomyConfig.AuraPerfectForm, stakes));
            int claps = Math.Min(Math.Max(0, f.ClapReps), EconomyConfig.AuraMaxClaps);
            if (claps > 0) moments.Add(Staked(claps == 1 ? "CLAP PUSH-UP" : "CLAP PUSH-UPS x" + claps, (long)claps * EconomyConfig.AuraPerClap, stakes));
            if (f.PersonalBestBefore > 0 && f.MyReps > f.PersonalBestBefore)
                moments.Add(Staked("NEW RECORD", EconomyConfig.AuraNewRecord, stakes));
        }

        private static void AddStreak(List<AuraMoment> moments, int streak, float stakes)
        {
            long amount = streak == 3 ? EconomyConfig.AuraStreak3
                : streak == 5 ? EconomyConfig.AuraStreak5
                : streak >= 10 && streak % 10 == 0 ? EconomyConfig.AuraStreak10 : 0;
            if (amount > 0) moments.Add(Staked("WIN STREAK x" + streak, amount, stakes));
        }

        private static AuraMoment Staked(string caption, long amount, float stakes)
            => new AuraMoment(caption, (long)Math.Round(amount * (double)stakes, MidpointRounding.AwayFromZero));

        public static long Total(IEnumerable<AuraMoment> moments)
        {
            long total = 0;
            foreach (var moment in moments) total = checked(total + moment.Amount);
            return total;
        }
    }
}
