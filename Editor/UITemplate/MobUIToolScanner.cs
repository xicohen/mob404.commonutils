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
            var tabMap = new Dictionary<Sprite, GGTabType>();
            // Sprite thuộc ≥ 2 loại cùng danh mục: không đoán loại, chỉ liệt kê đối tượng để gắn tay.
            var buttonAmbiguous = new Dictionary<Sprite, List<string>>();
            var imageAmbiguous = new Dictionary<Sprite, List<string>>();
            var tabAmbiguous = new Dictionary<Sprite, List<string>>();
            var guids = new HashSet<string>();
            // Loại có khối Cost trùng sprite với loại thường (vd Btn_Yellow) — không gắn tự động, gắn marker bằng tay.
            foreach (var type in config.types)
                if (!MobUIToolApplier.HasCostKey(type)) _AddType(type, buttonMap, buttonAmbiguous, guids);
            foreach (var type in config.imageTypes) _AddType(type, imageMap, imageAmbiguous, guids);
            foreach (var type in config.tabTypes)
            {
                _AddSprite(type.sourceSprite, type, tabMap, tabAmbiguous, guids);
                _AddSprite(MobUIToolApplier.PresetSprite(type.on.imagePreset), type, tabMap, tabAmbiguous, guids);
            }

            EditorUtility.DisplayProgressBar(report.title, "Lọc prefab/scene…", 0f);
            var files = MobUIToolAssetUtil.SortByDependency(
                MobUIToolAssetUtil.FilterContaining(MobUIToolAssetUtil.AllPrefabsAndScenes(), guids));

            int attachedButtons = 0, attachedImages = 0, attachedTabs = 0, ambiguousCount = 0;
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
                            {
                                _AttachTabs(root, config, tabMap, tabAmbiguous, entry, ref attachedTabs, ref ambiguousCount);
                                _Attach(root, config, buttonMap, imageMap, buttonAmbiguous, imageAmbiguous, entry,
                                    ref attachedButtons, ref attachedImages, ref ambiguousCount);
                            }
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
            report.summary = $"{files.Count} file, gắn mới {attachedButtons} button + {attachedImages} image + {attachedTabs} tab" +
                             (ambiguousCount > 0 ? $", {ambiguousCount} đối tượng cần chọn loại (sprite dùng chung — xem cảnh báo từng file)" : "");
            return report;
        }

        /// <summary>Dựng lại danh sách liên kết của mọi loại từ YAML: file nào chứa marker mang typeId của loại.</summary>
        public static void RefreshIndex(MobUIToolConfig config)
        {
            var byId = new Dictionary<string, GGTypeBase>();
            foreach (var t in config.types) { t.linkedAssets.Clear(); if (!string.IsNullOrEmpty(t.id)) byId[t.id] = t; }
            foreach (var t in config.imageTypes) { t.linkedAssets.Clear(); if (!string.IsNullOrEmpty(t.id)) byId[t.id] = t; }
            foreach (var t in config.tabTypes) { t.linkedAssets.Clear(); if (!string.IsNullOrEmpty(t.id)) byId[t.id] = t; }
            foreach (var t in config.blockTypes) { t.linkedAssets.Clear(); if (!string.IsNullOrEmpty(t.id)) byId[t.id] = t; }

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

        /// <summary>
        /// Icon giá + Text giá của button kèm giá: Image con cháu đầu tiên (khác chính button) có chứa một Text khác mainText.
        /// Nhận diện theo cấu trúc vì tên không thống nhất (IconCost/TextCost, IconGem/TextGem, Coin/Number…).
        /// </summary>
        public static void FindCost(Transform button, Text mainText, out Image icon, out Text costText)
        {
            icon = null;
            costText = null;
            foreach (var image in button.GetComponentsInChildren<Image>(true))
            {
                if (image.transform == button) continue;
                foreach (var t in image.GetComponentsInChildren<Text>(true))
                {
                    if (t == mainText) continue;
                    icon = image;
                    costText = t;
                    return;
                }
            }
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

        private static void _AddType<T>(T type, Dictionary<Sprite, T> map, Dictionary<Sprite, List<string>> ambiguous, HashSet<string> guids) where T : GGTypeBase
        {
            _AddSprite(type.sourceSprite, type, map, ambiguous, guids);
            // Đối tượng đã được áp Image preset mang sprite của preset — vẫn phải nhận ra là cùng loại.
            _AddSprite(MobUIToolApplier.PresetSprite(type.imagePreset), type, map, ambiguous, guids);
        }

        /// <summary>
        /// Sprite → loại. Sprite đã thuộc loại khác thì chuyển sang danh sách mơ hồ (kèm tên các loại ứng viên): Scan không
        /// gắn đối tượng dùng sprite đó, tránh gắn nhầm theo thứ tự config.
        /// </summary>
        private static void _AddSprite<T>(Sprite sprite, T type, Dictionary<Sprite, T> map, Dictionary<Sprite, List<string>> ambiguous,
            HashSet<string> guids) where T : GGTypeBase
        {
            if (sprite == null) return;
            if (ambiguous.TryGetValue(sprite, out var names))
            {
                if (!names.Contains(type.name)) names.Add(type.name);
                return;
            }
            if (map.TryGetValue(sprite, out var other) && other != type)
            {
                map.Remove(sprite);
                ambiguous[sprite] = new List<string> { other.name, type.name };
                return;
            }
            map[sprite] = type;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long _)) guids.Add(guid);
        }

        private static void _Ambiguous(MobUIToolReport.Entry entry, Transform node, string kind, List<string> candidates, ref int ambiguousCount)
        {
            entry.warnings.Add($"Cần chọn loại {kind}: '{_Path(node)}' — sprite dùng chung bởi {string.Join(", ", candidates)}. " +
                               (kind == "Tab" ? "Chọn tab rồi Attach to selection." : "Gắn marker bằng tay rồi chọn loại ở Inspector."));
            ambiguousCount++;
        }

        private static string _Path(Transform t)
        {
            string path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        private static void _Attach(GameObject root, MobUIToolConfig config, Dictionary<Sprite, GGButtonType> buttonMap,
            Dictionary<Sprite, GGImageType> imageMap, Dictionary<Sprite, List<string>> buttonAmbiguous,
            Dictionary<Sprite, List<string>> imageAmbiguous, MobUIToolReport.Entry entry,
            ref int attachedButtons, ref int attachedImages, ref int ambiguousCount)
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
                bool hasButton = image.GetComponent<Button>() != null;
                if (hasButton && buttonAmbiguous.TryGetValue(image.sprite, out var buttonCandidates))
                    _Ambiguous(entry, image.transform, "Button", buttonCandidates, ref ambiguousCount);
                else if (hasButton && buttonMap.TryGetValue(image.sprite, out var buttonType))
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
                else if (imageAmbiguous.TryGetValue(image.sprite, out var imageCandidates))
                    _Ambiguous(entry, image.transform, "Image", imageCandidates, ref ambiguousCount);
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

        /// <summary>
        /// Tab: Image dùng sprite nguồn của một loại Tab → gắn GGTab lên tab gốc (tổ tiên gần nhất có Button).
        /// Tab cấu trúc cũ chưa dùng sprite đó → gắn tay bằng "Attach to selection".
        /// </summary>
        private static void _AttachTabs(GameObject root, MobUIToolConfig config, Dictionary<Sprite, GGTabType> tabMap,
            Dictionary<Sprite, List<string>> tabAmbiguous, MobUIToolReport.Entry entry, ref int attachedTabs, ref int ambiguousCount)
        {
            if (tabMap.Count == 0 && tabAmbiguous.Count == 0) return;
            var reported = new HashSet<GameObject>();
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null) continue;
                tabAmbiguous.TryGetValue(image.sprite, out var candidates);
                GGTabType tabType = null;
                if (candidates == null && !tabMap.TryGetValue(image.sprite, out tabType)) continue;
                var tabRoot = MobUIToolTabApplier.TabRoot(image.gameObject);
                if (tabRoot == null) continue;
                var marker = tabRoot.GetComponent<GGTab>();
                if (marker != null && config.FindTabType(marker.typeId) != null) continue;
                if (candidates != null)
                {
                    if (reported.Add(tabRoot)) _Ambiguous(entry, tabRoot.transform, "Tab", candidates, ref ambiguousCount);
                    continue;
                }
                if (marker == null) marker = tabRoot.AddComponent<GGTab>();
                marker.typeId = tabType.id;
                _Record(marker);
                attachedTabs++;
                entry.objects++;
            }
        }

        /// <summary>Gắn (hoặc đổi) marker GGTab của loại lên tab chứa từng object đang chọn. Undo được; người dùng tự lưu scene/prefab.</summary>
        public static int AttachToSelection(GGTabType type, GameObject[] selection, out string message)
        {
            int n = 0;
            var skipped = new List<string>();
            var done = new HashSet<GameObject>();
            foreach (var go in selection)
            {
                var tabRoot = MobUIToolTabApplier.TabRoot(go);
                if (tabRoot == null) { skipped.Add(go.name); continue; }
                if (!done.Add(tabRoot)) continue;
                var marker = tabRoot.GetComponent<GGTab>();
                if (marker == null) marker = Undo.AddComponent<GGTab>(tabRoot);
                else Undo.RecordObject(marker, "MobUITool attach tab");
                marker.typeId = type.id;
                _Record(marker);
                n++;
            }
            message = $"Gắn loại '{type.name}' cho {n} tab" + (skipped.Count > 0 ? $"; bỏ qua (không nằm trong GameObject có Button): {string.Join(", ", skipped)}" : "") +
                      ". Lưu scene/prefab rồi Refresh để thấy trong Linked.";
            return n;
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
