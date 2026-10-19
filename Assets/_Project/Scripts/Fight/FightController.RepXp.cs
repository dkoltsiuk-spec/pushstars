namespace PushStars.Fight
{
    public sealed partial class FightController
    {
        private RepXpPopups _repXp;

        /// <summary>Throws the XP an accepted rep just earned up off the player's back.</summary>
        private void ShowRepXp(float form, bool clap = false)
        {
            if (_phase != Phase.Live || _paused || _layoutPaused || _hud == null || _session == null) return;
            if (_repXp == null)
            {
                _repXp = _hud.GetComponent<RepXpPopups>();
                if (_repXp == null) _repXp = _hud.gameObject.AddComponent<RepXpPopups>();
                _repXp.Bind(_session);
            }
            _repXp.Show(form, clap);
        }
    }
}
