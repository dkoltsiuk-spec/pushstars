using System.Globalization;

namespace PushStars.Core
{
    /// <summary>
    /// Compact Aura numbers for the HUD: 850, 1.5K, 9.8K, 10K, 150K, 1.5M, 12M, 1.5B.
    /// Always rounded down, so 999 999 reads 999K (never "1000K") and the HUD never shows more
    /// than the player has. One decimal only below 10 of a unit, where it still carries meaning.
    /// </summary>
    public static class AuraFormat
    {
        public static string Short(long value)
        {
            if (value < 0) return "-" + Short(-value);
            if (value < 1000) return value.ToString(CultureInfo.InvariantCulture);
            if (value < 1000000) return Scaled(value, 1000, "K");
            if (value < 1000000000) return Scaled(value, 1000000, "M");
            return Scaled(value, 1000000000, "B");
        }

        /// <summary>Signed delta for reward labels: "+1000", "-500", "+150K".
        /// Exact digits up to 99 999 (the meme reads "+10000 aura"), compact above.</summary>
        public static string Delta(long value)
        {
            string sign = value < 0 ? "-" : "+";
            long magnitude = value < 0 ? -value : value;
            return sign + (magnitude < 100000 ? magnitude.ToString(CultureInfo.InvariantCulture) : Short(magnitude));
        }

        private static string Scaled(long value, long unit, string suffix)
        {
            long whole = value / unit;
            if (whole >= 10) return whole.ToString(CultureInfo.InvariantCulture) + suffix;
            long tenth = value % unit * 10 / unit;
            return (tenth == 0 ? whole.ToString(CultureInfo.InvariantCulture) : whole + "." + tenth) + suffix;
        }
    }
}
