using UnityEditor;
using UnityEngine;

namespace Mob404.Common.UITemplate.Editor
{
    [CustomEditor(typeof(GGButton))]
    [CanEditMultipleObjects]
    public class GGButtonInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var config = MobUIToolConfig.Load();
            var marker = (GGButton)target;
            serializedObject.Update();

            if (config == null)
            {
                EditorGUILayout.HelpBox("Chưa có MobUIToolConfig. Mở Tools/Mob404/MobUITool để tạo.", MessageType.Info);
            }
            else
            {
                var names = new string[config.types.Count];
                int current = -1;
                for (int i = 0; i < config.types.Count; i++)
                {
                    names[i] = config.types[i].name;
                    if (config.types[i].id == marker.typeId) current = i;
                }

                if (current < 0)
                    EditorGUILayout.HelpBox($"Loại '{marker.typeId}' không có trong config.", MessageType.Warning);

                EditorGUI.showMixedValue = serializedObject.FindProperty("typeId").hasMultipleDifferentValues;
                int chosen = EditorGUILayout.Popup("Loại", current, names);
                EditorGUI.showMixedValue = false;
                if (chosen != current && chosen >= 0)
                {
                    var type = config.types[chosen];
                    foreach (var t in targets)
                    {
                        var m = (GGButton)t;
                        Undo.RecordObject(m, "Đổi loại GGButton");
                        m.typeId = type.id;
                        EditorUtility.SetDirty(m);
                    }
                    serializedObject.Update();
                }
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("image"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("text"));
            serializedObject.ApplyModifiedProperties();

            var selectedType = config != null ? config.FindType(marker.typeId) : null;
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(selectedType == null))
            {
                if (GUILayout.Button(new GUIContent("Apply only", "Áp style của loại lên đối tượng đang chọn (không lưu file, Undo được)")))
                {
                    // Ghi qua Undo (kể cả thêm/xoá Outline/Shadow) và gộp thành một bước Ctrl+Z.
                    Undo.SetCurrentGroupName("MobUITool Apply only");
                    int undoGroup = Undo.GetCurrentGroup();
                    MobUIToolApplier.UseUndo = true;
                    MobUIToolApplier.Warnings.Clear();
                    foreach (var t in targets)
                    {
                        var m = (GGButton)t;
                        var type = config.FindType(m.typeId);
                        if (type == null) continue;
                        MobUIToolApplier.ApplyToButton(m, type);
                    }
                    MobUIToolApplier.CleanupTemp();
                    MobUIToolApplier.UseUndo = false;
                    Undo.CollapseUndoOperations(undoGroup);
                    foreach (var w in MobUIToolApplier.Warnings) Debug.LogWarning("[MobUITool] " + w);
                    MobUIToolApplier.Warnings.Clear();
                }
            }
            if (GUILayout.Button(new GUIContent("Open UITool", "Mở cửa sổ MobUITool, chọn sẵn loại này")))
                MobUIToolWindow.Open(marker.typeId);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Button đổi loại hoặc thêm tay sẽ vào danh sách của loại sau khi bấm \"Refresh\" trong cửa sổ.",
                MessageType.None);
        }
    }
}
