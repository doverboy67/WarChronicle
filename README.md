# War Chronicle Browser

Build 0573 — `v0.5.0.73`. Two application projects are provided in `WarChronicleBrowser.sln`.

- **Local Visual Studio / ASP.NET Core:** start `WarChronicle.Web` with the HTTPS launch profile. Typical URL: `https://localhost:7148`.
- **Static GitHub Pages / WebAssembly:** deploy `WarChronicle.Pages` (the supplied GitHub Actions workflow builds it).
- **Before updating:** back up `WarChronicle-Save-Backup.json` using the existing browser, especially for a campaign in progress.
- **Important:** saved campaigns live in the browser's `localStorage` for the exact origin (protocol, hostname and port). Updating application source on the same origin does not clear the save.
- **Build validation:** run `python3 scripts/verify_release.py` and compile both projects. Source checks alone are not a substitute for an interactive browser test.

For release changes, see `CHANGELOG.md`; for current known issues, see `DEVELOPMENT_STATUS.md`. Prior individual audits, manifests and handoff files are retained in the 0563 and earlier archived releases; they are not duplicated in each new ZIP.
