using System;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>How rare a nickname color is. Basic is the free white everyone starts with.</summary>
    public enum NickColorTier { Basic, Common, Rare, Ultra }

    /// <summary>What the name does beyond its fill. Only ultra colors move.</summary>
    public enum NickColorEffect { Solid, Gradient, GoldShine, Rainbow, Neon, Stardust }

    public sealed class NickColor
    {
        public readonly string Id, Name;
        public readonly NickColorTier Tier;
        public readonly NickColorEffect Effect;
        /// <summary>Top and bottom of the letters; equal for a solid color.</summary>
        public readonly Color32 Top, Bottom;

        public NickColor(string id, string name, NickColorTier tier, NickColorEffect effect, Color32 top, Color32 bottom)
        {
            Id = id; Name = name; Tier = tier; Effect = effect; Top = top; Bottom = bottom;
        }
    }

    /// <summary>
    /// Nickname colors: white is free, the rest drop from epic and legendary cases and are picked in
    /// the nickname editor. Common ones are flat colors, rare ones two-tone gradients, ultra ones move
    /// (a gold shine, a flowing rainbow, a neon glow, stardust sparkles). Ids are saved; never rename one.
    /// </summary>
    public static class NickColorCatalog
    {
        public const string DefaultId = "white";

        private static NickColor Solid(string id, string name, uint rgb) =>
            new NickColor(id, name, NickColorTier.Common, NickColorEffect.Solid, Hex(rgb), Hex(rgb));
        private static NickColor Gradient(string id, string name, uint top, uint bottom) =>
            new NickColor(id, name, NickColorTier.Rare, NickColorEffect.Gradient, Hex(top), Hex(bottom));
        private static NickColor Ultra(string id, string name, NickColorEffect effect, uint top, uint bottom) =>
            new NickColor(id, name, NickColorTier.Ultra, effect, Hex(top), Hex(bottom));
        private static Color32 Hex(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        public static readonly NickColor[] All =
        {
            new NickColor(DefaultId, "WHITE", NickColorTier.Basic, NickColorEffect.Solid, Hex(0xFFFFFF), Hex(0xFFFFFF)),
            Solid("sky", "SKY", 0x5CC8FF),
            Solid("mint", "MINT", 0x4DF0A8),
            Solid("coral", "CORAL", 0xFF7A6B),
            Solid("lilac", "LILAC", 0xC79BFF),
            Solid("lemon", "LEMON", 0xFFF06A),
            Solid("tangerine", "TANGERINE", 0xFFA43A),
            Gradient("sunset", "SUNSET", 0xFFD24A, 0xFF4F7B),
            Gradient("ocean", "OCEAN", 0x8BF6FF, 0x2F7BFF),
            Gradient("toxic", "TOXIC", 0xEFFF5A, 0x22D46A),
            Gradient("candy", "CANDY", 0xFFA3E4, 0x9B5CFF),
            Gradient("frost", "FROST", 0xFFFFFF, 0x6FD3FF),
            Gradient("blaze", "BLAZE", 0xFFE45C, 0xFF3B2F),
            Ultra("gold", "GOLD", NickColorEffect.GoldShine, 0xFFEE6E, 0xFFA200),
            Ultra("rainbow", "RAINBOW", NickColorEffect.Rainbow, 0xFF5A5A, 0x5A8CFF),
            Ultra("neon", "NEON", NickColorEffect.Neon, 0xB9FFF8, 0x3DF5E4),
            Ultra("stardust", "STARDUST", NickColorEffect.Stardust, 0xF1E3FF, 0xA06BFF),
        };

        public static NickColor Find(string id) => Array.Find(All, c => c.Id == id);
        public static NickColor Default => All[0];

        public static string TierName(NickColorTier tier)
        {
            switch (tier)
            {
                case NickColorTier.Common: return "COMMON";
                case NickColorTier.Rare: return "RARE";
                case NickColorTier.Ultra: return "ULTRA";
                default: return "BASIC";
            }
        }
    }
}
