using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PushStars.Core
{
    // Values match the chest art: black, blue, gold, purple.
    public enum CaseRarity { Common, Rare, Epic, Legendary }

    /// <summary>A detached, read-only view of an unclaimed case.</summary>
    public sealed class PendingCase
    {
        public string Id { get; }
        public CaseRarity Rarity { get; }
        public int UpgradeTapsUsed { get; }
        public int RemainingTaps => Math.Max(0, CaseRewards.UpgradeTapCount - UpgradeTapsUsed);
        public bool Opened { get; }
        public int Gems { get; }
        public string AvatarId { get; }
        public int AvatarCards { get; }
        public bool CanOpen => !Opened && RemainingTaps == 0;

        internal PendingCase(string id, CaseRarity rarity, int taps, bool opened, int gems, string avatarId = null, int avatarCards = 0)
        {
            Id = id;
            Rarity = rarity;
            UpgradeTapsUsed = taps;
            Opened = opened;
            Gems = gems;
            AvatarId = avatarId;
            AvatarCards = avatarCards;
        }
    }

    /// <summary>
    /// Offline-only case inventory, avatar collection and currency wallet. This makes
    /// no server or purchase requests. Every accepted tap, opened prize and claim is saved before
    /// returning to the UI. Balance and case removal share a single save, so a repeated claim
    /// cannot pay twice, including after a domain reload or an app restart.
    /// </summary>
    public static class CaseRewards
    {
        public const int UpgradeTapCount = 3;
        public const int AssessmentAura = EconomyConfig.AuraAssessment;
        /// <summary>What the assessment paid before Aura moved to meme scale (saves v3–v5).</summary>
        public const int LegacyAssessmentAura = 200;
        public const string AssessmentReceipt = "assessment:welcome:v1";
        /// <summary>Wallet receipt of the assessment's Aura, credited with the award itself.</summary>
        public const string AssessmentAuraReceipt = "assessment:welcome:aura:v1";
        /// <summary>Tops a legacy 200 welcome up to <see cref="AssessmentAura"/> once (save v6).</summary>
        public const string AssessmentAuraTopUpReceipt = "assessment:welcome:aura:topup:v2";
        // Keep the original key so existing inventories are migrated in place, never abandoned.
        private const string SaveKey = "rewards.case_ledger.v1";
        private static readonly System.Random Random = new System.Random();
        private static CaseRewardLedger _ledger;
        private static CaseRewardPolicy _policy;
        private static bool _policyLoaded;

        private static CaseRewardLedger Ledger => _ledger ?? (_ledger = new CaseRewardLedger(
            PlayerPrefs.GetString(SaveKey, string.Empty), () => Random.NextDouble(), json =>
            {
                PlayerPrefs.SetString(SaveKey, json);
                PlayerPrefs.Save();
            }));

        public static PendingCase Pending => Ledger.Pending;
        public static int PendingCount => Ledger.PendingCount;
        public static bool HasPendingCase => PendingCount > 0;
        public static long GemsBalance => Ledger.GemsBalance;
        public static long AuraBalance => Ledger.AuraBalance;
        /// <summary>Highest Aura balance ever reached. Never drops; fills the Aura heroes' unlock bars.</summary>
        public static long AuraPeak => Ledger.AuraPeak;
        public static int CardsFor(string id) => Ledger.CardsFor(id);
        public static bool OwnsAvatar(string id) => Ledger.OwnsAvatar(id);
        /// <summary>Gems price, or for an Aura hero the Aura still missing from its goal.</summary>
        public static long AvatarPrice(string id) => Ledger.AvatarPrice(id);
        public static long AvatarUnlockProgress(string id) => Ledger.AvatarUnlockProgress(id);
        public static bool TryBuyAvatar(string id) => Ledger.TryBuyAvatar(id);
        public static bool TryCreditAura(string receipt, int amount) => Ledger.TryCreditAura(receipt, amount);
        /// <summary>Applies several receipted Aura changes (plus or minus) in one save.
        /// See <see cref="CaseRewardLedger.ApplyAura"/>.</summary>
        public static AuraApplyResult ApplyAura(IReadOnlyList<AuraGrant> grants) => Ledger.ApplyAura(grants);
        public static bool HasAuraReceipt(string receipt) => Ledger.HasAuraReceipt(receipt);
        /// <summary>One-shot gem payout keyed by a stable receipt (e.g. a boss-map gem islet).</summary>
        public static bool TryCreditGems(string receipt, int amount) => Ledger.TryCreditGems(receipt, amount);
        public static bool HasGemReceipt(string receipt) => Ledger.HasGemReceipt(receipt);
        /// <summary>A case with this grant or workout ID was ever awarded (claimed or not).</summary>
        public static bool HasCaseReceipt(string grantId) => Ledger.HasCaseReceipt(grantId);
        public static PendingCase Find(string caseId) => Ledger.Find(caseId);
        public static int DailyWorkoutCaseLimit => Policy != null ? Policy.DailyWorkoutCaseLimit : CaseRewardPolicy.DefaultDailyLimit;
        public static int MinimumWorkoutReps => Policy != null ? Policy.MinimumWorkoutReps : CaseRewardPolicy.DefaultMinimumReps;
        public static int DailyWorkoutCasesRemaining => Ledger.RemainingDailyWorkoutCases(DateTimeOffset.UtcNow, DailyWorkoutCaseLimit);
        public static bool CanAwardDailyWorkoutCase => DailyWorkoutCasesRemaining > 0;

        private static CaseRewardPolicy Policy
        {
            get
            {
                if (!_policyLoaded)
                {
                    _policy = Resources.Load<CaseRewardPolicy>(CaseRewardPolicy.ResourcePath);
                    _policyLoaded = true;
                }
                return _policy;
            }
        }

        /// <summary>
        /// Award within the configured quota for the device clock's UTC calendar day. Opening or
        /// claiming never resets eligibility. Rejected completed workouts are remembered too, so
        /// replaying yesterday's completion callback cannot claim today's reward.
        /// </summary>
        public static bool TryAwardDailyWorkoutCase(string workoutId, int reps) =>
            Ledger.TryAwardDailyWorkoutCase(workoutId, reps, DateTimeOffset.UtcNow, DailyWorkoutCaseLimit, MinimumWorkoutReps);

        public static bool TryAwardDailyWorkoutCase(string workoutId, int reps, out PendingCase awarded)
        {
            bool granted = TryAwardDailyWorkoutCase(workoutId, reps);
            awarded = granted ? Ledger.Find(workoutId) : null;
            return granted;
        }

        /// <summary>Compatibility entry point; workout awards now obey the daily policy.</summary>
        public static bool AwardForWorkout(string sessionId, int reps) => TryAwardDailyWorkoutCase(sessionId, reps);

        /// <summary>
        /// Explicit grant for a different reward source; bypasses workout eligibility. The caller
        /// must provide a stable, globally unique receipt ID such as "promotion:gold:player-id".
        /// </summary>
        public static bool TryGrantCase(string grantId) => Ledger.TryGrantCase(grantId);

        /// <summary>Once per player: an ordinary level-1 case (upgradable like any other) plus
        /// <see cref="AssessmentAura"/> credited straight to the wallet in the same save.</summary>
        public static bool TryAwardAssessmentCase(int reps, out PendingCase awarded)
        {
            bool granted = Ledger.TryAwardAssessmentCase(reps);
            awarded = reps > 0 ? Ledger.Find(AssessmentReceipt) : null;
            return granted;
        }

        /// <summary>expectedTap is the snapshot's UpgradeTapsUsed; stale or repeated callbacks are rejected.</summary>
        public static bool TryUpgrade(string caseId, int expectedTap, out PendingCase updated) =>
            Ledger.TryUpgrade(caseId, expectedTap, out updated);

        /// <summary>After three upgrade taps, persist the prize. Reopening replays the saved prize.</summary>
        public static bool TryOpen(string caseId, out PendingCase opened) => Ledger.TryOpen(caseId, out opened);

        public static bool TryClaim(string caseId, out int gems) => Ledger.TryClaim(caseId, out gems);

        public static double UpgradeChance(CaseRarity rarity)
        {
            switch (rarity)
            {
                case CaseRarity.Common: return 0.45;
                case CaseRarity.Rare: return 0.30;
                case CaseRarity.Epic: return 0.15;
                case CaseRarity.Legendary: return 0;
                default: throw new ArgumentOutOfRangeException(nameof(rarity));
            }
        }

        /// <summary>Pure, bounded rule: one tap can advance at most one rarity and never downgrade.</summary>
        public static CaseRarity UpgradeForRoll(CaseRarity rarity, double roll)
        {
            ValidateRoll(roll);
            return roll < UpgradeChance(rarity) ? (CaseRarity)((int)rarity + 1) : rarity;
        }

        public static int MinimumGems(CaseRarity rarity)
        {
            switch (rarity)
            {
                case CaseRarity.Common: return 10;
                case CaseRarity.Rare: return 30;
                case CaseRarity.Epic: return 80;
                case CaseRarity.Legendary: return 180;
                default: throw new ArgumentOutOfRangeException(nameof(rarity));
            }
        }

        public static int MaximumGems(CaseRarity rarity)
        {
            switch (rarity)
            {
                case CaseRarity.Common: return 25;
                case CaseRarity.Rare: return 60;
                case CaseRarity.Epic: return 140;
                case CaseRarity.Legendary: return 300;
                default: throw new ArgumentOutOfRangeException(nameof(rarity));
            }
        }

        /// <summary>Pure inclusive-range reward roll; random input must be in [0, 1).</summary>
        public static int GemsForRoll(CaseRarity rarity, double roll)
        {
            ValidateRoll(roll);
            int minimum = MinimumGems(rarity);
            return minimum + (int)Math.Floor(roll * (MaximumGems(rarity) - minimum + 1));
        }

        private static void ValidateRoll(double roll)
        {
            if (double.IsNaN(roll) || roll < 0 || roll >= 1)
                throw new ArgumentOutOfRangeException(nameof(roll), "Random roll must be in [0, 1).");
        }
    }

    /// <summary>One receipted Aura change. Negative deltas are losses; the balance floors at 0.</summary>
    public readonly struct AuraGrant
    {
        public readonly string Receipt;
        public readonly long Delta;
        public AuraGrant(string receipt, long delta) { Receipt = receipt; Delta = delta; }
    }

    public sealed class AuraApplyResult
    {
        /// <summary>Receipts that were new and are now committed.</summary>
        public readonly List<string> Applied = new List<string>();
        /// <summary>Actual balance change after the zero floor.</summary>
        public long Change;
        public long Balance, Peak;
        /// <summary>Aura heroes whose unlock goal this change reached.</summary>
        public readonly List<string> Unlocked = new List<string>();
    }

    /// <summary>
    /// The actual save-state machine, with injectable randomness and persistence for deterministic
    /// regression checks that never touch the player's PlayerPrefs. Runtime UI uses CaseRewards.
    /// </summary>
    public sealed class CaseRewardLedger
    {
        [Serializable]
        private sealed class Entry
        {
            public string id;
            public CaseRarity rarity;
            public int taps;
            public bool opened;
            public int gems;
            /// <summary>Legacy (save v4): Aura sealed inside the assessment case. Migrated away in v5.</summary>
            public int aura;
            public string avatarId;
            public int avatarCards;

            public Entry Copy() => (Entry)MemberwiseClone();
            public PendingCase Snapshot() => new PendingCase(id, rarity, taps, opened, gems, avatarId, avatarCards);
        }

        [Serializable]
        private sealed class AvatarProgress
        {
            public string id;
            public int cards;
            public bool owned;
            public AvatarProgress Copy() => (AvatarProgress)MemberwiseClone();
        }

        [Serializable]
        private sealed class SaveData
        {
            public int version = 6;
            public long gems;
            public long aura;
            /// <summary>Highest Aura ever reached (save v6). Never drops.</summary>
            public long peakAura;
            public List<string> auraReceipts = new List<string>();
            /// <summary>Added within v5; older saves load it as null and are given an empty list.</summary>
            public List<string> gemReceipts = new List<string>();
            public List<AvatarProgress> avatars = new List<AvatarProgress>();
            public List<string> awardedWorkouts = new List<string>();
            public List<Entry> cases = new List<Entry>();
            public List<string> processedWorkoutIds = new List<string>();
            public string dailyWorkoutUtcDay;
            public int dailyWorkoutCount;

            public SaveData Copy()
            {
                var copy = new SaveData
                {
                    version = version, gems = gems, aura = aura, peakAura = peakAura, auraReceipts = new List<string>(auraReceipts),
                    gemReceipts = new List<string>(gemReceipts), awardedWorkouts = new List<string>(awardedWorkouts),
                    processedWorkoutIds = new List<string>(processedWorkoutIds),
                    dailyWorkoutUtcDay = dailyWorkoutUtcDay, dailyWorkoutCount = dailyWorkoutCount
                };
                foreach (Entry entry in cases) copy.cases.Add(entry.Copy());
                foreach (AvatarProgress avatar in avatars) copy.avatars.Add(avatar.Copy());
                return copy;
            }
        }

        private SaveData _state;
        private readonly HashSet<string> _awardedWorkouts;
        private readonly HashSet<string> _processedWorkouts;
        private readonly Func<double> _random;
        private readonly Action<string> _persist;
        private readonly AvatarOffer[] _offers;

        public CaseRewardLedger(string savedJson, Func<double> random, Action<string> persist, AvatarOffer[] offers = null)
        {
            _offers = offers ?? AvatarCatalog.All;
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _persist = persist ?? throw new ArgumentNullException(nameof(persist));
            _state = string.IsNullOrEmpty(savedJson) ? new SaveData() : JsonUtility.FromJson<SaveData>(savedJson);
            ValidateSave(_state);
            if (_state.version == 1)
            {
                // Version 1 had no dates. Preserve every receipt/prize and start tracking daily
                // eligibility on the next new workout; historic rewards are never paid again.
                _state.version = 2;
                _state.processedWorkoutIds = new List<string>(_state.awardedWorkouts);
                _state.dailyWorkoutUtcDay = null;
                _state.dailyWorkoutCount = 0;
            }
            if (_state.version < 3)
            {
                _state.version = 3;
                _state.avatars = new List<AvatarProgress>();
                _state.auraReceipts = new List<string>();
                _state.aura = 0;
            }
            if (_state.version < 5) MigrateSealedAura(_state);
            if (_state.gemReceipts == null) _state.gemReceipts = new List<string>();
            if (_state.version < 6) MigrateMemeAura(_state);
            _awardedWorkouts = new HashSet<string>(_state.awardedWorkouts, StringComparer.Ordinal);
            _state.version = 6;
            _processedWorkouts = new HashSet<string>(_state.processedWorkoutIds, StringComparer.Ordinal);
        }

        /// <summary>v4 kept the assessment Aura inside a fixed legendary case. Aura is now credited
        /// on award and the case is an ordinary level-1 case, so pay out any sealed Aura once and
        /// turn the case into a fresh common one. Nothing is lost or paid twice.</summary>
        private static void MigrateSealedAura(SaveData state)
        {
            foreach (Entry entry in state.cases)
            {
                if (entry.aura <= 0) continue;
                if (!state.auraReceipts.Contains(CaseRewards.AssessmentAuraReceipt))
                {
                    state.aura = checked(state.aura + entry.aura);
                    state.auraReceipts.Add(CaseRewards.AssessmentAuraReceipt);
                }
                entry.aura = 0; entry.rarity = CaseRarity.Common; entry.taps = 0;
                entry.opened = false; entry.gems = 0;
            }
        }

        /// <summary>v6: Aura became the meme status score. The legacy 200 welcome is topped up to
        /// the new welcome, the peak starts at the balance, and heroes a v5 save already owns stay owned.</summary>
        private static void MigrateMemeAura(SaveData state)
        {
            if (state.auraReceipts.Contains(CaseRewards.AssessmentAuraReceipt) &&
                !state.auraReceipts.Contains(CaseRewards.AssessmentAuraTopUpReceipt))
            {
                state.aura = checked(state.aura + CaseRewards.AssessmentAura - CaseRewards.LegacyAssessmentAura);
                state.auraReceipts.Add(CaseRewards.AssessmentAuraTopUpReceipt);
            }
            state.peakAura = Math.Max(state.peakAura, state.aura);
        }

        public PendingCase Pending => PendingCount > 0 ? _state.cases[0].Snapshot() : null;
        public int PendingCount => _state.cases.Count;
        public long GemsBalance => _state.gems;
        public long AuraBalance => _state.aura;
        public long AuraPeak => _state.peakAura;
        private AvatarOffer Offer(string id) => Array.Find(_offers, offer => offer.Id == id);
        public int CardsFor(string id) => _state.avatars.Find(a => a.id == id)?.cards ?? 0;
        public bool OwnsAvatar(string id) => Owns(_state, Offer(id));
        private static bool Owns(SaveData state, AvatarOffer offer)
        {
            if (offer == null) return false;
            var progress = state.avatars.Find(a => a.id == offer.Id);
            return offer.Kind == AvatarPurchaseKind.Included || (progress != null && progress.owned) ||
                offer.Unlocked(state.peakAura, progress?.cards ?? 0);
        }

        /// <summary>Filled part of an Aura hero's bar (peak Aura + cards), 0..goal.</summary>
        public long AvatarUnlockProgress(string id)
        {
            var offer = Offer(id);
            if (offer == null || !offer.UnlocksByAura) return 0;
            return OwnsAvatar(id) ? offer.AuraGoal : offer.UnlockProgress(_state.peakAura, CardsFor(id));
        }

        /// <summary>Gems price, or the Aura an Aura hero still misses (0 once unlocked).</summary>
        public long AvatarPrice(string id)
        {
            var offer = Offer(id);
            if (offer == null) return 0;
            return offer.UnlocksByAura ? offer.AuraGoal - AvatarUnlockProgress(id) : offer.Price;
        }

        public bool HasAuraReceipt(string receipt)
            => !string.IsNullOrWhiteSpace(receipt) && _state.auraReceipts.Contains(receipt.Trim());

        public bool TryCreditAura(string receipt, int amount)
            => amount > 0 && ApplyAura(new[] { new AuraGrant(receipt, amount) }).Applied.Count > 0;

        /// <summary>
        /// Commits every grant whose receipt is new, in order, in one save. The balance floors at 0
        /// after each grant, the peak follows the highest balance, and any Aura hero whose goal is
        /// reached becomes owned in the same save. Replayed receipts change nothing.
        /// </summary>
        public AuraApplyResult ApplyAura(IReadOnlyList<AuraGrant> grants)
        {
            var result = new AuraApplyResult();
            var next = _state.Copy();
            var seen = new HashSet<string>(next.auraReceipts, StringComparer.Ordinal);
            foreach (var grant in grants ?? Array.Empty<AuraGrant>())
            {
                if (string.IsNullOrWhiteSpace(grant.Receipt) || grant.Delta == 0) continue;
                string receipt = grant.Receipt.Trim();
                if (!seen.Add(receipt)) continue;
                next.aura = Math.Max(0, checked(next.aura + grant.Delta));
                next.peakAura = Math.Max(next.peakAura, next.aura);
                next.auraReceipts.Add(receipt);
                result.Applied.Add(receipt);
            }
            result.Change = next.aura - _state.aura;
            result.Balance = next.aura; result.Peak = next.peakAura;
            if (result.Applied.Count == 0) return result;
            foreach (var offer in _offers)
                if (offer.UnlocksByAura && !Owns(_state, offer) && Owns(next, offer))
                {
                    Progress(next, offer.Id).owned = true;
                    result.Unlocked.Add(offer.Id);
                }
            Commit(next);
            return result;
        }

        public bool TryCreditGems(string receipt, int amount)
        {
            if (string.IsNullOrWhiteSpace(receipt) || amount <= 0 || HasGemReceipt(receipt)) return false;
            var next = _state.Copy();
            next.gems = checked(next.gems + amount);
            next.gemReceipts.Add(receipt.Trim());
            Commit(next);
            return true;
        }

        public bool HasGemReceipt(string receipt)
            => !string.IsNullOrWhiteSpace(receipt) && _state.gemReceipts.Contains(receipt.Trim());

        public bool HasCaseReceipt(string grantId)
            => !string.IsNullOrWhiteSpace(grantId) && _awardedWorkouts.Contains(grantId.Trim());

        public bool TryBuyAvatar(string id)
        {
            var offer = Offer(id);
            // Aura is a status score, never spent: Aura heroes unlock by reaching their goal.
            if (offer == null || OwnsAvatar(id) || offer.Kind != AvatarPurchaseKind.Gems) return false;
            int price = offer.Price;
            if (price < 0 || _state.gems < price) return false;
            var next = _state.Copy();
            next.gems -= price;
            Progress(next, id).owned = true;
            Commit(next);
            return true;
        }

        private static AvatarProgress Progress(SaveData state, string id)
        {
            var progress = state.avatars.Find(a => a.id == id);
            if (progress != null) return progress;
            progress = new AvatarProgress { id = id };
            state.avatars.Add(progress);
            return progress;
        }
        public PendingCase Find(string caseId)
        {
            int index = FindIndex(caseId);
            return index >= 0 ? _state.cases[index].Snapshot() : null;
        }

        /// <summary>Compatibility entry point using the default daily policy and UTC device clock.</summary>
        public bool AwardForWorkout(string sessionId, int reps) =>
            TryAwardDailyWorkoutCase(sessionId, reps, DateTimeOffset.UtcNow);

        /// <summary>
        /// UTC calendar-day quota, with an explicit instant for deterministic tests or a future
        /// trusted clock. A backward day never resets quota. This is offline eligibility, not a
        /// trusted server clock: setting the device ahead can still advance the local day.
        /// </summary>
        public int RemainingDailyWorkoutCases(DateTimeOffset now, int dailyLimit = CaseRewardPolicy.DefaultDailyLimit)
        {
            if (dailyLimit <= 0) return 0;
            string day = UtcDay(now);
            int comparison = string.IsNullOrEmpty(_state.dailyWorkoutUtcDay) ? 1 : string.CompareOrdinal(day, _state.dailyWorkoutUtcDay);
            if (comparison > 0) return dailyLimit;
            if (comparison < 0) return 0;
            return Math.Max(0, dailyLimit - _state.dailyWorkoutCount);
        }

        public bool CanAwardDailyWorkoutCase(DateTimeOffset now, int dailyLimit = CaseRewardPolicy.DefaultDailyLimit) =>
            RemainingDailyWorkoutCases(now, dailyLimit) > 0;

        public bool TryAwardDailyWorkoutCase(string workoutId, int reps, DateTimeOffset now,
            int dailyLimit = CaseRewardPolicy.DefaultDailyLimit, int minimumReps = CaseRewardPolicy.DefaultMinimumReps)
        {
            if (reps <= 0 || string.IsNullOrWhiteSpace(workoutId)) return false;
            workoutId = workoutId.Trim();
            if (_awardedWorkouts.Contains(workoutId) || _processedWorkouts.Contains(workoutId)) return false;
            bool eligible = reps >= Math.Max(1, minimumReps) && RemainingDailyWorkoutCases(now, dailyLimit) > 0;
            string day = UtcDay(now);
            var next = _state.Copy();
            next.processedWorkoutIds.Add(workoutId);
            if (string.IsNullOrEmpty(next.dailyWorkoutUtcDay) || string.CompareOrdinal(day, next.dailyWorkoutUtcDay) > 0)
            {
                next.dailyWorkoutUtcDay = day;
                next.dailyWorkoutCount = 0;
            }
            if (eligible)
            {
                next.dailyWorkoutCount++;
                next.awardedWorkouts.Add(workoutId);
                next.cases.Add(new Entry { id = workoutId, rarity = CaseRarity.Common });
            }
            Commit(next);
            _processedWorkouts.Add(workoutId);
            if (eligible) _awardedWorkouts.Add(workoutId);
            return eligible;
        }

        public bool TryGrantCase(string grantId)
        {
            if (string.IsNullOrWhiteSpace(grantId)) return false;
            grantId = grantId.Trim();
            if (_awardedWorkouts.Contains(grantId) || _processedWorkouts.Contains(grantId)) return false;
            var next = _state.Copy();
            next.awardedWorkouts.Add(grantId);
            next.cases.Add(new Entry { id = grantId, rarity = CaseRarity.Common });
            Commit(next);
            _awardedWorkouts.Add(grantId);
            return true;
        }

        /// <summary>One welcome reward, independent of daily workout quota: an ordinary common
        /// case and the assessment Aura, committed together in one save.</summary>
        public bool TryAwardAssessmentCase(int reps)
        {
            if (reps <= 0 || _awardedWorkouts.Contains(CaseRewards.AssessmentReceipt)) return false;
            var next = _state.Copy();
            next.awardedWorkouts.Add(CaseRewards.AssessmentReceipt);
            next.cases.Add(new Entry { id = CaseRewards.AssessmentReceipt, rarity = CaseRarity.Common });
            if (!next.auraReceipts.Contains(CaseRewards.AssessmentAuraReceipt))
            {
                next.aura = checked(next.aura + CaseRewards.AssessmentAura);
                next.peakAura = Math.Max(next.peakAura, next.aura);
                next.auraReceipts.Add(CaseRewards.AssessmentAuraReceipt);
                // The new welcome is already the full amount; never top it up again.
                if (!next.auraReceipts.Contains(CaseRewards.AssessmentAuraTopUpReceipt))
                    next.auraReceipts.Add(CaseRewards.AssessmentAuraTopUpReceipt);
            }
            Commit(next);
            _awardedWorkouts.Add(CaseRewards.AssessmentReceipt);
            return true;
        }

        private static string UtcDay(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public bool TryUpgrade(string caseId, int expectedTap, out PendingCase updated)
        {
            int index = FindIndex(caseId);
            updated = index >= 0 ? _state.cases[index].Snapshot() : null;
            if (updated == null || updated.Opened || updated.RemainingTaps == 0 ||
                updated.UpgradeTapsUsed != expectedTap) return false;

            var next = _state.Copy();
            Entry entry = next.cases[index];
            entry.rarity = CaseRewards.UpgradeForRoll(entry.rarity, _random());
            entry.taps++;
            Commit(next);
            updated = next.cases[index].Snapshot();
            return true;
        }

        public bool TryOpen(string caseId, out PendingCase opened)
        {
            int index = FindIndex(caseId);
            opened = index >= 0 ? _state.cases[index].Snapshot() : null;
            if (opened == null) return false;
            if (opened.Opened) return true;
            if (!opened.CanOpen) return false;

            var next = _state.Copy();
            Entry entry = next.cases[index];
            entry.gems = CaseRewards.GemsForRoll(entry.rarity, _random());
            var eligible = Array.FindAll(_offers, a => a.UsesCards && !OwnsAvatar(a.Id));
            if (eligible.Length > 0)
            {
                var offer = eligible[Math.Min(eligible.Length - 1, (int)(_random() * eligible.Length))];
                int[] minimum = { 1, 3, 6, 10 }, maximum = { 3, 6, 10, 20 };
                int tier = (int)entry.rarity;
                entry.avatarId = offer.Id;
                entry.avatarCards = Math.Min(offer.RequiredCards - CardsFor(offer.Id),
                    minimum[tier] + (int)(_random() * (maximum[tier] - minimum[tier] + 1)));
            }
            entry.opened = true;
            Commit(next);
            opened = next.cases[index].Snapshot();
            return true;
        }

        public bool TryClaim(string caseId, out int gems)
        {
            gems = 0;
            int index = FindIndex(caseId);
            if (index < 0 || !_state.cases[index].opened) return false;
            var next = _state.Copy();
            int reward = next.cases[index].gems;
            next.gems = checked(next.gems + reward);
            Entry prize = next.cases[index];
            var offer = Offer(prize.avatarId);
            if (prize.avatarCards > 0 && offer != null && offer.UsesCards && !OwnsAvatar(offer.Id))
            {
                var progress = Progress(next, offer.Id);
                progress.cards = (int)Math.Min(offer.RequiredCards, (long)progress.cards + prize.avatarCards);
                progress.owned = offer.Unlocked(next.peakAura, progress.cards);
            }
            next.cases.RemoveAt(index);
            Commit(next);
            gems = reward;
            return true;
        }

        private int FindIndex(string caseId)
        {
            if (string.IsNullOrWhiteSpace(caseId)) return -1;
            caseId = caseId.Trim();
            return _state.cases.FindIndex(entry => string.Equals(entry.id, caseId, StringComparison.Ordinal));
        }

        private void Commit(SaveData next)
        {
            // Publish state only after persistence succeeds; callers cannot see an uncommitted award.
            _persist(JsonUtility.ToJson(next));
            _state = next;
        }

        private static void ValidateSave(SaveData state)
        {
            if (state == null || state.version < 1 || state.version > 6 || state.gems < 0 || state.cases == null || state.awardedWorkouts == null)
                throw new InvalidOperationException("Invalid case ledger. Saved rewards were not overwritten.");
            foreach (string id in state.awardedWorkouts)
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidOperationException("Invalid case receipt. Saved rewards were not overwritten.");
            if (state.version >= 2)
            {
                bool noDay = string.IsNullOrEmpty(state.dailyWorkoutUtcDay);
                if (state.processedWorkoutIds == null || state.dailyWorkoutCount < 0 ||
                    (noDay && state.dailyWorkoutCount != 0) || (!noDay && !DateTime.TryParseExact(state.dailyWorkoutUtcDay,
                        "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
                    throw new InvalidOperationException("Invalid daily case eligibility. Saved rewards were not overwritten.");
                foreach (string id in state.processedWorkoutIds)
                    if (string.IsNullOrWhiteSpace(id))
                        throw new InvalidOperationException("Invalid processed workout. Saved rewards were not overwritten.");
            }
            if (state.version >= 3)
            {
                if (state.aura < 0 || state.avatars == null || state.auraReceipts == null ||
                    (state.version >= 6 && state.peakAura < state.aura))
                    throw new InvalidOperationException("Invalid avatar wallet. Saved rewards were not overwritten.");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var avatar in state.avatars)
                    if (avatar == null || string.IsNullOrWhiteSpace(avatar.id) || avatar.cards < 0 || !ids.Add(avatar.id))
                        throw new InvalidOperationException("Invalid avatar progress. Saved rewards were not overwritten.");
                ids.Clear();
                foreach (var receipt in state.auraReceipts)
                    if (string.IsNullOrWhiteSpace(receipt) || !ids.Add(receipt))
                        throw new InvalidOperationException("Invalid Aura receipt. Saved rewards were not overwritten.");
                ids.Clear();
                if (state.gemReceipts != null)
                    foreach (var receipt in state.gemReceipts)
                        if (string.IsNullOrWhiteSpace(receipt) || !ids.Add(receipt))
                            throw new InvalidOperationException("Invalid gem receipt. Saved rewards were not overwritten.");
            }
            var awards = new HashSet<string>(state.awardedWorkouts, StringComparer.Ordinal);
            var caseIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Entry entry in state.cases)
            {
                if (entry != null && entry.aura != 0)
                {
                    // Only a v4 save may still hold the sealed assessment Aura; v5 pays it out.
                    if (state.version != 4 || entry.id != CaseRewards.AssessmentReceipt ||
                        entry.aura != CaseRewards.LegacyAssessmentAura || entry.gems != 0 ||
                        entry.avatarCards != 0 || !string.IsNullOrEmpty(entry.avatarId) ||
                        entry.rarity != CaseRarity.Legendary || entry.taps != CaseRewards.UpgradeTapCount ||
                        !caseIds.Add(entry.id) || !awards.Contains(entry.id))
                        throw new InvalidOperationException("Invalid assessment case. Saved rewards were not overwritten.");
                    continue;
                }
                if (entry == null || string.IsNullOrWhiteSpace(entry.id) || !caseIds.Add(entry.id) || !awards.Contains(entry.id) ||
                    (int)entry.rarity < 0 || (int)entry.rarity > (int)CaseRarity.Legendary ||
                    entry.taps < 0 || entry.taps > CaseRewards.UpgradeTapCount || (int)entry.rarity > entry.taps ||
                    entry.avatarCards < 0 || entry.avatarCards > 20 || (entry.avatarCards > 0 && (!entry.opened || string.IsNullOrWhiteSpace(entry.avatarId))) ||
                    (!entry.opened && entry.gems != 0) || (entry.opened && (entry.taps != CaseRewards.UpgradeTapCount ||
                    entry.gems < CaseRewards.MinimumGems(entry.rarity) || entry.gems > CaseRewards.MaximumGems(entry.rarity))))
                    throw new InvalidOperationException("Invalid pending case. Saved rewards were not overwritten.");
            }
        }
    }
}
