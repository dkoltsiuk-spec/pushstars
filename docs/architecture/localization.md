# Interface languages

The application supports English (`en`), Russian (`ru`) and Brazilian Portuguese (`pt-BR`).
English is the default. Settings → Language opens a picker with native language names.
The selection is saved in `settings.language` and applies immediately, including inactive panels.
Unsupported codes fall back to English; `pt` is accepted as an alias for `pt-BR`.

`Assets/_Project/Resources/Localization/interface.json` contains English source copy and
the `ru` and `ptBR` translations. Keep the English key stable. `{0}`, `{1}`, etc. match
runtime values such as timers and counts. Preserve placeholders and TMP markup in every
translation. Specific templates take priority over generic suffixes.

`Localization` loads the catalog and caches lookups. `InterfaceLocalization` starts before
the first scene, tracks authored and runtime TMP labels, and keeps their original source text.
It refreshes changed labels before canvas rendering and restores the English source when
switching back. Input text and player-name labels are excluded. Unknown copy stays unchanged.
New player-name fields should use the existing `PlayerName`, `OpponentName`, `FriendName`
or `Name` conventions. For new copy, add both translations to the catalog.

Translated labels can shrink within their authored bounds. `FontSetup.BakeGlyphs` pre-bakes
Latin, Portuguese accents and Russian Cyrillic in all Rubik atlases; build preparation
already calls this method. Keep generated font assets with their existing GUIDs.

Verification:

- **Tools → Push Stars → Validate Interface Languages** checks catalog completeness,
  placeholders, markup, timer templates, locale normalization and persistence. Output:
  `output/localization/catalog-validation.txt`.
- `LocalizationValidation.RuntimeLabels()` in Play Mode checks runtime initialization,
  active/inactive and dynamic labels, restoration, and user-content protection. Output:
  `output/localization/runtime-validation.txt`.
- `python tools/check_english.py` checks the authored English source; Russian catalog
  content and the native name in the picker are intentional.
- The mobile experience Play suite exercises the three language choices in settings.

This supersedes the English-only language behavior recorded in the September mobile audit.
External privacy/terms pages and baked text in artwork or video are separate content.
