War Chronicle Browser Update 0547 (v0.5.0.47)
===================================================
Source: WarChronicleBrowser_Update_0546.zip.

TEMPORARY SCROLL-DURATION CONTROL:
- The New Chronicle setup screen, immediately beneath Middelalderen Finale difficulty,
  now has a numeric input: "Chronicle scroll duration (seconds)".
- Units are SECONDS. Examples: 0.5 is half a second, 1.5 is 1.5 seconds,
  and 4.0 is the currently confirmed smooth but deliberately slow duration.
- Range is 0.1 to 30 seconds. The Begin Chronicle button is disabled if
  an invalid number is entered.
- The selected value is applied before the new Chronicle begins and is
  remembered in this browser's local storage. A resumed campaign uses the
  same browser preference; this setting is not part of campaign rules or the
  game's saved/logged state.
- In a browser that has no saved preference, the field starts at 4.0 seconds.
- Scrolling retains the existing ease-out cubic curve, anchor handling,
  and Chronicle-only target. No changes were made to event/choice behavior.
- This is a temporary UI control until the designer selects a preferred speed.

UNCHANGED FROM 0546:
- Inspired acquisition: 2 Leadership plus 1 Research, effect unchanged.
- Plague Pit/Cursed Battlefield exhausted-Advancement restrictions.
- Coin, Research (RT), Leadership, Food, Wood, Stone resource icon formatting.
- Cached Chronicle formatting and card metadata lookups.
- Middelalderen 3/4/5 Advancement difficulty options, all gameplay.
- Persistent Chronicle choices/checkmarks and 120-entry display.
- Harvest, Refit & Reform workspaces, and unchanged game data.
- Login, player identity, and email reporting remain deferred.

DELIVERY:
- Web and Pages project source mirrors verified. GitHub Pages Home has no
  InteractiveServer render-mode directive.
- JS/CSS cache-busting tags bumped to 0547; Pages data-query version bumped.
- .NET SDK not available in this environment; build/browser smoke test pending.
