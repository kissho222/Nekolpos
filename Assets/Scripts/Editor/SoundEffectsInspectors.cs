using System;
using System.Collections.Generic;
using System.Reflection;
using Nekolpos.Audio;
using UnityEditor;
using UnityEngine;

/// <summary>音声設定の既存ラベル属性を、通常フィールドにも適用するInspector描画。</summary>
public static class SoundEffectsInspectorGUI
{
    public static GUIContent GetLabel(SerializedProperty property)
    {
        if (property == null) throw new ArgumentNullException(nameof(property));
        FieldInfo field = ResolveField(property);
        string tooltip = field?.GetCustomAttribute<TooltipAttribute>()?.tooltip ?? property.tooltip;
        if (property.propertyType == SerializedPropertyType.ArraySize)
            return new GUIContent("候補数", tooltip);
        if (TryGetElementIndex(property.propertyPath, out int index))
            return new GUIContent($"候補 {index + 1}", tooltip);
        string label = field?.GetCustomAttribute<InspectorNameAttribute>()?.displayName;
        return new GUIContent(string.IsNullOrEmpty(label) ? property.displayName : label, tooltip);
    }

    public static void Draw(SerializedObject serializedObject)
    {
        if (serializedObject == null) throw new ArgumentNullException(nameof(serializedObject));
        serializedObject.Update();
        SerializedProperty property = serializedObject.GetIterator();
        bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (property.propertyPath == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.PropertyField(property, true);
            }
            else
            {
                DrawProperty(property);
            }
        }
        serializedObject.ApplyModifiedProperties();
    }

    private static void DrawProperty(SerializedProperty property)
    {
        if (property.isArray && property.propertyType != SerializedPropertyType.String)
        {
            DrawArray(property);
            return;
        }
        if (property.propertyType == SerializedPropertyType.Generic && property.hasVisibleChildren)
        {
            if (!DrawFoldout(property)) return;
            using (new EditorGUI.IndentLevelScope())
            {
                SerializedProperty child = property.Copy();
                SerializedProperty end = property.GetEndProperty();
                if (!child.NextVisible(true)) return;
                do
                {
                    if (SerializedProperty.EqualContents(child, end)) break;
                    DrawProperty(child);
                } while (child.NextVisible(false));
            }
            return;
        }
        // PropertyField keeps Unity's Min/Range drawers, enum labels, mixed values and Undo.
        // Its decorators already draw Header; only custom container foldouts draw it below.
        EditorGUILayout.PropertyField(property, GetLabel(property), true);
    }

    private static bool DrawFoldout(SerializedProperty property)
    {
        FieldInfo field = ResolveField(property);
        if (field != null)
        {
            foreach (HeaderAttribute header in field.GetCustomAttributes<HeaderAttribute>())
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(header.header, EditorStyles.boldLabel);
            }
        }
        Rect rect = EditorGUILayout.GetControlRect();
        GUIContent label = EditorGUI.BeginProperty(rect, GetLabel(property), property);
        try
        {
            property.isExpanded = EditorGUI.Foldout(rect, property.isExpanded, label, true);
        }
        finally
        {
            EditorGUI.EndProperty();
        }
        return property.isExpanded;
    }

    private static void DrawArray(SerializedProperty property)
    {
        if (!DrawFoldout(property)) return;
        using (new EditorGUI.IndentLevelScope())
        {
            SerializedProperty size = property.FindPropertyRelative("Array.size");
            // Resizing one multi-target array can copy the first target's contents to others.
            // Keep size editing to a single target; shared elements retain normal mixed editing.
            using (new EditorGUI.DisabledScope(property.serializedObject.isEditingMultipleObjects))
            {
                EditorGUILayout.PropertyField(size, GetLabel(size));
            }
            for (int index = 0; index < property.arraySize; index++)
                DrawProperty(property.GetArrayElementAtIndex(index));
        }
    }

    private static FieldInfo ResolveField(SerializedProperty property)
    {
        UnityEngine.Object target = property.serializedObject.targetObject;
        if (target == null) return null;
        Type type = target.GetType();
        FieldInfo field = null;
        string[] parts = property.propertyPath.Split('.');
        for (int index = 0; index < parts.Length; index++)
        {
            if (parts[index] == "Array" && index + 1 < parts.Length)
            {
                string next = parts[++index];
                if (next == "size") return field;
                if (!next.StartsWith("data[", StringComparison.Ordinal)) return null;
                type = GetElementType(type);
                if (type == null) return null;
                continue;
            }
            field = FindField(type, parts[index]);
            if (field == null) return null;
            type = field.FieldType;
        }
        return field;
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        return null;
    }

    private static Type GetElementType(Type type)
    {
        if (type.IsArray) return type.GetElementType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            return type.GetGenericArguments()[0];
        return null;
    }

    private static bool TryGetElementIndex(string path, out int index)
    {
        const string marker = ".Array.data[";
        int start = path.LastIndexOf(marker, StringComparison.Ordinal);
        index = -1;
        return start >= 0 && path.EndsWith("]", StringComparison.Ordinal) &&
               int.TryParse(path.Substring(start + marker.Length, path.Length - start - marker.Length - 1), out index);
    }
}

[CustomEditor(typeof(CharacterMovementAudioController)), CanEditMultipleObjects]
public sealed class CharacterMovementAudioControllerEditor : Editor
{
    public override void OnInspectorGUI() => SoundEffectsInspectorGUI.Draw(serializedObject);
}

[CustomEditor(typeof(FootstepSurfaceProfile)), CanEditMultipleObjects]
public sealed class FootstepSurfaceProfileEditor : Editor
{
    public override void OnInspectorGUI() => SoundEffectsInspectorGUI.Draw(serializedObject);
}

[CustomEditor(typeof(FootstepSurface)), CanEditMultipleObjects]
public sealed class FootstepSurfaceEditor : Editor
{
    public override void OnInspectorGUI() => SoundEffectsInspectorGUI.Draw(serializedObject);
}
