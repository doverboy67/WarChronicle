War Chronicle Browser Update 0514 - GitHub Pages Trial

Update 0514 (v0.5.0.14 Pages build):
- Added a separate WarChronicle.Pages Blazor WebAssembly project for GitHub Pages.
- The existing WarChronicle.Web InteractiveServer project is retained for local playtesting and automatic local GameLogs filesystem writes.
- Added .github/workflows/deploy-pages.yml. GitHub Actions builds WarChronicle.Pages and deploys it to GitHub Pages.
- The workflow automatically adjusts the Blazor base path for the repository name, including project sites such as /WarChronicle/.
- The Pages build uses the same game data, components, engine, images, rulebook, localStorage save flow, Debug tools, and downloadable JSON logs as Update 0513.
- Because GitHub Pages is static hosting, the Pages build cannot automatically write completed game logs to a server-side GameLogs folder. Browser archive + Download Game Log remain active there. A future logging backend can replace this without changing the log schema.
- The local WarChronicle.Web project still writes completed logs to WarChronicle.Web\GameLogs.

GitHub Pages quick start:
1. Create a public GitHub repository and place the contents of this ZIP at the repository root.
2. In GitHub: Settings > Pages > Build and deployment > Source = GitHub Actions.
3. Commit/push the files to the main branch.
4. Open the Actions tab to watch the Deploy War Chronicle to GitHub Pages workflow.
5. When it completes, GitHub will provide the Pages site URL.

War Chronicle Browser Update 0513

Update 0513 (v0.5.0.13):
- Added Debug Scene / Echo simulation. Unlock Debug by clicking the version number 5 times, choose any Scene or Echo from the dropdown, and run it through the real EventFlow resolver. Combat launched by a simulated card is fully interactive.
- Debug simulation does not remove/discard the selected card merely because it was launched from Debug. Card effects themselves, including Echo seeding and other state changes, still resolve normally.
- Multi-resource gains are now staged as a single reward instead of being packed in JSON node order. When multiple physical Resources are offered together, the player chooses what to pack next and may purge carried Resources to make room.
- Fixed the Battle Board casualty highlight so it spans the complete selected force area, including the Levy lane.
- Internal Combat target identifiers are translated before reaching the UI. DrPepperHost now displays as Dr. Pepper’s Host.
- Losing the Pepperridge Farm Remembers combat now preserves the authored Dr. Pepper / baked-goods campaign-ending text instead of collapsing to the generic defeat message.
- New Game now uses the same button styling as Download Game Log and the two controls have proper spacing.
- All Update 0512 automatic local JSON logging, defeat New Game flow, and earlier gameplay fixes are retained.

War Chronicle Browser Update 0512

Update 0512 (v0.5.0.12):
- Completed real campaigns are now automatically written as actual JSON files to WarChronicle.Web\GameLogs on the machine running the app.
- Log filenames use the completed-game timestamp format WC_GameLog_YYYY-MM-DD_HH-MM-SS.json. Same-second collisions are preserved with a numeric suffix instead of overwriting an earlier playtest.
- Browser localStorage logging remains as a redundant backup and the manual Download Game Log button remains available.
- Added a New Game button to the Campaign Complete / defeat panel. It appears only after a defeat and returns the player to content selection for a fresh Chronicle.
- GameLogs/*.json is ignored by Git so local playtest logs are not accidentally committed.
- All Update 0511 casualty-choice highlighting, persistent Battle History, hidden Debug tools, and earlier gameplay fixes are retained.

War Chronicle Browser Update 0511

Update 0511 (v0.5.0.11):
- Casualty choices now identify the force explicitly. Enemy casualty choices are labeled as Enemy units; player casualty choices are labeled as Your units.
- When a casualty decision is active, the relevant force row on the Battle Board is highlighted and the control panel displays a matching ENEMY HOST or YOUR HOST banner.
- Added persistent per-game JSON logging for playtests. The current game log is continuously mirrored to browser local storage, including an in-progress Battle Chronicle when a real Combat is active.
- Completed real Combats are retained in a structured Battle History with start/end times, source, opponent, outcome, initial/final forces, and the full detailed Battle Chronicle. Debug Test Combat/Test Finale sessions are excluded from campaign logs.
- Game-log identity, start/end timestamps, and Battle History are included in normal campaign saves and restored when a saved campaign is resumed.
- When a campaign ends, its completed log is archived in this browser and a Download Game Log button appears on the Campaign Complete panel.
- Individual downloaded logs use the agreed browser-local end-time format: WC_GameLog_YYYY-MM-DD_HH-MM-SS.json.
- Game logs also include the normal Chronicle, final state, enabled content, and Finale log when applicable, making the JSON suitable for later collection and analysis.
- Winter defeat now uses the same campaign-end path as Combat/Finale defeat so its game log is finalized consistently.
- All Update 0510 hidden Debug behavior and earlier gameplay fixes are retained.

War Chronicle Browser Update 0510

Update 0510 (v0.5.0.10):
- Test Combat and Test Finale are hidden during normal play.
- Click the version number 5 times within 3 seconds to toggle a compact Debug tools panel.
- Debug mode is intentionally session-only and resets on page reload; it is concealment for normal users, not a security boundary.
- The Debug panel now contains Test Combat, Test Finale, and Exit Debug, and is structured as a home for future developer/test utilities.
- All Update 0509 gameplay behavior is retained.

Update 0509 (v0.5.0.9):
- Restored Repair Baggage Train as a Work result.
- The repair option is offered only when at least one Baggage Train space is damaged and the Host has at least 1 Wood.
- Repairing uses the normal Work action (2 Time), spends 1 Wood, and repairs exactly 1 chosen damaged space.
- The Work card text now states the repair option.

Update 0508 (v0.5.0.8):
- Fixed Unstable Pitch Baggage Train adjacency. Adjacent slots are now determined by the visible two-column Baggage Train layout instead of simple numeric/list order.
- Orthogonal adjacency is used: slots sharing an edge are adjacent; diagonals are not. For example, Firepots in Space 6 may damage Space 4 or Space 5.
- All Update 0507 Harvest Firepots dumping behavior and prior expansion-token functionality are retained.

Update 0507 (v0.5.0.7):
- During Harvest preparation, Firepots may now be voluntarily dumped from the Baggage Train to free their occupied slot.
- Dumping Firepots removes the active Firepots token, so later Echo and Combat checks correctly treat it as gone.
- Harvest Undo restores dumped Firepots to the same Baggage Train slot and reactivates the token.
- Market sales remain Resource-only; Firepots cannot be sold.
- All Update 0506 expansion-token artwork and gameplay behavior are retained.

Update 0506 (v0.5.0.6):
- Main screen now displays War Chronicle v0.5.0.6.
- Added the supplied Seasons in the Abyss token art for Firepots, Painkiller, Forced March, and Reign in Blood.
- Firepots now appears as its physical token art when it occupies a Baggage Train slot.
- Expansion tokens now use their supplied art in the Reminders area while preserving the existing tooltip rules text.
- Combat now shows a compact Active Tokens strip for combat-relevant expansion tokens: Firepots, Painkiller, and Reign in Blood.
- Existing token behavior is retained: Firepots can be used before the first Combat round, Painkiller checks applicable Host losses, Forced March reduces normal Explore to 1 Time, and Reign in Blood prevents Disengage while active.
- All Update 0505 Chronicle iconography, battle-stack, Rulebook, R&R, save-flow, and gameplay fixes are retained.

Visual Studio remains the definitive compile/run check because this package was prepared without a local .NET SDK.