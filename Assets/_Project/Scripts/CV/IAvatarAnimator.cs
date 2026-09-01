using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Something that animates a character built at runtime. The fight screen instantiates its
    /// bodies after the scene loads — which body depends on a saved preference, and the opponent's
    /// exists only when there is a recording to fight — so no Animator can be serialized into the
    /// scene and every driver has to be handed one afterwards.
    ///
    /// <para>The interface lives here rather than beside the fight screen because both drivers must
    /// implement it and they sit in different assemblies: the CV-driven one ships with the pose
    /// stack, the ghost-driven one with the duel.</para>
    /// </summary>
    public interface IAvatarAnimator
    {
        /// <summary>Binds the driver to the body it should move. Called once, after the character
        /// has been instantiated and its controller assigned.</summary>
        void BindAnimator(Animator animator);
    }
}
