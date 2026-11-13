# Exported preset JSON format

Probably Edited exports **one preset's overrides** as a share file. This file is a patch, not the `presets.json` workspace snapshot: it contains neither other presets nor the active preset ID, preset IDs, or original game text.

## Supported file versions

The current single-preset exporter writes `schemaVersion: 2`. The importer also accepts legacy patch files with `schemaVersion: 1`.

| Version | Root members (all required) | Preset name |
| --- | --- | --- |
| `2` — current export | `schema`, `schemaVersion`, `gameVersion`, `presetName`, `overrides` | Required; used when creating a new preset from the file. |
| `1` — legacy patch | `schema`, `schemaVersion`, `gameVersion`, `overrides` | Not present. A new target gets the next default preset name; merging into an existing target keeps that target's name. |

Both versions use `schema: "probably-edited"` and the same override records described below. Workspace storage uses a separate `probably-edited-workspace` schema with versions 1–3; it is not a share-file version and cannot be imported as one.

The exporter writes indented JSON with two spaces per level and sorts override records ordinally by `sourceId`, `locale`, `tableId`, then `key`. Imported array order does not affect matching.

## Fields and validation

JSON member names are case-sensitive. Member order is not significant, but each object must contain exactly the members for its version: missing or additional members are rejected.

| Field | Type | Required | Meaning and rules |
| --- | --- | --- | --- |
| `schema` | string | Yes | Must be exactly `probably-edited`. |
| `schemaVersion` | integer | Yes | Must be `2` or `1`. Other versions are unsupported. |
| `gameVersion` | string | Yes | The `PlayerStore.VERSION` value used by `GameTextCatalog` for the game's localization-content revision at export time. It is not the mod manifest's `<GameVersions>` compatibility list or `<ModVersion>`. The parser accepts any string, including an empty one. Import warns on a mismatch rather than blocking; if the current value is unavailable, preview warns that it cannot check. |
| `presetName` | string | Version 2 only | Exported preset's name. Must not be empty or whitespace-only. When imported as a new preset, surrounding whitespace is trimmed. An existing target preset keeps its own name. |
| `overrides` | array | Yes | May be empty. Each element is one replacement, with the five required string fields below. |

Each item in `overrides` has exactly these required fields:

| Field | Type | Meaning and rules |
| --- | --- | --- |
| `sourceId` | string | Source identity. Game localization tables use `game` (including third-party tables registered in that database, whose originating mod is not reliably attributed); mod localization uses the mod's manifest ID. |
| `locale` | string | Exact language/locale code for the entry, such as `en`, `fr`, `ptBR`, or `pt-BR`. The code is not normalized when read; `ptBR` and `pt-BR` are distinct identities. |
| `tableId` | string | Table identity. For game localization tables this is the table collection GUID in `N` format; for mod CSV localization it is `Localization`. |
| `key` | string | Entry identity within that source, locale, and table. For game tables it is the shared-data numeric key ID written as a string; for mod CSV localization it is the exact CSV key. |
| `text` | string | Replacement text. Empty string is allowed and preserved as an override; `null` is rejected. An empty game-table value may trigger Unity language fallback, so it is not a reliable clear/hide command. |

All four identity strings (`sourceId`, `locale`, `tableId`, `key`) must contain at least one non-whitespace character. Identity matching is exact and case-sensitive across the whole tuple `(sourceId, locale, tableId, key)`. Values are not trimmed or case-folded. A repeated tuple in one file is a duplicate override and is rejected.

ProbablyEdited's own translations use regional resource filenames such as `en-US.csv` and `zh-CN.csv`. After loading, the official dictionary also exposes the game's native keys (`en`, `zh`, and so on) as aliases of the same tables. Editing this mod through a native game language therefore keeps the existing `locale` identity in presets. Do not change an existing JSON locale merely because the CSV filename changed.

If one preset uses multiple locale aliases for the same runtime dictionary key (such as this mod's `en` and `en-US`), identical replacement text can be applied together. Different replacement text makes **Save and Apply** fail without changing the previously applied text. Keep the override under only one alias, or make the values identical before trying again. This check happens when applying a preset, separately from the import preview's conflicts based on the full four-part identity.

The exporter emits standard JSON. The importer reads with Json.NET and explicitly requires an object root, rejects comment tokens, duplicate JSON property names, and content after the first JSON value, then checks the field names and types described above. Json.NET accepts some syntax extensions, so this is not a strict validator for every lexical rule of the JSON standard. An empty `overrides` array is valid; the parser defines no fixed upper bound for override count, JSON size, or string length.

## Smallest valid version 2 file

This file is structurally valid and contains no text changes:

```json
{
  "schema": "probably-edited",
  "schemaVersion": 2,
  "gameVersion": "example-game-version",
  "presetName": "Sample preset",
  "overrides": []
}
```

## Example with replacements

```json
{
  "schema": "probably-edited",
  "schemaVersion": 2,
  "gameVersion": "example-game-version",
  "presetName": "Sample localization edits",
  "overrides": [
    {
      "sourceId": "example.mod",
      "locale": "en",
      "tableId": "Localization",
      "key": "welcome",
      "text": "<b>Welcome, {0}</b>\r\nChoose a route.\n"
    },
    {
      "sourceId": "example.mod",
      "locale": "fr",
      "tableId": "Localization",
      "key": "farewell",
      "text": "À bientôt."
    },
    {
      "sourceId": "example.mod",
      "locale": "en",
      "tableId": "Localization",
      "key": "intentionally-empty",
      "text": ""
    }
  ]
}
```

`example.mod` and these keys are fictional, deterministic examples, not a game-data extract. They are structurally valid, but they will only match entries whose complete identities are exactly the same. The `\r\n` and `\n` escapes decode to different line breaks; the importer does not normalize line endings. Unicode, tags, braces, and other text are kept as string content. The empty `text` is an explicit override; its runtime display behavior depends on the source.

The editor's text-edit check gives advisory warnings when placeholder tokens or rich-text tag names differ from the local original and checks basic brace balance. Importing a JSON file does **not** run that check or parse the text's formatting language. The `example.mod` `Localization` records use the numbered CSV placeholder style, such as `{0}`. Game string-table entries may instead be Unity Localization Smart Strings with their own variables, conditions, and plural formatting; the file stores only the text and has no Smart String flag. Preserve the syntax required by the source entry; the JSON schema cannot guarantee that arbitrary replacement text will render correctly.

## Legacy version 1 example

Version 1 has no `presetName` member:

```json
{
  "schema": "probably-edited",
  "schemaVersion": 1,
  "gameVersion": "example-game-version",
  "overrides": [
    {
      "sourceId": "example.mod",
      "locale": "en",
      "tableId": "Localization",
      "key": "welcome",
      "text": "Welcome, {0}!"
    }
  ]
}
```

Version 1 remains supported for importing older patch files. New single-preset exports use version 2.

## What import does

1. Preview the file and choose either an existing preset or a new-preset target. Previewing a new target does not create it.
2. The import matches each entry by its exact four-part identity and merges it into the chosen target. Entries absent from the file are left alone; the file does not replace or clear the target preset.
3. A conflict is an identity already overridden in the target with different text. Identical text is not a conflict. Choose whether to keep the target's value or overwrite conflicting values with the incoming text; non-conflicting entries are merged either way.
4. Confirming stages the merge in the workspace. It does not immediately change the game's applied text or save the workspace; use **Save and Apply** to persist and apply it.

The importer lists `.json` files in the mod data folder, except the root workspace and legacy files `presets.json`, `presets.previous.json`, `active.json`, and `previous.json`.

A new imported preset uses the version 2 `presetName` (trimmed), or the next default name for version 1. The workspace allows at most 36 presets: importing a new preset requires remaining capacity, while merging into an existing preset remains possible at capacity. If the workspace or selected target changes after preview, preview the file again.

The file records `gameVersion` and source IDs for context and identity; these are not dependency instructions. It does not contain dependency lists, mod display names, mod versions, or baseline text. The importer does not download or install sources. A missing source or entry is retained as an override; the unavailable count only checks the currently loaded locale's catalog, and other locales are not counted as missing. Install or enable the relevant source separately if you need the entry to resolve.

The export UI also requires a unique file name of at most 80 UTF-16 code units and saves the share file under the mod's `exports` data folder. This file-name rule is separate from the JSON format; the JSON importer defines no fixed override-count or text-length limit.
