using UnityEditor;
using UnityEngine;

namespace Mob404.Common.UITemplate.Editor
{
    [CustomEditor(typeof(GGImage))]
    [CanEditMultipleObjects]
    public class GGImageInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var config = MobUIToolConfig.Load();
            var marker = (GGImage)target;
            serializedObject.Update();

            if (config == null)
            {
                EditorGUILayout.HelpBox("Chưa có MobUIToolConfig. Mở Tools/Mob404/MobUITool để tạo.", MessageType.Info);
            }
            else
            {
                var names = new string[config.imageTypes.Count];
                int current = -1;
                for (int i = 0; i < config.imageTypes.Count; i++)
                {
                    names[i] = config.imageTypes[i].name;
                    if (config.imageTypes[i].id == marker.typeId) current = i;
                }

                if (current < 0)
                    EditorGUILayout.HelpBox($"Loại '{marker.typeId}' không có trong tab Image của config.", MessageType.Warning);

                EditorGUI.showMixedValue = serializedObject.FindProperty("typeId").hasMultipleDifferentValues;
                int chosen = EditorGUILayout.Popup("Loại", current, names);
                EditorGUI.showMixedValue = false;
                if (chosen != current && chosen >= 0)
                {
                    var type = config.imageTypes[chosen];
                    foreach (var t in targets)
                    {
                        var m = (GGImage)t;
                        Undo.RecordObject(m, "Đổi loại GGImage");
                        m.typeId = type.id;
                        EditorUtility.SetDirty(m);
                    }
                    serializedObject.Update();
                }
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("image"));
            serializedObject.ApplyModifiedProperties();

            var selectedType = config != null ? config.FindImageType(marker.typeId) : null;
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
                        var m = (GGImage)t;
                        var type = config.FindImageType(m.typeId);
                        if (type == null) continue;
                        MobUIToolApplier.ApplyToImage(m, type);
                    }
                    MobUIToolApplier.CleanupTemp();
                    MobUIToolApplier.UseUndo = false;
                    Undo.CollapseUndoOperations(undoGroup);
                    foreach (var w in MobUIToolApplier.Warnings) Debug.LogWarning("[MobUITool] " + w);
                    MobUIToolApplier.Warnings.Clear();
                }
            }
            if (GUILayout.Button(new GUIContent("Open UITool", "Mở cửa sổ MobUITool, chọn sẵn loại này")))
                MobUIToolWindow.Open(marker.typeId, true);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Image đổi loại hoặc thêm tay sẽ vào danh sách của loại sau khi bấm \"Refresh\" trong cửa sổ.",
                MessageType.None);
        }
    }
}
