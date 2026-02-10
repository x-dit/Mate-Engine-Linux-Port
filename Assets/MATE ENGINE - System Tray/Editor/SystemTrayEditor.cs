using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UI;

[CustomEditor(typeof(SystemTray))]
public class SystemTrayEditor : Editor
{
    private ReorderableList list;

    void OnEnable()
    {
        list = new ReorderableList(serializedObject,
            serializedObject.FindProperty("actions"),
            true, true, true, true);

        list.drawHeaderCallback = rect =>
        {
            EditorGUI.LabelField(rect, "Tray Actions");
        };

        list.elementHeightCallback = index =>
        {
            return EditorGUIUtility.singleLineHeight * 6 + 12;
        };

        list.drawElementCallback = (rect, index, active, focused) =>
        {
            var element = list.serializedProperty.GetArrayElementAtIndex(index);
            var label = element.FindPropertyRelative("label");
            var type = element.FindPropertyRelative("type");
            var handlerObject = element.FindPropertyRelative("handlerObject");
            var toggleField = element.FindPropertyRelative("toggleField");
            var methodName = element.FindPropertyRelative("methodName");
            var className = element.FindPropertyRelative("className");

            float y = rect.y + 2;
            float h = EditorGUIUtility.singleLineHeight;
            float s = 2;

            EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, h), label, new GUIContent("Label"));
            y += h + s;
            EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, h), type, new GUIContent("Type"));
            y += h + s;
            EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, h), handlerObject, new GUIContent("Handler Object"));
            y += h + s;

            if (handlerObject.objectReferenceValue != null)
            {
                var go = handlerObject.objectReferenceValue as GameObject;
                if (go != null)
                {
                    var actionType = (SystemTray.TrayActionType)type.enumValueIndex;

                    if (actionType == SystemTray.TrayActionType.Toggle)
                    {
                        var comps = go.GetComponents<MonoBehaviour>();
                        var fields = new List<string>();
                        var classes = new List<string>();
                        foreach (var comp in comps)
                        {
                            if (comp != null)
                            {
                                var type1 = comp.GetType();
                                foreach (var f in type1.GetFields(BindingFlags.Public | BindingFlags.Instance))
                                {
                                    if (f.FieldType == typeof(Toggle))
                                    {
                                        fields.Add(f.Name);
                                        classes.Add(type1.Name);
                                    }
                                }
                                int currentIdx = fields.IndexOf(toggleField.stringValue);
                                int shownIdx = Mathf.Clamp(currentIdx < 0 ? 0 : currentIdx, 0, Math.Max(fields.Count - 1, 0));
                                int newIdx = EditorGUI.Popup(new Rect(rect.x, y, rect.width, h), "Toggle Field", shownIdx, fields.ToArray());
                                if (fields.Count > 0 && newIdx != currentIdx)
                                {
                                    toggleField.stringValue = fields[newIdx];
                                    className.stringValue = classes[newIdx];
                                }
                            }
                        }
                    }
                    else if (actionType == SystemTray.TrayActionType.Button)
                    {
                        var comps = go.GetComponents<MonoBehaviour>();
                        var classes = new List<string>();
                        var methods = new List<string>();
                        foreach (var comp in comps)
                        {
                            if (comp != null)
                            {
                                var type1 = comp.GetType();
                                foreach (var m in type1.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                                {
                                    if (m.GetParameters().Length == 0 && m.ReturnType == typeof(void))
                                    {
                                        classes.Add(type1.Name);
                                        methods.Add(m.Name);
                                    }
                                }
                                int currentIdx = methods.IndexOf(methodName.stringValue);
                                int shownIdx = Mathf.Clamp(currentIdx < 0 ? 0 : currentIdx, 0, Math.Max(methods.Count - 1, 0));
                                int newIdx = EditorGUI.Popup(new Rect(rect.x, y, rect.width, h), "Method", shownIdx, methods.ToArray());
                                if (methods.Count > 0 && newIdx != currentIdx)
                                {
                                    methodName.stringValue = methods[newIdx];
                                    className.stringValue = classes[newIdx];
                                }
                            }
                        }
                    }
                    else if (actionType == SystemTray.TrayActionType.Method)
                    {
                        var display = new List<string>();
                        var classes = new List<string>();
                        var values = new List<string>();
                        var comps = go.GetComponents<MonoBehaviour>();
                        foreach (var c in comps)
                        {
                            if (!c) continue;
                            var t = c.GetType();
                            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                            {
                                if (m.GetParameters().Length == 0 && m.ReturnType == typeof(void))
                                {
                                    display.Add(t.Name + "." + m.Name);
                                    values.Add(m.Name);
                                    classes.Add(t.Name);
                                }
                            }
                        }
                        int currentIdx = values.IndexOf(methodName.stringValue);
                        int shownIdx = Mathf.Clamp(currentIdx < 0 ? 0 : currentIdx, 0, Math.Max(values.Count - 1, 0));
                        int newIdx = EditorGUI.Popup(new Rect(rect.x, y, rect.width, h), "Method", shownIdx, display.ToArray());
                        if (values.Count > 0 && newIdx != currentIdx)
                        {
                            methodName.stringValue = values[newIdx];
                            className.stringValue = classes[newIdx];
                        }
                    }
                }
            }
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        list.DoLayoutList();
        serializedObject.ApplyModifiedProperties();
    }
}
