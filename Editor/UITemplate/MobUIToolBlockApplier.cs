using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Mob404.Common.UITemplate.Editor
{
    /// <summary>
    /// Loại Block (khối ghép): đồng bộ cấu trúc theo path/aliases của từng phần (dời + đổi tên node cũ, tạo node thiếu) rồi áp
    /// preset gốc + từng phần. Tạo loại mới từ một khối mẫu đang chọn. Dùng chung cờ DryRun/UseUndo/Warnings của MobUIToolApplier.
    /// </summary>
    public static class MobUIToolBlockApplier
    {
        /// <summary>Áp loại lên một khối. Chỉ ghi khi giá trị khác. Trả về true nếu có thay đổi.</summary>
        public static bool ApplyToBlock(GGBlock marker, GGBlockType type)
        {
            bool changed = false;
            var rootImage = marker.GetComponent<Image>();
            if (rootImage != null && MobUIToolApplier.HasValues(type.imagePreset, typeof(Image)) &&
                MobUIToolApplier._ApplyPreset(type.imagePreset, rootImage))
                changed = true;
            if (marker.transform is RectTransform rt && MobUIToolApplier.HasValues(type.rectTransformPreset, typeof(RectTransform)) &&
                MobUIToolApplier._ApplyRect(type.rectTransformPreset, rt))
                changed = true;

            // Thứ tự phần = thứ tự lúc tạo (cha trước con) — node cha đã được dời/tạo trước khi tới node con.
            foreach (var part in type.parts)
            {
                if (part == null || string.IsNullOrEmpty(part.path)) continue;
                var node = _Sync(marker, part, ref changed);
                if (node != null && _ApplyPart(marker, part, node)) changed = true;
            }
            return changed;
        }

        private static bool _ApplyPart(GGBlock marker, GGBlockPart part, RectTransform node)
        {
            bool changed = false;
            if (MobUIToolApplier.HasValues(part.rectPreset, typeof(RectTransform)) && MobUIToolApplier._ApplyRect(part.rectPreset, node))
                changed = true;

            if (MobUIToolApplier.HasValues(part.imagePreset, typeof(Image)))
            {
                var image = node.GetComponent<Image>();
                if (image == null) MobUIToolApplier.Warnings.Add($"'{marker.name}/{part.path}': không có Image — bỏ qua Image preset.");
                else if (MobUIToolApplier._ApplyPreset(part.imagePreset, image)) changed = true;
            }

            // Text preset là khoá của Outline/Shadow (giống Button/Tab).
            if (MobUIToolApplier.IsPresetFor(part.textPreset, typeof(Text)))
            {
                var text = node.GetComponent<Text>();
                if (text == null) MobUIToolApplier.Warnings.Add($"'{marker.name}/{part.path}': không có Text — bỏ qua Text preset.");
                else
                {
                    if (MobUIToolApplier.HasValues(part.textPreset, typeof(Text)) && MobUIToolApplier._ApplyPreset(part.textPreset, text))
                        changed = true;
                    if (MobUIToolApplier._SyncEffect(part.effectPreset, text)) changed = true;
                }
            }
            return changed;
        }

        /// <summary>
        /// Node của phần: có sẵn ở path → dùng; không thì node ở một alias được dời về cha của path + đổi tên (cùng GameObject nên
        /// tham chiếu giữ nguyên); không có nữa thì tạo node mới (Image/Text theo preset của phần). Chạy thử: báo, không sửa.
        /// </summary>
        private static RectTransform _Sync(GGBlock marker, GGBlockPart part, ref bool changed)
        {
            var root = marker.transform;
            var node = root.Find(part.path) as RectTransform;
            if (node != null) return node;

            int slash = part.path.LastIndexOf('/');
            string parentPath = slash < 0 ? "" : part.path.Substring(0, slash);
            string name = slash < 0 ? part.path : part.path.Substring(slash + 1);
            var parent = parentPath.Length == 0 ? root : root.Find(parentPath);
            if (parent == null)
            {
                // Chạy thử không dời node cha thật nên chỗ này có thể chỉ là hệ quả của lần dời phía trên.
                if (!MobUIToolApplier.DryRun)
                    MobUIToolApplier.Warnings.Add($"'{marker.name}': không có node cha '{parentPath}' của '{part.path}' — bỏ qua phần này.");
                return null;
            }

            foreach (var alias in part.aliases)
            {
                if (string.IsNullOrEmpty(alias)) continue;
                var old = root.Find(alias) as RectTransform;
                if (old == null) continue;
                if (old.parent != parent && PrefabUtility.IsPartOfPrefabInstance(old) && !PrefabUtility.IsAddedGameObjectOverride(old.gameObject))
                {
                    MobUIToolApplier.Warnings.Add($"'{marker.name}/{alias}': thuộc prefab lồng nhau, không dời được từ đây — sửa ở prefab nguồn.");
                    return null;
                }
                changed = true;
                MobUIToolApplier.Warnings.Add($"'{marker.name}': {(MobUIToolApplier.DryRun ? "sẽ dời" : "đã dời")} '{alias}' → '{part.path}'.");
                if (MobUIToolApplier.DryRun) return old;
                if (old.parent != parent)
                {
                    if (MobUIToolApplier.UseUndo)
                    {
                        // Undo.SetTransformParent giữ toạ độ thế giới — đặt lại toạ độ cục bộ như SetParent(parent, false).
                        var pos = old.anchoredPosition;
                        var scale = old.localScale;
                        var rot = old.localRotation;
                        var size = old.sizeDelta;
                        Undo.SetTransformParent(old, parent, "MobUITool move " + name);
                        Undo.RecordObject(old, "MobUITool move " + name);
                        old.localRotation = rot;
                        old.localScale = scale;
                        old.sizeDelta = size;
                        old.anchoredPosition = pos;
                    }
                    else old.SetParent(parent, false);
                }
                if (old.name != name)
                {
                    if (MobUIToolApplier.UseUndo) Undo.RecordObject(old.gameObject, "MobUITool rename " + name);
                    old.gameObject.name = name;
                    if (PrefabUtility.IsPartOfPrefabInstance(old)) PrefabUtility.RecordPrefabInstancePropertyModifications(old.gameObject);
                }
                EditorUtility.SetDirty(old.gameObject);
                return old;
            }

            changed = true;
            MobUIToolApplier.Warnings.Add($"'{marker.name}': {(MobUIToolApplier.DryRun ? "sẽ tạo" : "đã tạo")} node '{part.path}'.");
            if (MobUIToolApplier.DryRun) return null;
            var go = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
            if (MobUIToolApplier.UseUndo) Undo.RegisterCreatedObjectUndo(go, "MobUITool create " + name);
            go.transform.SetParent(parent, false);
            if (MobUIToolApplier.IsPresetFor(part.imagePreset, typeof(Image))) go.AddComponent<Image>();
            else if (MobUIToolApplier.IsPresetFor(part.textPreset, typeof(Text))) go.AddComponent<Text>();
            EditorUtility.SetDirty(parent.gameObject);
            return (RectTransform)go.transform;
        }

        /// <summary>
        /// Tạo loại mới từ khối mẫu: preset Rect/Image gốc + mỗi node con (mọi tầng, cha trước con) một phần với preset
        /// Rect/Image/Text/Outline-Shadow của chính node đó; gắn GGBlock của loại lên khối mẫu (Undo được, người dùng tự lưu).
        /// </summary>
        public static GGBlockType CreateTypeFrom(MobUIToolConfig config, GameObject root, string typeName, out string message)
        {
            if (root == null || !(root.transform is RectTransform))
            {
                message = "Chọn GameObject gốc của khối mẫu (có RectTransform) trong Hierarchy trước.";
                return null;
            }

            var type = new GGBlockType
            {
                id = System.Guid.NewGuid().ToString("N").Substring(0, 8),
                name = string.IsNullOrEmpty(typeName) ? "Block_" + MobUIToolPresetFactory.Pascal(root.name) : typeName,
            };
            type.rectTransformPreset = MobUIToolPresetFactory.SaveBlockPreset(config, MobUIToolPresetFactory.Slot.Rect, root.transform, true, type.name, out _);
            var rootImage = root.GetComponent<Image>();
            if (rootImage != null)
                type.imagePreset = MobUIToolPresetFactory.SaveBlockPreset(config, MobUIToolPresetFactory.Slot.Image, rootImage, false, type.name, out _);

            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.transform == root.transform) continue;
                var part = new GGBlockPart { path = RelativePath(root.transform, rt) };
                part.rectPreset = MobUIToolPresetFactory.SaveBlockPreset(config, MobUIToolPresetFactory.Slot.Rect, rt, false, type.name, out _);
                var image = rt.GetComponent<Image>();
                if (image != null) part.imagePreset = MobUIToolPresetFactory.SaveBlockPreset(config, MobUIToolPresetFactory.Slot.Image, image, false, type.name, out _);
                var text = rt.GetComponent<Text>();
                if (text != null)
                {
                    part.textPreset = MobUIToolPresetFactory.SaveBlockPreset(config, MobUIToolPresetFactory.Slot.Text, text, false, type.name, out _);
                    var effect = _Effect(text);
                    if (effect != null) part.effectPreset = MobUIToolPresetFactory.SaveBlockPreset(config, MobUIToolPresetFactory.Slot.Effect, effect, false, type.name, out _);
                }
                type.parts.Add(part);
            }

            Undo.RecordObject(config, "MobUITool add block type");
            config.blockTypes.Add(type);
            config.Save();
            AttachToSelection(type, new[] { root }, out _);
            message = $"Tạo loại '{type.name}' từ '{root.name}' ({type.parts.Count} phần), đã gắn GGBlock lên mẫu — lưu scene/prefab rồi Refresh.";
            return type;
        }

        /// <summary>Gắn (hoặc đổi) marker GGBlock của loại lên CHÍNH từng object đang chọn (gốc khối). Undo được.</summary>
        public static int AttachToSelection(GGBlockType type, GameObject[] selection, out string message)
        {
            int n = 0;
            foreach (var go in selection)
            {
                if (!(go.transform is RectTransform)) continue;
                var marker = go.GetComponent<GGBlock>();
                if (marker == null) marker = Undo.AddComponent<GGBlock>(go);
                else Undo.RecordObject(marker, "MobUITool attach block");
                marker.typeId = type.id;
                if (PrefabUtility.IsPartOfPrefabInstance(marker)) PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
                EditorUtility.SetDirty(marker);
                n++;
            }
            message = $"Gắn loại '{type.name}' cho {n} khối. Lưu scene/prefab rồi Refresh để thấy trong Linked.";
            return n;
        }

        /// <summary>"IconPower/TextPower" — đường dẫn của node tính từ gốc khối.</summary>
        public static string RelativePath(Transform root, Transform node)
        {
            var parts = new List<string>();
            for (var t = node; t != null && t != root; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }

        private static Component _Effect(Text text)
        {
            foreach (var c in text.GetComponents<Shadow>())
                if (c.GetType() == typeof(Outline)) return c;
            foreach (var c in text.GetComponents<Shadow>())
                if (c.GetType() == typeof(Shadow)) return c;
            return null;
        }
    }
}
