using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LayoutSwitcher))]
public class LayoutSwitcherEditor : Editor
{
    SerializedProperty entriesProp;
    bool showEntries = true;

    void OnEnable()
    {
        entriesProp = serializedObject.FindProperty("entries");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var switcher = (LayoutSwitcher)target;

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Layout Switcher", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "1) Add UI elements (drag or use 'Add All Children')\n" +
            "2) Arrange your landscape layout, click 'Capture Landscape'\n" +
            "3) Rearrange for portrait, click 'Capture Portrait'\n" +
            "4) Use 'Apply' buttons to preview each layout",
            MessageType.Info);

        EditorGUILayout.Space(6);

        // --- Action buttons ---
        EditorGUILayout.LabelField("Capture", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GreenButton("Capture as LANDSCAPE"))
        {
            Undo.RecordObject(switcher, "Capture Landscape Layout");
            switcher.CaptureCurrentAs(false);
            EditorUtility.SetDirty(switcher);
        }
        if (GreenButton("Capture as PORTRAIT"))
        {
            Undo.RecordObject(switcher, "Capture Portrait Layout");
            switcher.CaptureCurrentAs(true);
            EditorUtility.SetDirty(switcher);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (BlueButton("Apply Landscape"))
        {
            Undo.RecordObject(switcher, "Apply Landscape Preview");
            RecordAllTargets(switcher);
            switcher.ApplyLayout(false);
        }
        if (BlueButton("Apply Portrait"))
        {
            Undo.RecordObject(switcher, "Apply Portrait Preview");
            RecordAllTargets(switcher);
            switcher.ApplyLayout(true);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);

        // --- Entries management ---
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add All Children", GUILayout.Height(24)))
        {
            Undo.RecordObject(switcher, "Add All Children");
            switcher.AddAllChildRectTransforms();
            EditorUtility.SetDirty(switcher);
        }
        if (GUILayout.Button("Clear All", GUILayout.Height(24)))
        {
            if (EditorUtility.DisplayDialog("Clear All Entries",
                "Remove all entries from the layout switcher?", "Yes", "Cancel"))
            {
                Undo.RecordObject(switcher, "Clear All Entries");
                switcher.entries.Clear();
                EditorUtility.SetDirty(switcher);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        // --- Entry list ---
        showEntries = EditorGUILayout.Foldout(showEntries,
            $"Tracked Elements ({entriesProp.arraySize})", true);

        if (showEntries)
        {
            EditorGUI.indentLevel++;
            for (int i = 0; i < entriesProp.arraySize; i++)
            {
                var entryProp = entriesProp.GetArrayElementAtIndex(i);
                var targetProp = entryProp.FindPropertyRelative("target");

                EditorGUILayout.BeginHorizontal();

                string label = targetProp.objectReferenceValue != null
                    ? targetProp.objectReferenceValue.name
                    : "(empty)";

                EditorGUILayout.PropertyField(targetProp, new GUIContent(label));

                if (GUILayout.Button("X", GUILayout.Width(22), GUILayout.Height(18)))
                {
                    entriesProp.DeleteArrayElementAtIndex(i);
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(2);
            if (GUILayout.Button("+ Add Entry"))
            {
                entriesProp.InsertArrayElementAtIndex(entriesProp.arraySize);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    void RecordAllTargets(LayoutSwitcher switcher)
    {
        foreach (var entry in switcher.entries)
        {
            if (entry.target != null)
                Undo.RecordObject(entry.target, "Layout Preview");
        }
    }

    bool GreenButton(string text)
    {
        var prev = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
        bool clicked = GUILayout.Button(text, GUILayout.Height(28));
        GUI.backgroundColor = prev;
        return clicked;
    }

    bool BlueButton(string text)
    {
        var prev = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.4f, 0.6f, 1f);
        bool clicked = GUILayout.Button(text, GUILayout.Height(28));
        GUI.backgroundColor = prev;
        return clicked;
    }
}
