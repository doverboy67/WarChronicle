War Chronicle Browser Update 0551 (v0.5.0.51)
===================================================
Based on WarChronicleBrowser_Update_0550.zip.

CAMP STEWARD REVISION
- Field Staff: Appoint Camp Steward costs 2 Coin again; Train Scouts remains 1 Coin.
- After Work, a player with Camp Steward may choose to search the surrounding
  area for opportunities (roll 1d6).
    1-4: A Willing Trader. Buy one Resource at the current Market Buy price minus
         1 Coin (minimum 1 Coin), OR sell one Resource at its Market Sell value.
    5-6: A Fortunate Discovery. Obtain one adjacent-Terrain Resource free.
- The player may decline to use the Steward. Free gains honor the same baggage
  overflow and purge rules as any other physical Resource gain.
- Work and Field Staff card text, costs, branch checks, prompts and persistent
  token help all reflect the revised rule.

RESOURCE PURCHASE / BAGGAGE FIX
- Single-Resource purchases from Explore, Market Day, Supply Convoy and Camp
  Steward no longer become unavailable solely because the Baggage Train is full.
- When necessary, the player is offered a list of occupied undamaged Baggage
  spaces to purge, or the option to decline and keep current cargo.
- Purging Food discards the contents of its space (up to two Food).
- Coin is paid only after a successful acquisition; declining a purchase does
  not cost Coin or discard cargo.
- Flow-based free resource gains already used the baggage overflow flow; the
  Steward's free-resource outcome now uses that same functionality.
- A disabled or otherwise non-purgeable Baggage slot cannot be chosen.

FINALE FIXES (QUEUED FROM 0550)
- Gritty (ADV-M8) now grants Middelalderen +1 Levy in regular and Custom Finale
  deployments. Existing Gritty anti-rout combat effect is unchanged.
- Custom Finale allows Mercenaries and Hedge Knights as separate input counts.
  They add to Infantry-class and Cavalry-class combat totals respectively,
  exactly as the existing normal Finale aggregation does. These special-unit
  inputs are identified separately in the Custom Finale transcript/test log.
- Both Test Finale and Custom Finale remain available only in the DEV/debug
  toolbar; nothing was added to the normal gameplay controls.

UNCHANGED
- Easy (3), Standard (4), Hard (5), Are You Nuts? (6) Advancement levels.
- All other 0550 fixes, campaign rules, and hosted/static project structure.

VERIFICATION
- Both WarChronicle.Web and WarChronicle.Pages are included and changed source
  files match between them except for their existing Razor platform differences.
- Data JSON and EventFlow links validated, JavaScript syntax checked with Node,
  and ZIP archive integrity verified.
- The .NET SDK is unavailable in this environment; this release has NOT been
  compiled or browser-tested. Compile and smoke-test locally before deployment.
