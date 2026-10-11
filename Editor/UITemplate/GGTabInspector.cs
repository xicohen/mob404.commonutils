using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Mob404.Common.UITemplate.Editor
{
    [CustomEditor(typeof(GGTab))]
    [CanEditMultipleObjects]
    public class GGTabInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var config = MobUIToolConfig.Load();
            var marker = (GGTab)target;
            serializedObject.Update();

            if (config == null)
            {
                EditorGUILayout.HelpBox("Chưa có MobUIToolConfig. Mở Tools/Mob404/MobUITool để tạo.", MessageType.Info);
            }
            else
            {
                var names = new string[config.tabTypes.Count];
                int current = -1;
                for (int i = 0; i < config.tabTypes.Count; i++)
                {
                    names[i] = config.tabTypes[i].name;
                    if (config.tabTypes[i].id == marker.typeId) current = i;
                }

                if (current < 0)
                    EditorGUILayout.HelpBox($"Loại '{marker.typeId}' không có trong config.", MessageType.Warning);

                EditorGUI.showMixedValue = serializedObject.FindProperty("typeId").hasMultipleDifferentValues;
                int chosen = EditorGUILayout.Popup("Loại", current, names);
                EditorGUI.showMixedValue = false;
                if (chosen != current && chosen >= 0)
                {
                    var type = config.tabTypes[chosen];
                    foreach (var t in targets)
                    {
                        var m = (GGTab)t;
                        Undo.RecordObject(m, "Đổi loại GGTab");
                        m.typeId = type.id;
                        EditorUtility.SetDirty(m);
                    }
                    serializedObject.Update();
                }
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("term"), new GUIContent("Term (I2)", "Term I2 Localization cho Title của cả hai nửa ON/OFF"));
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tham chiếu (để trống = tìm theo quy ước tên của loại)", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("onRoot"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("onImage"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("onText"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("offRoot"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("offImage"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("offText"));
            serializedObject.ApplyModifiedProperties();

            var selectedType = config != null ? config.FindTabType(marker.typeId) : null;
            if (selectedType != null && targets.Length == 1) _DrawChanges(config, marker, selectedType);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(selectedType == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button(new GUIContent("Apply only", "Sync structure + áp style của loại lên tab đang chọn (không lưu file, Ctrl+Z một lần hoàn tác)")))
                {
                    Undo.SetCurrentGroupName("MobUITool Apply only (Tab)");
                    int undoGroup = Undo.GetCurrentGroup();
                    MobUIToolApplier.UseUndo = true;
                    MobUIToolApplier.Warnings.Clear();
                    foreach (var t in targets)
                    {
                        var m = (GGTab)t;
                        var type = config.FindTabType(m.typeId);
                        if (type == null) continue;
                        var scene = m.gameObject.scene;
                        var roots = scene.IsValid() ? scene.GetRootGameObjects() : new[] { m.transform.root.gameObject };
                        MobUIToolTabApplier.ApplyToTab(m, type, roots);
                    }
                    MobUIToolApplier.CleanupTemp();
                    MobUIToolApplier.UseUndo = false;
                    Undo.CollapseUndoOperations(undoGroup);
                    foreach (var w in MobUIToolApplier.Warnings) Debug.LogWarning("[MobUITool] " + w);
                    MobUIToolApplier.Warnings.Clear();
                }
            }
            if (GUILayout.Button(new GUIContent("Open UITool", "Mở cửa sổ MobUITool ở tab Tab, chọn sẵn loại này")))
                MobUIToolWindow.OpenTab(marker.typeId);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Tab đổi loại hoặc gắn tay sẽ vào danh sách của loại sau khi lưu scene/prefab và bấm \"Refresh\" trong cửa sổ.",
                MessageType.None);
        }

        private bool _showChanges = true;

        /// <summary>
        /// Tự so tab với preset của loại (kể cả bật/tắt component). Có khác → liệt kê + nút lưu ngược vào preset,
        /// hoặc lưu rồi áp cho mọi tab của loại.
        /// </summary>
        private void _DrawChanges(MobUIToolConfig config, GGTab marker, GGTabType type)
        {
            var slots = MobUIToolTabCapture.Build(marker, type);
            int total = MobUIToolTabCapture.CountDiffs(slots);
            EditorGUILayout.Space();
            if (total == 0)
            {
                EditorGUILayout.HelpBox($"Tab khớp preset của loại '{type.name}'.", MessageType.None);
                return;
            }

            EditorGUILayout.HelpBox($"Tab đang khác preset của loại '{type.name}' ở {total} chỗ. Apply loại lúc này sẽ GHI ĐÈ tab về preset — " +
                                    "bấm \"Save to preset\" nếu muốn giữ chỉnh sửa này làm chuẩn.", MessageType.Warning);
            _showChanges = EditorGUILayout.Foldout(_showChanges, "Khác biệt (tab hiện tại → preset)", true);
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
                if (GUILayout.Button(new GUIContent("Save to preset", "Ghi giá trị của tab này vào preset của loại (preset dùng chung với ô khác sẽ được tách file mới). Chưa áp cho tab khác.")))
                    _Log(MobUIToolTabCapture.Save(config, slots));
                if (GUILayout.Button(new GUIContent($"Save + Apply all '{type.name}'", "Lưu vào preset rồi áp loại cho mọi tab liên kết (ghi file, không Undo)")) &&
                    EditorUtility.DisplayDialog("MobUITool — Save + Apply",
                        $"Lưu chỉnh sửa của '{marker.name}' vào preset rồi áp loại '{type.name}' cho mọi tab liên kết?\n\nKhông Undo được — nên commit git trước.",
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
