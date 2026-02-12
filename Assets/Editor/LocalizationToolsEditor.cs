using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

public class LocalizationToolsEditor
{
    [MenuItem("Tools/Localization/Setup Scene (Add LocalizedText to all TMP_Text)")]
    public static void SetupScene()
    {
        // Collect all TMP_Text in the active scene (including inactive GameObjects)
        var allTMPTexts = new List<TMP_Text>();
        var scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            allTMPTexts.AddRange(root.GetComponentsInChildren<TMP_Text>(true));
        }

        // Load existing en.json if it exists
        string enJsonPath = Path.Combine(Application.dataPath, "Resources", "Localization", "en.json");
        var existingTranslations = new Dictionary<string, string>();
        if (File.Exists(enJsonPath))
        {
            string json = File.ReadAllText(enJsonPath);
            existingTranslations = JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                                   ?? new Dictionary<string, string>();
        }

        // Track all keys we've assigned this run to detect collisions
        var assignedKeys = new HashSet<string>(existingTranslations.Keys);

        int added = 0;
        int skipped = 0;

        foreach (var tmpText in allTMPTexts)
        {
            // Skip empty/whitespace-only text — likely dynamic
            if (string.IsNullOrWhiteSpace(tmpText.text))
                continue;

            var existing = tmpText.GetComponent<LocalizedText>();
            if (existing != null)
            {
                // Already has LocalizedText — ensure its key is in en.json
                if (!string.IsNullOrEmpty(existing.localizationKey)
                    && !existingTranslations.ContainsKey(existing.localizationKey))
                {
                    existingTranslations[existing.localizationKey] = tmpText.text;
                }
                skipped++;
                continue;
            }

            // Generate key from hierarchy path
            string key = GenerateKeyFromHierarchy(tmpText.transform);

            // Ensure uniqueness
            string baseKey = key;
            int suffix = 1;
            while (assignedKeys.Contains(key)
                   && existingTranslations.ContainsKey(key)
                   && existingTranslations[key] != tmpText.text)
            {
                key = baseKey + "." + suffix;
                suffix++;
            }
            assignedKeys.Add(key);

            // Add LocalizedText component (supports Undo)
            var localizedText = Undo.AddComponent<LocalizedText>(tmpText.gameObject);
            localizedText.localizationKey = key;

            // Add to translations
            if (!existingTranslations.ContainsKey(key))
                existingTranslations[key] = tmpText.text;

            added++;
            EditorUtility.SetDirty(tmpText.gameObject);
        }

        // Ensure _language_name exists
        if (!existingTranslations.ContainsKey("_language_name"))
            existingTranslations["_language_name"] = "English";

        // Save en.json (sorted for readability)
        Directory.CreateDirectory(Path.GetDirectoryName(enJsonPath));
        var sorted = new SortedDictionary<string, string>(existingTranslations);
        string output = JsonConvert.SerializeObject(sorted, Formatting.Indented);
        File.WriteAllText(enJsonPath, output);

        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Localization Setup Complete",
            $"Processed {allTMPTexts.Count} TMP_Text components.\n" +
            $"Added LocalizedText: {added}\n" +
            $"Already had LocalizedText: {skipped}\n" +
            $"Skipped (empty text): {allTMPTexts.Count - added - skipped}\n\n" +
            $"English translations saved to:\nAssets/Resources/Localization/en.json",
            "OK");
    }

    private static string GenerateKeyFromHierarchy(Transform t)
    {
        var parts = new List<string>();
        Transform current = t;
        while (current != null)
        {
            parts.Add(CleanName(current.name));
            current = current.parent;
        }
        parts.Reverse();
        return string.Join(".", parts);
    }

    private static string CleanName(string name)
    {
        // Remove common Unity suffixes
        name = name.Replace("(TMP)", "").Replace("(Clone)", "").Trim();
        // Replace non-alphanumeric with underscore
        name = Regex.Replace(name, @"[^a-zA-Z0-9]", "_");
        // Collapse multiple underscores
        name = Regex.Replace(name, @"_+", "_").Trim('_');
        return name.ToLowerInvariant();
    }
}
