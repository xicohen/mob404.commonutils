using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Mob404.Common.UITemplate.Editor
{
    [CustomEditor(typeof(GGBlock))]
    [CanEditMultipleObjects]
    public class GGBlockInspector : UnityEditor.Editor
    {
        private bool _showChanges = true;

        public override void OnInspectorGUI()
        {
            var config = MobUIToolConfig.Load();
            var marker = (GGBlock)target;
            serializedObject.Update();

            if (config == null)
            {
                EditorGUILayout.HelpBox("Chưa có MobUIToolConfig. Mở Tools/Mob404/MobUITool để tạo.", MessageType.Info);
            }
            else
            {
                var names = new string[config.blockTypes.Count];
                int current = -1;
                for (int i = 0; i < config.blockTypes.Count; i++)
                {
                    names[i] = config.blockTypes[i].name;
                    if (config.blockTypes[i].id == marker.typeId) current = i;
                }

                if (current < 0)
                    EditorGUILayout.HelpBox($"Loại '{marker.typeId}' không có trong tab Block của config.", MessageType.Warning);

                EditorGUI.showMixedValue = serializedObject.FindProperty("typeId").hasMultipleDifferentValues;
                int chosen = EditorGUILayout.Popup("Loại", current, names);
                EditorGUI.showMixedValue = false;
                if (chosen != current && chosen >= 0)
                {
                    var type = config.blockTypes[chosen];
                    foreach (var t in targets)
                    {
                        var m = (GGBlock)t;
                        Undo.RecordObject(m, "Đổi loại GGBlock");
                        m.typeId = type.id;
                        EditorUtility.SetDirty(m);
                    }
                    serializedObject.Update();
                }
            }
            serializedObject.ApplyModifiedProperties();

            var selectedType = config != null ? config.FindBlockType(marker.typeId) : null;
            if (selectedType != null && targets.Length == 1) _DrawChanges(config, marker, selectedType);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(selectedType == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button(new GUIContent("Apply only", "Sync structure + áp style của loại lên khối đang chọn (không lưu file, Ctrl+Z một lần hoàn tác)")))
                {
                    Undo.SetCurrentGroupName("MobUITool Apply only (Block)");
                    int undoGroup = Undo.GetCurrentGroup();
                    MobUIToolApplier.UseUndo = true;
                    MobUIToolApplier.Warnings.Clear();
                    foreach (var t in targets)
                    {
                        var m = (GGBlock)t;
                        var type = config.FindBlockType(m.typeId);
                        if (type == null) continue;
                        MobUIToolBlockApplier.ApplyToBlock(m, type);
                    }
                    MobUIToolApplier.CleanupTemp();
                    MobUIToolApplier.UseUndo = false;
                    Undo.CollapseUndoOperations(undoGroup);
                    foreach (var w in MobUIToolApplier.Warnings) Debug.LogWarning("[MobUITool] " + w);
                    MobUIToolApplier.Warnings.Clear();
                }
            }
            if (GUILayout.Button(new GUIContent("Open UITool", "Mở cửa sổ MobUITool ở tab Block, chọn sẵn loại này")))
                MobUIToolWindow.OpenBlock(marker.typeId);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Khối đổi loại hoặc gắn tay sẽ vào danh sách của loại sau khi lưu scene/prefab và bấm \"Refresh\" trong cửa sổ.",
                MessageType.None);
        }

        /// <summary>Tự so khối với preset của loại. Có khác → liệt kê + nút lưu ngược vào preset, hoặc lưu rồi áp cho mọi khối của loại.</summary>
        private void _DrawChanges(MobUIToolConfig config, GGBlock marker, GGBlockType type)
        {
            var slots = MobUIToolTabCapture.BuildBlock(marker, type);
            int total = MobUIToolTabCapture.CountDiffs(slots);
            EditorGUILayout.Space();
            if (total == 0)
            {
                EditorGUILayout.HelpBox($"Khối khớp preset của loại '{type.name}'.", MessageType.None);
                return;
            }

            EditorGUILayout.HelpBox($"Khối đang khác preset của loại '{type.name}' ở {total} chỗ. Apply loại lúc này sẽ GHI ĐÈ khối về preset — " +
                                    "bấm \"Save to preset\" nếu muốn giữ chỉnh sửa này làm chuẩn.", MessageType.Warning);
            _showChanges = EditorGUILayout.Foldout(_showChanges, "Khác biệt (khối hiện tại → preset)", true);
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
                if (GUILayout.Button(new GUIContent("Save to preset", "Ghi giá trị của khối này vào preset của loại (preset dùng chung với ô khác sẽ được tách file mới). Chưa áp cho khối khác.")))
                    _Log(MobUIToolTabCapture.Save(config, slots));
                if (GUILayout.Button(new GUIContent($"Save + Apply all '{type.name}'", "Lưu vào preset rồi áp loại cho mọi khối liên kết (ghi file, không Undo)")) &&
                    EditorUtility.DisplayDialog("MobUITool — Save + Apply",
                        $"Lưu chỉnh sửa của '{marker.name}' vào preset rồi áp loại '{type.name}' cho mọi khối liên kết?\n\nKhông Undo được — nên commit git trước.",
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
