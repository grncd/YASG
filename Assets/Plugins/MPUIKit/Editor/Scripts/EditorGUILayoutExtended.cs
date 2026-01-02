using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MPUIKIT.Editor
{
	public class EditorGUILayoutExtended : UnityEditor.Editor
	{
		private static readonly Type editorGUIType = typeof(EditorGUI);

		private static readonly Type RecycledTextEditorType =
			Assembly.GetAssembly(editorGUIType).GetType("UnityEditor.EditorGUI+RecycledTextEditor");

		private static readonly Type[] argumentTypes =
		{
			RecycledTextEditorType, typeof(Rect), typeof(Rect), typeof(int), typeof(float), typeof(string),
			typeof(GUIStyle), typeof(bool)
		};

		private static readonly MethodInfo doFloatFieldMethod = editorGUIType.GetMethod("DoFloatField",
			BindingFlags.NonPublic | BindingFlags.Static, null, argumentTypes, null);

		private static readonly FieldInfo fieldInfo =
			editorGUIType.GetField("s_RecycledEditor", BindingFlags.NonPublic | BindingFlags.Static);

		// fieldInfo can be null on some Unity versions; guard the GetValue call to avoid a NullReferenceException
		private static readonly object recycledEditor = fieldInfo != null ? fieldInfo.GetValue(null) : null;
		private static readonly GUIStyle style = EditorStyles.numberField;

		public static float FloatFieldExtended(Rect _position, float _value, Rect _dragHotZone)
		{
			// If reflection failed to find the internal method/field (different Unity versions),
			// fall back to the public EditorGUI.FloatField to avoid runtime exceptions.
			if (doFloatFieldMethod == null || recycledEditor == null)
			{
				return EditorGUI.FloatField(_position, _value);
			}

			int controlId = GUIUtility.GetControlID("EditorTextField".GetHashCode(), FocusType.Keyboard, _position);
			object[] parameters = {recycledEditor, _position, _dragHotZone, controlId, _value, "g7", style, true};
			try
			{
				var result = doFloatFieldMethod.Invoke(null, parameters);
				return result is float f ? f : _value;
			}
			catch
			{
				// If invoking the internal method fails for any reason, fall back to the public API.
				return EditorGUI.FloatField(_position, _value);
			}
		}

//	public static float FloatField(GUIContent _content, float _value, float _inputBoxWidth, params GUILayoutOption[] _options)
//	{
//		Rect totalRect = EditorGUILayout.GetControlRect(_options);
//		float width;
//		if (_inputBoxWidth < 1) width = totalRect.width * Mathf.Clamp(_inputBoxWidth, 0.2f, 0.8f);
//		else width = Mathf.Clamp(_inputBoxWidth, totalRect.width * 0.2f, totalRect.width * 0.8f);
//		Rect labelRect = new Rect(totalRect.x, totalRect.y, totalRect.width - width - 8, totalRect.height);
//		Rect inputRect = new Rect(totalRect.x + totalRect.width - width, totalRect.y, width, totalRect.height);
//		
//		EditorGUI.LabelField(labelRect, _content);
//		return FloatFieldExtended(inputRect, _value, labelRect);
//	}

		public static float FloatField(GUIContent _content, float _value, float _labelwidth,
			params GUILayoutOption[] _options)
		{
			Rect totalRect = EditorGUILayout.GetControlRect(_options);
//		float width;
//		if (_labelwidth < 1) width = totalRect.width * Mathf.Clamp(_labelwidth, 0.2f, 0.8f);
//		else width = Mathf.Clamp(_labelwidth, totalRect.width * 0.2f, totalRect.width * 0.8f);

			Rect labelRect = new Rect(totalRect.x, totalRect.y, _labelwidth, totalRect.height);
			Rect inputRect = new Rect(totalRect.x + _labelwidth, totalRect.y, totalRect.width - _labelwidth,
				totalRect.height);

//		Rect labelRect = new Rect(totalRect.x, totalRect.y, totalRect.width - width - 8, totalRect.height);
//		Rect inputRect = new Rect(totalRect.x + totalRect.width - width, totalRect.y, width, totalRect.height);

			EditorGUI.LabelField(labelRect, _content);
			return FloatFieldExtended(inputRect, _value, labelRect);
		}

	}
}