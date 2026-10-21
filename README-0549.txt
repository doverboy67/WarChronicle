War Chronicle Browser Update 0549 (v0.5.0.49)
===================================================
Source: WarChronicleBrowser_Update_0548.zip.

NEW FINALE DIFFICULTY
- New Chronicle now offers "Are You Nuts?" with SIX Middelalderen
  Advancements, beside Easy (3), Standard (4, default), and Hard (5).
- Middelalderen's starting Host remains 2 Archers, 2 Cavalry,
  6 Infantry, 6 Levy before Advancement bonus units are applied.
- Existing Advancement bonus-unit rule and ten-card weighted enemy
  Advancement deck stay unchanged; no special army reallocation.
- Six-Advancement selection is saved, restored, and exported in game logs.

CHRONICLE & CHOICE CLARITY
- Echo's "Seeded by" appears directly below the Echo title and before
  flavor text instead of in the calendar stamp, including old saves.
- Hedge Knights' Explore event and recruitment choice show a separate,
  italic reminder that Hedge Knights disband in Winter.
- Hire Mercenaries' Camp card and choice show a separate, italic reminder
  that Mercenaries disband in Winter; repeated rule bullet removed.
- Arrival choices now name the affected tribe when offering Rapport
  gains or losses. The Chronicle's "You choose" line also names it.
  When a tribe is selected for a Camp action, the selected tribe is named
  in applicable Rapport outcomes where the selection is already known.
- World Painted Blood and other Echo test prompts with printed Pass/Fail
  bullet outcomes now state BOTH outcomes alongside each test option
  BEFORE the player chooses how to roll or spends Leadership.
- Rapport losses report the actual resulting status for the tribe,
  including cases when its status does not change, and use the same
  Hostile/Neutral/Friendly/Allied text colors as the Game State panel.
- Printed game effects and dice/combat mechanics are otherwise unchanged.

FIXED EXISTING DATA MISMATCH
- Fixed the GitHub Pages public wc_data.json stale Inspired acquisition
  cost (had 2 Coin + 1 Research). It now matches the source and hosted
  catalog: 2 Leadership + 1 Research. Inspired's effect is unchanged.

CARRIED FORWARD
- Fixed 300ms Chronicle scroll ease-out, unchanged.
- 0546's Plague Pit/Cursed Battlefield exhausted-reward guards, token
  icons, and caching; 0545's persistent choices and Finale selector.
- Hosted WarChronicle.Web and standalone WarChronicle.Pages both included.
- Automatic game login and end-game email notifications remain deferred.

VERIFICATION
- Static source and data consistency assertions passed for both projects.
- JS syntax validation with node passed, JSON catalogs parsed, ZIP checked.
- .NET SDK is not installed in this environment, so the projects were NOT
  compiled or browser-tested here. Build before production deployment.
