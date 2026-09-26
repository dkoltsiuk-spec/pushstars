using System;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>Deterministic PvE health; accepted CV reps are the only player damage source.</summary>
    public sealed class BossCombatState
    {
        public const int PlayerMaxHp = 1000;
        public const int BossAttackDamage = 75;
        public int BossMaxHp { get; }
        public int BossHp { get; private set; }
        public int PlayerHp { get; private set; } = PlayerMaxHp;
        public bool Knockout => BossHp == 0 || PlayerHp == 0;
        public bool PlayerWins => BossHp == 0 && PlayerHp > 0 || !Knockout && PlayerHp > BossHp;
        public bool Draw => PlayerHp == BossHp;
        public event Action<bool, int> Damaged;
        /// <summary>A clap push-up's second strike landed on the boss (argument: its damage). Fires
        /// right after the matching <see cref="Damaged"/>, so presentation can tell it apart.</summary>
        public event Action<int> ClapStrike;
        public BossCombatState(string bossId) { BossMaxHp = BossCatalog.Find(bossId)?.MaxHp ?? 1000; BossHp = BossMaxHp; }
        public static int RepDamage(float form)
            => 70 + Mathf.RoundToInt(30 * Mathf.Clamp01(float.IsNaN(form) || float.IsInfinity(form) ? 0 : form / 100));
        public void PlayerRep(float form)
        {
            if (Knockout) return;
            int damage = RepDamage(form); BossHp = Mathf.Max(0, BossHp - damage); Damaged?.Invoke(true, damage);
        }
        /// <summary>A clap push-up deals double damage: its rep already hit through
        /// <see cref="PlayerRep"/>; the clap, confirmed on landing, strikes the same amount again.</summary>
        public void PlayerClapStrike(float form)
        {
            if (Knockout) return;
            int damage = RepDamage(form); BossHp = Mathf.Max(0, BossHp - damage);
            Damaged?.Invoke(true, damage); ClapStrike?.Invoke(damage);
        }
        public void BossAttack()
        {
            if (Knockout) return;
            PlayerHp = Mathf.Max(0, PlayerHp - BossAttackDamage); Damaged?.Invoke(false, BossAttackDamage);
        }
    }
}
