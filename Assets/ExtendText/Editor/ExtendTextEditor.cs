using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ExtendText), true)]
[CanEditMultipleObjects]
public class ExtendTexttEditor : UnityEditor.UI.TextEditor
{
    SerializedProperty emojProp;
    SerializedProperty m_Text;
    SerializedProperty m_FontData;
    SerializedProperty linkColor;
    SerializedProperty linkUnderlineColor;
    SerializedProperty onHrefClick;

    protected override void OnEnable()
    {
        base.OnEnable();

        emojProp = serializedObject.FindProperty("emojDatabase");
        m_Text = serializedObject.FindProperty("m_Text");
        m_FontData = serializedObject.FindProperty("m_FontData");
        linkColor = serializedObject.FindProperty("linkColor");
        linkUnderlineColor = serializedObject.FindProperty("linkUnderlineColor");
        onHrefClick = serializedObject.FindProperty("onHrefClick");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(emojProp);
        EditorGUILayout.PropertyField(m_Text);
        EditorGUILayout.PropertyField(m_FontData);
        AppearanceControlsGUI();
        EditorGUILayout.PropertyField(linkColor);
        EditorGUILayout.PropertyField(linkUnderlineColor);
        RaycastControlsGUI();
        EditorGUILayout.PropertyField(onHrefClick);
        serializedObject.ApplyModifiedProperties();
    }
}