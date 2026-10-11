using System.Collections.Generic;
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
            EditorGUILayout.PropertyField(serializedObject.FindProperty("costIcon"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("costText"));
            serializedObject.ApplyModifiedProperties();

            var selectedType = config != null ? config.FindType(marker.typeId) : null;
            if (selectedType != null && targets.Length == 1) _DrawChanges(config, marker, selectedType);
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

        private bool _showChanges = true;

        /// <summary>
        /// Tự so button với preset của loại. Khác → liệt kê + lưu ngược giá trị của button vào preset (ô trống thì tạo,
        /// preset dùng chung với ô/loại khác thì tách file), hoặc lưu rồi áp cho mọi button của loại.
        /// </summary>
        private void _DrawChanges(MobUIToolConfig config, GGButton marker, GGButtonType type)
        {
            var slots = MobUIToolTabCapture.BuildButton(marker, type);
            int total = MobUIToolTabCapture.CountDiffs(slots);
            EditorGUILayout.Space();
            if (total == 0)
            {
                EditorGUILayout.HelpBox($"Button khớp preset của loại '{type.name}'.", MessageType.None);
                return;
            }

            EditorGUILayout.HelpBox($"Button đang khác preset của loại '{type.name}' ở {total} chỗ. Apply loại lúc này sẽ GHI ĐÈ button về preset — " +
                                    "bấm \"Save to preset\" nếu muốn giữ chỉnh sửa này làm chuẩn.", MessageType.Warning);
            _showChanges = EditorGUILayout.Foldout(_showChanges, "Khác biệt (button hiện tại → preset)", true);
            if (_showChanges)
            {
                EditorGUI.indentLevel++;
                foreach (var s in slots)
                {
                    if (s.diffs.Count == 0) continue;
                    EditorGUILayout.LabelField(s.label + (s.preset != null ? $"  ({s.preset.name})" : ""), EditorStyles.miniBoldLabel);
                    EditorGUI.indentLevel++;
                    foreach (var d in s.diffs) EditorGUILayout.LabelField(d, EditorStyles.wordWrappedMiniLabel);
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button(new GUIContent("Save to preset", "Ghi giá trị của button này vào preset của loại (preset dùng chung với ô khác sẽ được tách file mới). Chưa áp cho button khác.")))
                    _Log(MobUIToolTabCapture.Save(config, slots));
                if (GUILayout.Button(new GUIContent($"Save + Apply all '{type.name}'", "Lưu vào preset rồi áp loại cho mọi button liên kết (ghi file, không Undo)")) &&
                    EditorUtility.DisplayDialog("MobUITool — Save + Apply",
                        $"Lưu chỉnh sửa của '{marker.name}' vào preset rồi áp loại '{type.name}' cho mọi button liên kết?\n\nKhông Undo được — nên commit git trước.",
                        "Save + Apply", "Cancel"))
                {
                    _Log(MobUIToolTabCapture.Save(config, slots));
                    if (MobUIToolAssetUtil.SaveOpenEditorsIfUserWantsTo())
                    {
                        var report = MobUIToolBatchApply.Apply(type, false);
                        Debug.Log("[MobUITool] " + report.Headline);
                        foreach (var e in report.entries)
                            foreach (var w in e.warnings) Debug.LogWarning($"[MobUITool] {e.path}: {w}");
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void _Log(List<string> lines)
        {
            foreach (var l in lines) Debug.Log("[MobUITool] " + l);
        }
    }
}
