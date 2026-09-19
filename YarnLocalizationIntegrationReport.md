# Yarn Translation Integration Report

## Current State
- Investigation date: 2026-05-18
- Yarn Spinner package: `3.2.2`
- Package source: `Library/PackageCache/dev.yarnspinner.unity@3e46a90878d7/package.json`
- Active Yarn project: `Assets/Yarn/test project.yarnproject`
- Base language in the current Yarn project: `ja`

## Investigation Results
1. Current Yarn version
- `3.2.2`

2. `Export Strings as CSV` availability
- Available in the installed package.
- Inspector label: `Export Strings and Metadata as CSV`
- Internal implementation: `Yarn.Unity.Editor.YarnProjectUtility.WriteStringsFile(...)`

3. `Add Line Tags to Scripts` availability
- Available in the installed package.
- Inspector label: `Add Line Tags to Yarn Scripts`
- Internal implementation: `Yarn.Unity.Editor.YarnProjectUtility.AddLineTagsToFilesInYarnProject(...)`

4. `LocalizationAsset` structure
- `YarnProject` keeps:
  - `baseLocalization`
  - `localizations`
  - `lineMetadata`
- Built-in localization asset type: `Yarn.Unity.Localization`
- String storage shape:
  - key: line id such as `line:1234`
  - value: localized string and optional localized asset
- Standard Yarn CSV shape:
  - `language,id,text,file,node,lineNumber,lock,comment`

5. `DialogueRunner` language switching API
- `DialogueRunner` itself has no direct high-level language switch method.
- Built-in switching is performed through `BuiltinLocalisedLineProvider`:
  - `LocaleCode`
  - `AssetLocaleCode`
- Runtime line resolution uses `YarnProject.GetLocalization(localeCode)`.

## Implemented Changes
- Added editor utility: `Assets/Scripts/Editor/YarnLocalizationUtility.cs`
- Added menu path: `Tools/Nekolpos/Yarn`
  - `Generate Line Tags`
  - `Export Translation CSV`
  - `Import Translation CSV`
  - `Merge Dialogue Preview CSV`
- Added runtime locale sync:
  - `ConversationDataManager.SetLocale(...)`
  - `ConversationDataManager.LocaleChanged`
  - `YarnManager.ApplyLocale(...)`
  - `YarnManager` now syncs `ConversationDataManager.Locale` to `BuiltinLocalisedLineProvider`
- Added `zh-Hans` compatibility to the existing reactive conversation CSV loader.

## Translation Flow
1. Generate line tags for all Yarn projects.
2. Export translator-facing CSV to `Assets/Localization/Yarn/yarn_ja.csv`.
3. Import that CSV back into per-project Yarn standard CSV files.
4. Reimport Yarn projects so built-in localizations are rebuilt.
5. Merge reactive + Yarn rows into `Assets/Localization/Yarn/master_translation.csv`.

## Notes
- Node names remain unchanged.
- Jump targets remain unchanged.
- Variable keys remain unchanged.
- The `ja` column in `yarn_ja.csv` is treated as source reference. Import does not rewrite `.yarn` script text from that column.
- `zh-Hans` is normalized to Yarn locale key `zh-Hans`, while the reactive CSV side maps it to existing Chinese columns like `output_zh`.
