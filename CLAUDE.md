# YASG (Yet Another Singing Game) — Project Guide

## Project Overview

YASG is a Unity karaoke game supporting desktop and Android. It uses TextMeshPro for all UI text and Newtonsoft.Json for serialization.

## Localization System (CRITICAL — Read Before Touching Any UI Text)

Every user-facing string in the game **must** go through the localization system. Never hardcode display text directly into `TMP_Text.text` in code — always use a localization key.

### Architecture

```
Assets/Resources/Localization/
  en.json      ← English (source of truth, fallback for missing keys)
  pt-BR.json   ← Portuguese (Brazil)
  (future language files go here as {code}.json)
```

Each JSON file is a flat `{ "key": "value" }` dictionary. The special key `"_language_name"` holds the display name (e.g. `"English"`, `"Portugues (Brasil)"`).

Key components:
- **`LocalizationManager`** (`Assets/Scripts/Localization/LocalizationManager.cs`) — DontDestroyOnLoad singleton. Loads language JSONs from `Resources/Localization/`, maintains both instance and static dictionaries. Fires `OnLanguageChanged` event on language switch.
- **`LocalizedText`** (`Assets/Scripts/Localization/LocalizedText.cs`) — Component attached to every `TMP_Text` GameObject. Has a `localizationKey` field. Subscribes to `OnLanguageChanged` and calls `UpdateText()` to set `TMP_Text.text` from the key.
- **`LocalizedTextAutoSetup`** (`Assets/Editor/LocalizedTextAutoSetup.cs`) — Editor-only. Auto-adds `LocalizedText` when you add a `TMP_Text` in the editor, auto-generates keys from hierarchy, and syncs text changes to `en.json` in real time.

### How to Use `L()` for Dynamic/Code-Driven Text

For text set in **C# code** (alerts, toasts, dynamic labels), use the static method:

```csharp
LocalizationManager.L("alert.my_key.title")
LocalizationManager.L("alert.my_key.info", "Fallback text if key missing")
```

`L()` is fully static — it reads from static dictionaries and does **not** depend on `LocalizationManager.Instance`. This is intentional because `Instance` can be null/stale in builds due to MonoBehaviour lifecycle timing. **Always use `L()`, never `Instance.GetTranslation()`** for code-driven text.

For text with format parameters, use standard C# string formatting:

```csharp
string msg = string.Format(LocalizationManager.L("alert.connection_failed.info"), ipAddress);
// The JSON value uses {0}, {1}, etc.: "Could not connect to '{0}'."
```

### How to Add a New Localized String

1. **Choose a key** following the naming convention (see below).
2. **Add the key + English text** to `Assets/Resources/Localization/en.json` (keep keys sorted alphabetically).
3. **Add the translated text** to every other language file (`pt-BR.json`, etc.). If you don't have a translation yet, still add the key with the English text as a placeholder.
4. **Use the key in code or in the editor:**
   - **In code:** `LocalizationManager.L("your.key.here")`
   - **In scene:** The `LocalizedText` component's `localizationKey` field should be set to the key. If you created the TMP_Text in the editor, the auto-setup tool handles this.

### Key Naming Convention

Keys use dot-separated lowercase hierarchy paths:

```
{root}.{parent}.{child}.{descriptor}
```

Examples:
- `alert.connection_failed.title` — alert dialog title
- `alert.connection_failed.info` — alert dialog body
- `alert.close` — shared "Close" button text
- `canvas.settings.profiles.add_profile.label` — UI element from scene hierarchy
- `canvas.onboarding.step1.info.text` — onboarding flow text

Rules:
- All lowercase
- Dots `.` separate hierarchy levels
- Underscores `_` replace spaces and special characters
- Strip `(TMP)` and `(Clone)` suffixes
- For alerts/toasts/popups, prefix with `alert.`
- For scene UI elements, the auto-setup tool generates keys from the GameObject hierarchy path

### When You Add or Modify UI Text — Checklist

- [ ] Is the English text in `en.json`?
- [ ] Is the translation in `pt-BR.json` (and any other language files)?
- [ ] Is the key used via `LocalizationManager.L("key")` in code, or via `LocalizedText.localizationKey` in the scene?
- [ ] If it's a new alert/dialog, are both `.title` and `.info` keys added?
- [ ] Keys are sorted alphabetically in the JSON files?

### Language Switching

Language is switched via `LocalizationManager.SwitchLanguageByIndex(int index)` (fully static, no instance needed). The setting is persisted in `PlayerPrefs` under `"yasg_language"`. The `SettingsManager` calls this when the Language dropdown changes.

### Adding a New Language

1. Duplicate `en.json` and rename to the language code (e.g. `es.json`, `ja.json`, `fr.json`).
2. Change `"_language_name"` to the display name in that language (e.g. `"Espanol"`, `"日本語"`).
3. Translate all values. Keys stay identical across all files.
4. The system auto-discovers new files from `Resources/Localization/` — no code changes needed.

### Common Pitfalls

- **Never use `Instance.GetTranslation()`** — always use `LocalizationManager.L()`. The instance can be null in builds.
- **Never hardcode user-facing strings** in C# code. Always add a key to the JSON files and use `L()`.
- **Never forget pt-BR.json** — when adding keys to `en.json`, always add them to `pt-BR.json` too.
- **Format placeholders** use `{0}`, `{1}`, etc. in the JSON value, then wrap with `string.Format()` in code.
- **Rich text** is supported — use `<b>`, `<i>`, `<color>`, `\n` etc. directly in JSON values.

## Project Structure (Key Paths)

```
Assets/
  Scripts/
    Localization/
      LocalizationManager.cs   ← Singleton, L(), language loading
      LocalizedText.cs          ← Per-TMP_Text component
    SettingsManager.cs          ← Game settings, calls SwitchLanguageByIndex
    SettingsUI.cs               ← Settings UI rendering, onboarding logic
  Editor/
    LocalizedTextAutoSetup.cs   ← Auto-setup LocalizedText in editor
    LocalizationToolsEditor.cs  ← Tools > Localization > Setup Scene
  Resources/
    Localization/
      en.json                   ← English (source of truth)
      pt-BR.json                ← Portuguese (Brazil)
  SetupManager.cs               ← First-time setup flow
  SettingsUIInstantiator.cs     ← Instantiates settings UI, rebuilds on language change
```

## Platform Notes

- **Android:** Uses `Application.platform == RuntimePlatform.Android`. Mic feedback is off by default on mobile. yt-dlp uses `AndroidJavaProxy` for progress callbacks.
- **Frame rate:** Background render texture updates use adaptive frame skipping based on `Application.targetFrameRate` to handle varying refresh rates (120Hz, 60Hz power saving, etc.).
- **Settings persistence:** All settings stored via `PlayerPrefs`. Language stored as `"yasg_language"` (language code string like `"en"`, `"pt-BR"`).
