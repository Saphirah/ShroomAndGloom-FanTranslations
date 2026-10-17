# Build and release

The installer and startup updater use the latest published release in
`Saphirah/ShroomAndGloom-FanTranslations`. Drafts and prereleases are ignored.

## Build

Windows x64, .NET SDK 8+, Python 3 and supported game installation are required.

```powershell
./tools/build.ps1 -GameDir 'E:\SteamLibrary\steamapps\common\Shroom and Gloom'
```

German source edits live in `data/editorial-*.json`; the build rebuilds its catalog.
Other community edits can be made directly in `data/languages/<locale>.json`.
Keep game tokens, markup, IDs and boundary whitespace unchanged.

`tools/build_languages.py` generates missing-language drafts and performs a separate
AI editorial pass using an existing `GEMINI_API_KEY` environment variable. It checkpoints
progress and enforces an estimated cost limit. `tools/review_languages.py` adds a focused
high-reasoning terminology/quantity pass; review both scripts' diffs before release.
`SHROOM_TRANSLATION_MODEL` selects a supported model with verified pricing; daily
quota exhaustion stops that model's work while preserving completed checkpoints.
Generation is optional for normal builds;
never regenerate community-corrected packs without reviewing the diff.
The original English and reviewed German packs do not require API calls.

## Release

1. Change the package version in `data/release.json`. Change the plugin version only
   for runtime changes; a text-only version must preserve the existing plugin binary.
2. Validate catalogs and build plugin/installer when needed.
3. After approval, commit the complete source state and run `python tools/package.py --release`.
4. Tag that commit `v<package version>`. Attach these files to the release:
   - `dist/package-info.json`
   - `dist/ShroomAndGloom-FanTranslations.zip`
   - every `dist/language-*.zip` listed in the manifest
   - a new installer EXE only when installer/plugin rollout requires it
   - `dist/SHA256SUMS.txt`
5. Publish only with user authorization. Attach all assets before publication.

The full package includes the loader, plugin, bundled OFL font fallbacks and every
language. Individual language archives contain exactly one language JSON and the
same manifest as the full package. The GitHub asset digest and all contained file
hashes are validated before any installation writes.

The manifest stores a full commit SHA, game-code fingerprint, package version,
`InstallerProtocol: 2`, complete file hashes and per-language asset mappings.
Uncommitted preview packages carry `SourceDirty: true` and cannot be installed
from a published release. `--release` refuses dirty source trees.

## Updating

The installation record keeps the installed commit and full package snapshot
separate from ownership of files. Runtime additions/removals/changes or missing
runtime files require a full installer download. Unchanged runtime with changed
language files downloads only those individual language archives. Identical file
hashes fetch metadata only, even if a new commit was released.

The mod checks once at startup in a managed background task. It never replaces
executable code while the game is running. It validates language JSON, preserves
local edits, writes atomically and updates the ownership/commit record. Unity tables
are refreshed on the game thread. Network failures preserve the installed text.
`SprachpaketBeimStart = false` under `[Aktualisierung]` disables the check.

The language dropdown uses native language names and remembers the selection.
English is the source fallback. Original game fonts remain primary; bundled Noto
fonts are loaded from the mod's files for missing glyphs, without a Windows font installation.
The original plugin GUID and `BepInEx/plugins/ShroomGerman` path are deliberately
retained so earlier German-only installations can upgrade and uninstall safely.

## Checks

```powershell
python tools/validate_languages.py
dotnet run --project tools/InstallerChecks -c Release -- dist/ShroomAndGloom-FanTranslations.zip
```

Checks use isolated HTTP and filesystem fixtures. Installation checks are skipped
when the real game is running; they never start, stop or modify the real game.
Unity rendering, language switching and runtime font appearance require game acceptance.
AI-reviewed translations are not claimed to be native-speaker-certified.
