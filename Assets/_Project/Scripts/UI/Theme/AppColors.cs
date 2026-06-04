using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Runtime-accessible colour palette. Defaults match the canonical PushStarsTheme asset.
    /// Call AppColors.Apply(theme) once at boot; ThemeInitializer does this automatically.
    ///
    /// Colour reference: docs/design/screens/
    ///   01-duel-main.png   → BgDark, AccentBlue, AccentYellow, NavBg
    ///   04-fight-hud.png   → ArenaRed, ArenaBlue, AccentYellow (big reps)
    ///   06-result-victory.png → AccentLime (ПОБЕДА), BtnSuccessBg (ДАЛЕЕ)
    ///   07-search-opponent.png → DangerRed (ВЫЙТИ)
    /// </summary>
    public static class AppColors
    {
        // ── Backgrounds ──────────────────────────────────────────────────────────
        public static Color BgDark          = new Color32( 19,  19,  31, 255); // #13131F
        public static Color NavBg           = new Color32( 28,  28,  46, 255); // #1C1C2E

        // ── Primary accent ───────────────────────────────────────────────────────
        public static Color AccentBlue      = new Color32( 45,  91, 227, 255); // #2D5BE3

        // ── Secondary accents ────────────────────────────────────────────────────
        public static Color AccentYellow    = new Color32(245, 200,  66, 255); // #F5C842
        public static Color AccentLime      = new Color32(107, 255,  74, 255); // #6BFF4A

        // ── State ─────────────────────────────────────────────────────────────────
        public static Color TrophyGold      = new Color32(245, 200,  66, 255); // #F5C842
        public static Color DangerRed       = new Color32(198,  40,  40, 255); // #C62828

        // ── Arena ────────────────────────────────────────────────────────────────
        public static Color ArenaRed        = new Color32(122,  26,  26, 255); // #7A1A1A
        public static Color ArenaBlue       = new Color32( 26,  54, 128, 255); // #1A3680

        // ── Text ──────────────────────────────────────────────────────────────────
        public static Color TextPrimary     = new Color32(255, 255, 255, 255); // #FFFFFF
        public static Color TextSecondary   = new Color32(136, 136, 170, 255); // #8888AA

        // ── Buttons ───────────────────────────────────────────────────────────────
        public static Color BtnPrimaryBg    = new Color32( 45,  91, 227, 255); // Blue
        public static Color BtnPrimaryFg    = new Color32(255, 255, 255, 255);
        public static Color BtnSuccessBg    = new Color32( 69, 160,  73, 255); // Green
        public static Color BtnSuccessFg    = new Color32(255, 255, 255, 255);
        public static Color BtnSecondaryBg  = new Color32( 28,  28,  46, 255); // Dark
        public static Color BtnSecondaryFg  = new Color32(255, 255, 255, 255);
        public static Color BtnDangerBg     = new Color32(198,  40,  40, 255); // Red
        public static Color BtnDangerFg     = new Color32(255, 255, 255, 255);

        // ── Chips ────────────────────────────────────────────────────────────────
        public static Color ChipSelectedBorder = new Color32(245, 200,  66, 255);
        public static Color ChipSelectedFg     = new Color32(245, 200,  66, 255);
        public static Color ChipNormalFg       = new Color32(200, 200, 220, 255);

        // ── Exit Button ───────────────────────────────────────────────────────────
        public static Color BtnExitBorder = new Color32(255,  60,  90, 255);
        public static Color BtnExitFill   = new Color32( 62,  10,  10, 255);
        public static Color BtnExitFg     = new Color32(255,   2,  27, 255); // #FF021B

        // ── Ready Button ──────────────────────────────────────────────────────────
        public static Color BtnReadyBorder = new Color32(107, 255,  74, 255);
        public static Color BtnReadyFill   = new Color32( 15,  46,   5, 255);
        public static Color BtnReadyFg     = new Color32( 82, 255,   2, 255); // #52FF02

        /// <summary>
        /// Copies all colour tokens from a loaded PushStarsTheme ScriptableObject.
        /// </summary>
        public static void Apply(PushStarsTheme theme)
        {
            if (theme == null) return;

            BgDark             = theme.BgDark;
            NavBg              = theme.NavBg;
            AccentBlue         = theme.AccentBlue;
            AccentYellow       = theme.AccentYellow;
            AccentLime         = theme.AccentLime;
            TrophyGold         = theme.TrophyGold;
            DangerRed          = theme.DangerRed;
            ArenaRed           = theme.ArenaRed;
            ArenaBlue          = theme.ArenaBlue;
            TextPrimary        = theme.TextPrimary;
            TextSecondary      = theme.TextSecondary;
            BtnPrimaryBg       = theme.BtnPrimaryBg;
            BtnPrimaryFg       = theme.BtnPrimaryFg;
            BtnSuccessBg       = theme.BtnSuccessBg;
            BtnSuccessFg       = theme.BtnSuccessFg;
            BtnSecondaryBg     = theme.BtnSecondaryBg;
            BtnSecondaryFg     = theme.BtnSecondaryFg;
            BtnDangerBg        = theme.BtnDangerBg;
            BtnDangerFg        = theme.BtnDangerFg;
            ChipSelectedBorder = theme.ChipSelectedBorder;
            ChipSelectedFg     = theme.ChipSelectedFg;
            ChipNormalFg       = theme.ChipNormalFg;
            BtnExitBorder      = theme.BtnExitBorder;
            BtnExitFill        = theme.BtnExitFill;
            BtnExitFg          = theme.BtnExitFg;
            BtnReadyBorder     = theme.BtnReadyBorder;
            BtnReadyFill       = theme.BtnReadyFill;
            BtnReadyFg         = theme.BtnReadyFg;
        }
    }
}
