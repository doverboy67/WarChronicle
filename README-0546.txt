War Chronicle Browser Update 0546 (v0.5.0.46)
===================================================
Source: WarChronicleBrowser_Update_0545.zip.

DIAGNOSTIC SCROLL SPEED (deliberately NOT the final setting):
- Chronicle animation is a conspicuous FOUR SECONDS (4,000 ms), using
  the existing ease-out cubic movement and scroll anchoring.
- To isolate the cause of instant scrolling, this diagnostic bypasses the
  prior reduced-motion instant-scroll short circuit TEMPORARILY.
- If it still snaps to position, the issue is NOT an excessively short
  animation duration; investigate scroll anchoring/focus/render sequence.
- If the four-second movement is visible, restore the user's preferred
  duration for the next release (and respect reduced motion again).
- The animation changes only the Chronicle panel, not the whole page.

BALANCE / GAMEPLAY:
- Inspired's acquisition cost: 2 Leadership + 1 Research instead of
  2 Coin + 1 Research, with its effect unchanged. Steady Advance and
  its interaction with Inspired are unchanged.
- Plague Pit: Search is disabled with an explanation when no Armor
  Advancements remain in the acquisition supply.
- Cursed Battlefield: Dig is disabled with an explanation when neither
  Halberds nor Longbows remains obtainable. Leaving remains available.
- Existing 0545 Finale difficulty (3/4/5 Advancements) remains intact.

CHRONICLE AND PERFORMANCE:
- Resource-token icons now render compact Advancement shorthand such
  as "1RT", "1R", "2C" and "2L" in acquisition narration and on
  Advancement offer cards. Existing saved log text is preserved;
  rendered displays retain legible words next to the matching icons.
- Acquisition and past-tense spend lines now qualify for icon treatment;
  acquired resources and costs get the same restrained transaction marks.
- Cached the formatted Chronicle HTML per UI instance, plus Echo icon
  and Arrival metadata lookups, to reduce repeated work on Blazor renders.
  This is a plausible optimization, NOT a confirmed cure for lag.
- Still renders only the most recent 120 Chronicle entries by default,
  with Show Earlier History available; full log export is unchanged.
- Completed Chronicle choices/checkmarks, Harvest and R&R workspaces,
  and UI layout/scrolled text retention remain unchanged.

DELIVERY:
- WarChronicle.Web hosted version and WarChronicle.Pages static version
  mirror gameplay, scripts, and styles. Pages MUST remain free of the
  InteractiveServer render-mode directive.
- CSS/script version query strings updated to 0546 for cache refresh.
- Sign-in / player identity / email reporting stay deferred.
- Bundled rulebook PDF remains unchanged by this source update.
- Compile and browser smoke test NOT performed: .NET SDK absent here.
