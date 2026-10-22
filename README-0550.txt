War Chronicle Browser Update 0550 (v0.5.0.50)
===================================================
Based on WarChronicleBrowser_Update_0549.zip.

CUSTOM FINALE DEBUG SETUP
- Debug toolbar: original randomized "Test Finale" retained; new "Custom Finale"
  opens a configurable setup before any Final Combat begins.
- Configure each army's Archer, Cavalry, Infantry, and Levy counts.
  Middelalderen's starting force receives the normal +1 bonus per affected
  Military unit type when one or more of the selected enemy Advancements apply.
  An explicit final-unit override is available for unusual experiments.
- Select individual player and enemy Advancement cards, including up to three
  separate Armor cards on each side. Up to ten enemy cards may be selected.
- Set Morale, Priest present/absent, and Leadership *after* the Host Resolve
  check. Custom Finale skips the Resolve roll and stages the specified matchup.
- Pre-commit legal Tactics and choose the Steady Advance Military unit type.
  Committed Tactics each consume 1 Leadership. Inspired and Loose Formation
  retain their in-combat triggers. Feigned Withdrawal is not available in a
  Finale, where Disengagement is prohibited.
- Preview selected enemy Advancements and deployed troops, then launch the
  ordinary Finale interactive combat engine. All existing battle mechanics,
  casualty choices, dice effects, and Tactic interactions are reused.
- Repeat the same matchup and random dice with "Restart Seed", or run the
  identical matchup with "New Dice" and a new seed.
- "Download Test Log" exports the chosen config, seed, selected Advancements,
  deployed units, combat dice transcript, and outcome. These are debug tests,
  not normal campaign results and do not enter campaign BattleHistory.
  Active custom test information is also included in diagnostic snapshots.
- The custom setup allows abnormal army sizes intentionally for edge cases.
  This does NOT alter normal army-size limits or normal Finale deployment.

CHRONICLE LOG DOWNLOAD FIX
- Fixed the Chronicle's "Download Game Log" button sometimes doing nothing
  when the browser log archive was full or unavailable. It now downloads
  directly rather than waiting for another archive write. The working header
  "Download Log" button is unchanged, as is best-effort automatic archiving.

UNCHANGED
- Difficulty levels: Easy 3, Standard 4, Hard 5, Are You Nuts? 6 Advancements.
- Middelalderen's regular starting Host: 2A / 2C / 6I / 6L, with the existing
  bonus-unit rule. No rule or card changes to campaign play.
- All 0549 Chronicle, Echo, Rapport, Winter reminder, and display fixes.
- Hosted WarChronicle.Web and static GitHub Pages WarChronicle.Pages included.

CHECKS / LIMITATION
- Original ZIP inventory retained, aside from new README and modified files.
- Equivalent changes applied to both platforms, data catalogs checked, JS
  syntax checked using Node, and ZIP integrity verified.
- There is no .NET SDK in this environment. 0550 has NOT been compiled or
  browser-tested; build both projects before using as a deployed release.
