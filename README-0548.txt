War Chronicle Browser Update 0548 (v0.5.0.48)
===================================================
Source: WarChronicleBrowser_Update_0547.zip.

SCROLLING FINALIZED:
- Every nontrivial Chronicle automatic scroll is now animated over a FIXED
  300 milliseconds (0.3 seconds), regardless of distance scrolled.
- Existing cubic ease-out motion, anchoring of previously visible entries,
  targeting of the first new Chronicle entry, and panel-only scroll remain.
- Removed the temporary New Chronicle scroll-duration input, its validation,
  CSS styling, and its C#/JavaScript preference plumbing.
- Previously stored experimental scroll-speed values are now IGNORED, so an
  older browser preference cannot override the fixed 300 ms duration.
- This is display-only, does not change saves or game mechanics, and applies
  to both resumed and newly started campaigns.

CARRIED FORWARD FROM 0547/0546/0545:
- Inspired acquisition costs 2 Leadership + 1 Research. Effect unchanged.
- Plague Pit search is unavailable once all Armor Advancements are owned;
  Cursed Battlefield dig is unavailable if both rewards are exhausted.
- Resource icons for Research Token (RT), Coin, Leadership, Food, Wood, Stone,
  including shorthand transaction costs and older Chronicle entries.
- Cached Chronicle formatting/card lookups to help avoid repetitive work.
- Middelalderen Finale difficulty: Easy 3, Standard 4, Hard 5 Advancements;
  existing weighted Advancement deck and defender Host are unchanged.
- Persistent choices/checkmarks, 120-entry Chronicle, Harvest/R&R workspaces,
  sound track link, game content and mechanics otherwise remain unchanged.
- Login, player identity, and email reporting are still deferred.

DELIVERY AND CHECKS:
- WarChronicle.Web (hosted) and WarChronicle.Pages (GitHub Pages) matched for
  shared assets and source after this edit; Pages Home must remain without
  the InteractiveServer render-mode directive.
- JS/CSS asset cache version strings and Pages data-query version are 0548.
- No .NET SDK in build environment: source/regression checks only, not a
  compiled or browser-tested release.
