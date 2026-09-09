namespace PushStars.CV
{
    /// <summary>
    /// A live-mirror component that can be told to stand down without being disabled. While
    /// <see cref="Suppressed"/> is set it behaves exactly as it does once the plank has armed —
    /// it hands the Animator back and stops writing bones or moving the body — so the character
    /// simply plays its idle clip.
    ///
    /// <para>The pre-duel card uses this: the player is still getting the phone into position and
    /// their on-screen body should be a calm standing figure, not a puppet of whatever they are
    /// doing at their desk. Toggling <c>enabled</c> instead would race the bind/rebind and the
    /// Animator hand-off; a flag routed through the already-tested armed path does not.</para>
    /// </summary>
    public interface IMirrorSuppressible
    {
        bool Suppressed { get; set; }
    }
}
