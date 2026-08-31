namespace PushStars.UI
{
    /// <summary>
    /// Which of the two main bodies the player wears. Purely an appearance choice: both figures
    /// are Humanoid rigs driven by the same retargeted clip set and the same CV pipeline, so
    /// nothing downstream of <see cref="CharacterRoster"/> has to know which one is on the stage.
    ///
    /// <para>The numbers are persisted, so they are fixed. A third body appends a new value.</para>
    /// </summary>
    public enum CharacterGender
    {
        Male   = 0,
        Female = 1,
    }
}
