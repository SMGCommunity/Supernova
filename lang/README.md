# Interface languages

Supernova's buttons, labels, and tooltips are looked up here at runtime by their English text.
This folder sits next to the executable (a downloaded build's folder, or
`src/SMGEditor.Editor/bin/<Config>/net8.0/lang/` when running from source).

- `en.json` is the full catalog of every translatable string, regenerated from the source with
  `dotnet run --project src/SMGEditor.L10nExtract -- src/SMGEditor.Editor lang/en.json`. English
  itself is not loaded from a file, so this one is only a template.
- `<code>.json` (for example `ja.json`) is a translation. Copy `en.json`, rename it to the
  language code, and replace each value with the translation. An optional `"$language"` key sets
  the display name shown in the Settings dropdown.

A string with no entry, an empty value, or a missing file falls back to the English text, so a
partial translation is fine. Format placeholders like `{0}` must be kept.

Pick a language under **Settings** on the galaxy list; it applies immediately and is remembered in
`editorsettings.json`.
