# Ten-rep celebration

Training plays a 1.65-second vector celebration when the active set first reaches
10 accepted repetitions. The existing live counter remains the source of truth.
The effect has no reward or persistence writes and does not pause counting.

The timeline starts with a tapered gold lightning charge (0–240 ms), followed by
a purple/gold medal and Rubik “10 / PUSH-UPS!” lettering. A cyan shockwave, radial
streaks and deterministic particles accompany the spring entrance. The badge
settles, then lifts and fades from 1.2–1.65 seconds. The impact uses the existing
RewardComplete sound and medium haptic; both honor the app's settings.

The lightning uses connected contour strips with shared stations, asymmetric
widths, a pointed travelling head/tail and two tapered forks. Its bright core
follows the same silhouette; a translucent fringe softens the outer edge.
After impact the tail retracts into the medal by 640 ms. Sparks have pointed
diamond silhouettes rather than rectangular confetti. No independent thick-line
segments or capped joints are used.

`FightController.HandleRep` forwards training counts to `RepMilestoneEffect`.
The once-per-set latch resets when countdown enters Live. Pause and finishing a
set cancel the presentation without rearming it. Disabling the component hides
the overlay. The generated UI never receives raycasts and reuses its objects.
Boss battles, assessment and PvP do not trigger it.

Run **Tools → Push Stars → Validate Ten Rep Celebration**, or arm
`Temp/pushstars.rep-milestone` in an already open editor. The validator uses an
isolated preview of the authored Training scene, preserves open scenes, checks
threshold/reset/cleanup behavior and captures the timeline at 390×844 plus an
impact frame at 320×568, both rendered at 2× resolution for visual inspection.
Results are in `output/rep-milestone`.
The editor renders sample data; real camera timing and device feedback still
need an iOS/Android check.
