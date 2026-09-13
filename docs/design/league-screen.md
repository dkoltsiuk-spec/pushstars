# League mock screen

Main contains the authored LeaguePanel, opened from the left navigation tab. LeagueArt holds editable UI elements; the former placeholder is retained inactive under LegacyLeaguePlaceholder. The default runtime tab remains Duel.

## Local data

LeagueView exposes SILVER LEAGUE, header score 955, progress 77%, 126 online and a 108-hour session countdown. The first four reference scores are 799 / 721 / 611 / 554; six additional mock players bring the list to ten. Header and highlighted-row values retain the supplied reference. No Firestore reads or player-state writes are made by the league fixture.

## Presentation

Opening League restarts a 1.63-second unscaled entrance: hero pop at 0s, title at 0.24s, score and progress frame at 0.46s, progress growth through 1.11s, season and online status, then staggered rows from 0.96s. Closing the tab cancels playback and restores resting transforms. All motion uses the existing UITween easing functions.

The leaderboard has a 229-unit clipped viewport above navigation and 682 units of content. Row pitch is 69, leaving 8 units between 61-unit cards. It supports vertical dragging and mouse-wheel scrolling, with the fourth player highlighted blue. Reopening resets to the top and scrolling unlocks when the entrance settles.

The background uses the square-corner Mask group (2) export. The progress track and fill use Rectangle 434 and Group 565. The complete fill sprite resizes to retain its rounded, slanted end. The progress bar's authored X position is 72.

LeagueLayout fits the composition within the safe area and extends the backdrop behind device insets. Pop animations use separate centered wrappers, preserving the authored artwork and text positions.

## Editor tools and validation

- LeagueSceneSetup.Build installs a new mock screen with a scene backup in Library/LeagueBackup.
- LeaguePresentationSetup.Install adds the entrance and ten-player list to the existing screen; it is also called by the new-screen builder.
- LeaguePresentationValidation.Run checks a separate preview copy of Main: saved bindings, ten players, staged reveal, progress, cancellation/reopening without drift, row spacing and visibility of rank 10 inside the scroll viewport.

Validation passed. Rendered examples and the report are in output/league: entrance-hero.png, entrance-progress.png, top-ten.png, top-ten-bottom.png and entrance-validation.txt.
