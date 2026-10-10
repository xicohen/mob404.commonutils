using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Mob404.Common.UITemplate.Editor
{
    /// <summary>
    /// Scan &amp; Attach: gắn marker lên Image dùng sprite nguồn của một loại — GGButton nếu cùng GameObject có Button và
    /// sprite thuộc một loại Button, không thì GGImage nếu sprite thuộc một loại Image. Không áp style.
    /// Refresh: dựng lại danh sách liên kết bằng cách đọc YAML (không mở file nào).
    /// </summary>
    public static class MobUIToolScanner
    {
        // "typeId: 03e2d098" trên marker, hoặc override typeId của instance lồng nhau.
        private static readonly Regex s_TypeId = new Regex(@"(?:\btypeId: |propertyPath: typeId\s+value: )([0-9a-f]{8})\b");

        /// <summary>Gắn marker cho Image khớp sprite nguồn. Trả về report; report.cancelled nếu người dùng huỷ.</summary>
        public static MobUIToolReport Scan(MobUIToolConfig config)
        {
            var report = new MobUIToolReport("Scan & Attach");
            var buttonMap = new Dictionary<Sprite, GGButtonType>();
            var imageMap = new Dictionary<Sprite, GGImageType>();
            var guids = new HashSet<string>();
            foreach (var type in config.types) _AddType(type, buttonMap, guids, report);
            foreach (var type in config.imageTypes) _AddType(type, imageMap, guids, report);

            EditorUtility.DisplayProgressBar(report.title, "Lọc prefab/scene…", 0f);
            var files = MobUIToolAssetUtil.SortByDependency(
                MobUIToolAssetUtil.FilterContaining(MobUIToolAssetUtil.AllPrefabsAndScenes(), guids));

            int attachedButtons = 0, attachedImages = 0;
            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    string path = files[i];
                    if (EditorUtility.DisplayCancelableProgressBar(report.title, path, (float)i / files.Count))
                    {
                        report.cancelled = true;
                        break;
                    }

                    var entry = report.Add(path);
                    try
                    {
                        entry.result = MobUIToolAssetUtil.Process(path, roots =>
                        {
                            foreach (var root in roots)
                                _Attach(root, config, buttonMap, imageMap, entry, ref attachedButtons, ref attachedImages);
                            return entry.objects > 0;
                        }, true, out entry.skipReason);
                    }
                    catch (Exception e)
                    {
                        entry.error = e.Message;
                        Debug.LogError($"[MobUITool] Lỗi khi scan {path}: {e}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            RefreshIndex(config);
            report.summary = $"{files.Count} file, gắn mới {attachedButtons} button + {attachedImages} image";
            return report;
        }

        /// <summary>Dựng lại danh sách liên kết của mọi loại từ YAML: file nào chứa marker mang typeId của loại.</summary>
        public static void RefreshIndex(MobUIToolConfig config)
        {
            var byId = new Dictionary<string, GGTypeBase>();
            foreach (var t in config.types) { t.linkedAssets.Clear(); if (!string.IsNullOrEmpty(t.id)) byId[t.id] = t; }
            foreach (var t in config.imageTypes) { t.linkedAssets.Clear(); if (!string.IsNullOrEmpty(t.id)) byId[t.id] = t; }

            foreach (var path in MobUIToolAssetUtil.AllPrefabsAndScenes())
            {
                string text;
                try { text = File.ReadAllText(path); }
                catch (IOException) { continue; }
                if (!text.Contains("typeId")) continue;

                Dictionary<GGTypeBase, GGLinkedAsset> entries = null;
                foreach (Match m in s_TypeId.Matches(text))
                {
                    if (!byId.TryGetValue(m.Groups[1].Value, out var type)) continue;
                    entries ??= new Dictionary<GGTypeBase, GGLinkedAsset>();
                    if (!entries.TryGetValue(type, out var entry)) entries[type] = entry = new GGLinkedAsset { path = path };
                    entry.count++;
                }
                if (entries == null) continue;
                foreach (var pair in entries) pair.Key.linkedAssets.Add(pair.Value);
            }
        }

        /// <summary>File chứa trực tiếp marker của loại (đọc lại YAML, không dùng danh sách cache).</summary>
        public static List<string> FilesOfType(GGTypeBase type) =>
            MobUIToolAssetUtil.FilterContaining(MobUIToolAssetUtil.AllPrefabsAndScenes(), new[] { "typeId: " + type.id });

        /// <summary>
        /// Text của button: Text con trực tiếp đầu tiên, không có thì Text đầu tiên ở tầng sâu hơn.
        /// directCount = số Text con trực tiếp (> 1 là mơ hồ, cần chọn tay).
        /// </summary>
        public static Text FindText(Transform button, out int directCount)
        {
            directCount = 0;
            Text first = null;
            foreach (Transform child in button)
            {
                var t = child.GetComponent<Text>();
                if (t == null) continue;
                directCount++;
                if (first == null) first = t;
            }
            if (first != null) return first;

            foreach (var t in button.GetComponentsInChildren<Text>(true))
                if (t.transform != button) return t;
            return null;
        }

        public static string ScriptGuid(Type scriptClass)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript " + scriptClass.Name))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script != null && script.GetClass() == scriptClass) return guid;
            }
            return null;
        }

        private static void _AddType<T>(T type, Dictionary<Sprite, T> map, HashSet<string> guids, MobUIToolReport report) where T : GGTypeBase
        {
            _AddSprite(type.sourceSprite, type, map, guids, report);
            // Đối tượng đã được áp Image preset mang sprite của preset — vẫn phải nhận ra là cùng loại.
            _AddSprite(MobUIToolApplier.PresetSprite(type.imagePreset), type, map, guids, report);
        }

        private static void _AddSprite<T>(Sprite sprite, T type, Dictionary<Sprite, T> map, HashSet<string> guids, MobUIToolReport report) where T : GGTypeBase
        {
            if (sprite == null) return;
            if (map.TryGetValue(sprite, out var other) && other != type)
            {
                report.warnings.Add($"Sprite '{sprite.name}' nằm trong cả loại '{other.name}' và '{type.name}' — dùng '{other.name}'.");
                return;
            }
            map[sprite] = type;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long _)) guids.Add(guid);
        }

        private static void _Attach(GameObject root, MobUIToolConfig config, Dictionary<Sprite, GGButtonType> buttonMap,
            Dictionary<Sprite, GGImageType> imageMap, MobUIToolReport.Entry entry, ref int attachedButtons, ref int attachedImages)
        {
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null) continue;

                // Marker trỏ tới loại còn trong config → giữ nguyên (có thể đã gán tay). Marker mồ côi
                // (typeId trống hoặc loại đã bị xoá) được coi như chưa gắn.
                var buttonMarker = image.GetComponent<GGButton>();
                var imageMarker = image.GetComponent<GGImage>();
                if (buttonMarker != null && config.FindType(buttonMarker.typeId) != null) continue;
                if (imageMarker != null && config.FindImageType(imageMarker.typeId) != null) continue;

                // Loại Button chỉ nhận Image có Button cùng GameObject; còn lại rơi xuống loại Image (nếu có).
                if (image.GetComponent<Button>() != null && buttonMap.TryGetValue(image.sprite, out var buttonType))
                {
                    _RemoveOrphan(imageMarker);
                    if (buttonMarker == null) buttonMarker = image.gameObject.AddComponent<GGButton>();
                    buttonMarker.typeId = buttonType.id;
                    buttonMarker.image = image;
                    buttonMarker.text = FindText(image.transform, out _);
                    _Record(buttonMarker);
                    attachedButtons++;
                    entry.objects++;
                }
                else if (imageMap.TryGetValue(image.sprite, out var imageType))
                {
                    _RemoveOrphan(buttonMarker);
                    if (imageMarker == null) imageMarker = image.gameObject.AddComponent<GGImage>();
                    imageMarker.typeId = imageType.id;
                    imageMarker.image = image;
                    _Record(imageMarker);
                    attachedImages++;
                    entry.objects++;
                }
            }
        }

        /// <summary>Xoá marker mồ côi của kiểu kia khi Image được gắn sang tab khác (nếu xoá được từ file này).</summary>
        private static void _RemoveOrphan(Component marker)
        {
            if (marker == null) return;
            if (PrefabUtility.IsPartOfPrefabInstance(marker) && !PrefabUtility.IsAddedComponentOverride(marker)) return;
            UnityEngine.Object.DestroyImmediate(marker);
        }

        private static void _Record(Component marker)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(marker)) PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
            EditorUtility.SetDirty(marker);
        }
    }
}
