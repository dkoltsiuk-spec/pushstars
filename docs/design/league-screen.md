# League mock screen

Main contains the authored LeaguePanel, opened from the left navigation tab. LeagueArt holds editable UI elements; the former placeholder is retained inactive under LegacyLeaguePlaceholder. The default runtime tab remains Duel.

## Local data

LeagueView binds the title, trophy score, progress and current-player card to LocalProfile. Until online rankings are connected, only the local player's card is visible, with OFFLINE status and no invented countdown or rankings. Ten authored row slots remain available in the scene.

## Presentation

Opening League restarts a 1.63-second unscaled entrance: hero pop at 0s, title at 0.24s, score and progress frame at 0.46s, progress growth through 1.11s, season and online status, then staggered rows from 0.96s. Closing the tab cancels playback and restores resting transforms. All motion uses the existing UITween easing functions.

One clipped LeagueViewport spans the safe-area panel above the fixed bottom navigation (86 units reserved). Its ScrollRect moves LeagueArt, including the hero, title, score, progress, status and LeaderboardContent. Dragging anywhere in the viewport or using the mouse wheel moves the entire page. Row pitch is 69, leaving 8 units between 61-unit cards. Reopening resets the whole page to the top and scrolling unlocks when the entrance settles.

The background uses the square-corner Mask group (2) export. The progress track and fill use Rectangle 434 and Group 565. The fill stays at the full track size and uses Unity UI's horizontal Filled Image, revealing the artwork from left to right without stretching its slope or highlights. The moving boundary is vertical; the authored rounded end appears at full progress. The progress bar's authored X position is 72.

LeagueLayout scales the composition to the safe-area width and derives the page height from visible rows, with a minimum of 700 design units and 16 units after the last row. Short screens scroll instead of shrinking the entire page; hidden ranking slots do not create an empty tail. Layout updates preserve the scroll offset and clamp it to the current bounds. The backdrop extends behind device insets. Pop animations use separate centered wrappers, preserving the authored artwork and text positions.

LeagueTrophyField draws drifting trophy silhouettes in one UI mesh under LeagueBackground, behind all foreground content. It preserves the cup-pattern sprite's baked ~6% opacity instead of multiplying it by another .11. Cups rise at 12 reference units per unscaled second with gentle lateral motion and rotation. Screen-space vertex fading reaches zero in the bottom 22% of the background, rises smoothly to full sprite opacity at 58%, and fades out again at the top edge. The field stretches with the backdrop and does not receive pointer input. LeagueSceneSetup.InstallFloatingTrophies migrates the former nine static cups.

## Editor tools and validation

- LeagueSceneSetup.Build installs a new mock screen with a scene backup in Library/LeagueBackup.
- LeaguePresentationSetup.Install adds the entrance and ranking slots with whole-page scrolling; it is also called by the new-screen builder.
- LeaguePresentationSetup.InstallWholePageScrolling migrates the existing list viewport and preserves the entrance bindings. Running it again is safe.
- LeaguePresentationValidation.Run checks a separate preview copy of Main: local-player binding, staged reveal, progress, cancellation/reopening without drift, whole-page wheel/header dragging, fixed navigation and last-row visibility at three portrait sizes. A ten-row fixture is used only inside that preview.

Rendered examples and the latest validation report are written to output/league.
