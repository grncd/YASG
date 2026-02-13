using UnityEngine;
using UnityEditor;
using TMPro;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

/// <summary>
/// Auto-adds LocalizedText to new TMP_Text components, auto-generates localization keys
/// from the hierarchy path, and syncs text content to en.json in real time as you edit.
/// </summary>
[InitializeOnLoad]
public static class LocalizedTextAutoSetup
{
    private static readonly Dictionary<int, string> _trackedTexts = new Dictionary<int, string>();
    private static readonly string _enJsonPath = Path.Combine(Application.dataPath, "Resources", "Localization", "en.json");

    static LocalizedTextAutoSetup()
    {
        ObjectFactory.componentWasAdded += OnComponentAdded;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        EditorApplication.update += OnEditorUpdate;
    }

    // ──────────────────────────────────────────────
    // 1. Auto-add LocalizedText when TMP_Text is added via Add Component
    // ──────────────────────────────────────────────
    private static void OnComponentAdded(Component component)
    {
        if (Application.isPlaying) return;
        if (!(component is TMP_Text)) return;

        var go = component.gameObject;
        if (go.GetComponent<LocalizedText>() != null) return;

        EditorApplication.delayCall += () =>
        {
            if (go == null || go.GetComponent<LocalizedText>() != null) return;

            var lt = Undo.AddComponent<LocalizedText>(go);
            lt.localizationKey = GenerateKey(go);
            EditorUtility.SetDirty(lt);

            var tmpText = go.GetComponent<TMP_Text>();
            if (tmpText != null && !string.IsNullOrWhiteSpace(tmpText.text))
                SyncKeyToEnJson(lt.localizationKey, tmpText.text);
        };
    }

    // ──────────────────────────────────────────────
    // 2. Catch objects created via menus (UI > Text - TextMeshPro)
    // ──────────────────────────────────────────────
    private static void OnHierarchyChanged()
    {
        if (Application.isPlaying) return;

        var go = Selection.activeGameObject;
        if (go == null) return;

        var tmpText = go.GetComponent<TMP_Text>();
        if (tmpText == null || go.GetComponent<LocalizedText>() != null) return;

        EditorApplication.delayCall += () =>
        {
            if (go == null || go.GetComponent<LocalizedText>() != null) return;

            var lt = Undo.AddComponent<LocalizedText>(go);
            lt.localizationKey = GenerateKey(go);
            EditorUtility.SetDirty(lt);

            var tmp = go.GetComponent<TMP_Text>();
            if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text))
                SyncKeyToEnJson(lt.localizationKey, tmp.text);
        };
    }

    // ──────────────────────────────────────────────
    // 3. Real-time text sync: poll the selected object for text changes
    // ──────────────────────────────────────────────
    private static void OnEditorUpdate()
    {
        if (Application.isPlaying) return;

        var go = Selection.activeGameObject;
        if (go == null) return;

        var lt = go.GetComponent<LocalizedText>();
        var tmpText = go.GetComponent<TMP_Text>();
        if (lt == null || tmpText == null || string.IsNullOrEmpty(lt.localizationKey)) return;

        int id = go.GetInstanceID();
        string currentText = tmpText.text;

        if (_trackedTexts.TryGetValue(id, out string lastText))
        {
            if (lastText != currentText)
            {
                _trackedTexts[id] = currentText;
                SyncKeyToEnJson(lt.localizationKey, currentText);
            }
        }
        else
        {
            _trackedTexts[id] = currentText;
        }
    }

    // ──────────────────────────────────────────────
    // Key generation (matches existing LocalizationToolsEditor convention)
    // ──────────────────────────────────────────────
    public static string GenerateKey(GameObject go)
    {
        var parts = new List<string>();
        Transform current = go.transform;
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
        name = name.Replace("(TMP)", "").Replace("(Clone)", "").Trim();
        name = Regex.Replace(name, @"[^a-zA-Z0-9]", "_");
        name = Regex.Replace(name, @"_+", "_").Trim('_');
        return name.ToLowerInvariant();
    }

    // ──────────────────────────────────────────────
    // en.json sync
    // ──────────────────────────────────────────────
    public static void SyncKeyToEnJson(string key, string text)
    {
        if (string.IsNullOrEmpty(key) || !File.Exists(_enJsonPath)) return;

        try
        {
            string json = File.ReadAllText(_enJsonPath);
            var jObj = JObject.Parse(json);

            string existing = jObj[key]?.ToString();
            if (existing == text) return;

            jObj[key] = text;
            File.WriteAllText(_enJsonPath, jObj.ToString(Newtonsoft.Json.Formatting.Indented));
            Debug.Log($"[Localization] Synced '{key}' to en.json");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Localization] Failed to sync to en.json: {e.Message}");
        }
    }
}

/// <summary>
/// Custom inspector for LocalizedText — shows key management and sync controls.
/// </summary>
[CustomEditor(typeof(LocalizedText))]
public class LocalizedTextEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var lt = (LocalizedText)target;

        // Key field
        EditorGUI.BeginChangeCheck();
        string newKey = EditorGUILayout.TextField("Localization Key", lt.localizationKey);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(lt, "Change Localization Key");
            lt.localizationKey = newKey;
            EditorUtility.SetDirty(lt);
        }

        EditorGUILayout.Space(4);

        if (GUILayout.Button("Regenerate Key from Hierarchy"))
        {
            Undo.RecordObject(lt, "Regenerate Localization Key");
            lt.localizationKey = LocalizedTextAutoSetup.GenerateKey(lt.gameObject);
            EditorUtility.SetDirty(lt);
        }

        var tmpText = lt.GetComponent<TMP_Text>();
        if (tmpText != null && !string.IsNullOrEmpty(lt.localizationKey))
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("Text changes sync to en.json automatically while this object is selected.", MessageType.Info);

            if (GUILayout.Button("Force Sync to en.json"))
            {
                LocalizedTextAutoSetup.SyncKeyToEnJson(lt.localizationKey, tmpText.text);
            }
        }
    }
}
