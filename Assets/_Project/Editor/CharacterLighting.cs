using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>
    /// The two lights every character stage is lit by, in one place.
    ///
    /// <para><b>Why a shared constant and not a value in each scene.</b> Every stage in this project
    /// is generated from code, so a light tuned by hand in the Scene view survives exactly until the
    /// next build regenerates that scene. Tuning still happens in the Inspector — that is the only
    /// way to judge it — but the numbers land here afterwards, which is the same round trip
    /// <see cref="FontSetup"/> documents for the text style.</para>
    ///
    /// <para>The same body appears on the menu stage and in a duel and has to read identically in
    /// both, so colour and intensity are shared. <b>Angles are not:</b> the menu camera stands on
    /// −Z and the duel cameras on +Z, so each tool keeps its own rotations — copying these across
    /// would light one of the two from behind.</para>
    ///
    /// <para>Values dialled in on the Main stage, 2026-09-01.</para>
    /// </summary>
    internal static class CharacterLighting
    {
        /// <summary>Warm key. High: the toon shader caps how much a scene light may tint the body
        /// (<c>_LightInfluence</c> on the material, 0.35), so intensity here is pushing against that
        /// ceiling rather than lighting a surface in the usual sense.</summary>
        public const float KeyIntensity = 6.77f;
        public static readonly Color KeyColor = Color.white;

        /// <summary>Cold fill from the opposite side. Deeply blue rather than a pale wash — it is
        /// what keeps the unlit half of the figure from going flat grey against the arena.</summary>
        public const float FillIntensity = 0.45f;
        public static readonly Color FillColor = new Color(0f, 0.3421054f, 1f);
    }
}
