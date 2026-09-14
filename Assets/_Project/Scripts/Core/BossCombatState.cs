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
        public BossCombatState(string bossId) { BossMaxHp = bossId == "novice" ? 450 : 1000; BossHp = BossMaxHp; }
        public static int RepDamage(float form)
            => 70 + Mathf.RoundToInt(30 * Mathf.Clamp01(float.IsNaN(form) || float.IsInfinity(form) ? 0 : form / 100));
        public void PlayerRep(float form)
        {
            if (Knockout) return;
            int damage = RepDamage(form); BossHp = Mathf.Max(0, BossHp - damage); Damaged?.Invoke(true, damage);
        }
        public void BossAttack()
        {
            if (Knockout) return;
            PlayerHp = Mathf.Max(0, PlayerHp - BossAttackDamage); Damaged?.Invoke(false, BossAttackDamage);
        }
    }
}
