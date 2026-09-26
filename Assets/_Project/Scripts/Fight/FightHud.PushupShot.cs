using PushStars.UI;
using UnityEngine;

namespace PushStars.Fight
{
    public sealed partial class FightHud
    {
        // Push-up size and ground line per the design mockups, in the 390-wide canvas units:
        // the hands' outer span, and how far the palms rest above the avatar image's bottom edge.
        internal static float DuelPlayerSpan = 275f, DuelPlayerFloor = 112f;
        internal static float DuelOpponentSpan = 234f, DuelOpponentFloor = 65f;
        internal static float SoloSpan = 314f, SoloFloor = 146f;

        /// <summary>Hands each stage its mockup push-up shot. A stage is matched by the image it
        /// renders into; one that was re-pointed elsewhere (the boss battle's portrait) is left
        /// to the screen that owns it.</summary>
        private void ApplyPushupShots()
        {
            foreach (var avatar in FindObjectsByType<FightAvatar>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var stage = avatar.StageCamera != null ? avatar.StageCamera.GetComponentInParent<CharacterStage>(true) : null;
                var image = stage != null ? stage.TargetImage : null;
                if (image == null) continue;
                if (_playerHalf != null && image.transform.IsChildOf(_playerHalf))
                {
                    if (_solo) avatar.SetPushupShot(SoloSpan, SoloFloor);
                    else avatar.SetPushupShot(DuelPlayerSpan, DuelPlayerFloor);
                }
                else if (_opponentHalf != null && image.transform.IsChildOf(_opponentHalf.transform))
                    avatar.SetPushupShot(DuelOpponentSpan, DuelOpponentFloor);
            }
        }
    }
}
