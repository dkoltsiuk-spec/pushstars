namespace PushStars.Fight
{
    public sealed partial class FightController
    {
        private RepMilestoneEffect _repMilestone;

        private void ShowRepMilestone(int acceptedSetReps)
        {
            if (acceptedSetReps < 10 || _hud == null) return;
            if (_repMilestone == null)
            {
                _repMilestone = _hud.GetComponent<RepMilestoneEffect>();
                if (_repMilestone == null) _repMilestone = _hud.gameObject.AddComponent<RepMilestoneEffect>();
            }
            _repMilestone.Observe(acceptedSetReps);
        }
    }
}
