using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;
using UnityEngine.UI;

namespace Mob404.Common.UITemplate.Editor
{
    /// <summary>Áp preset của một loại lên từng button (Rect Button/Image + Rect Text/Text/Outline/Shadow) hoặc từng Image (Rect Image/Image).</summary>
    public static class MobUIToolApplier
    {
        /// <summary>
        /// Thuộc tính không bao giờ áp, dù preset có chứa: nội dung chữ, trạng thái bật/tắt, callback,
        /// dữ liệu nội bộ của Editor. Khớp theo tiền tố (vd "m_Color" loại cả "m_Color.r").
        /// </summary>
        public static readonly string[] AlwaysExcluded =
        {
            "m_Text", "m_Enabled", "m_OnCullStateChanged", "m_EditorHideFlags", "m_EditorClassIdentifier",
            "m_Script", "m_GameObject",
        };

        /// <summary>Thuộc tính vị trí của RectTransform — áp lên hàng loạt sẽ dời mọi đối tượng về cùng một chỗ.</summary>
        public static readonly string[] PositionProperties = { "m_AnchoredPosition", "m_LocalPosition" };

        /// <summary>Chạy thử: chỉ cho biết có thay đổi hay không, không sửa gì.</summary>
        public static bool DryRun;

        /// <summary>Ghi qua Undo (nút Apply only trên Inspector). Áp hàng loạt thì tắt.</summary>
        public static bool UseUndo;

        /// <summary>Cảnh báo phát sinh trong lượt áp; người gọi đọc rồi xoá.</summary>
        public static readonly List<string> Warnings = new List<string>();

        // Mỗi kiểu một GameObject tạm riêng: Image và Text đều là Graphic, không cùng nằm trên một GameObject được.
        private static readonly Dictionary<Type, Component> s_Temps = new Dictionary<Type, Component>();

        /// <summary>Áp style lên một button. Chỉ ghi khi giá trị khác. Trả về true nếu có thay đổi.</summary>
        public static bool ApplyToButton(GGButton marker, GGButtonType type)
        {
            bool changed = _ApplyBase(marker, marker.image, type);

            // Text preset là khoá của khối Text: không có thì bỏ qua cả Rect Text lẫn Outline/Shadow.
            if (!HasTextKey(type)) return changed;
            var text = marker.text;
            if (text == null)
            {
                Warnings.Add($"'{marker.name}': không có Text — bỏ qua khối Text.");
                return changed;
            }
            MobUIToolScanner.FindText(marker.transform, out int direct);
            if (direct > 1) Warnings.Add($"'{marker.name}': có {direct} Text con, đang dùng '{text.name}' — kiểm tra ô Text trên marker.");

            if (HasValues(type.textRectTransformPreset, typeof(RectTransform)) &&
                _ApplyRect(type.textRectTransformPreset, text.rectTransform))
                changed = true;
            if (HasValues(type.textPreset, typeof(Text)) && _ApplyPreset(type.textPreset, text))
                changed = true;
            if (_SyncEffect(type.effectPreset, text))
                changed = true;

            return changed;
        }

        /// <summary>Áp style lên một Image (tab Image). Chỉ ghi khi giá trị khác. Trả về true nếu có thay đổi.</summary>
        public static bool ApplyToImage(GGImage marker, GGImageType type) => _ApplyBase(marker, marker.image, type);

        /// <summary>Loại có Text preset hợp lệ (đúng kiểu Text) — điều kiện để khối Text được áp.</summary>
        public static bool HasTextKey(GGButtonType type) => IsPresetFor(type.textPreset, typeof(Text));

        /// <summary>Kiểu effect của preset trong ô Outline/Shadow: typeof(Outline), typeof(Shadow), hoặc null nếu không phải hai kiểu đó.</summary>
        public static Type EffectType(Preset preset)
        {
            if (IsPresetFor(preset, typeof(Outline))) return typeof(Outline);
            if (IsPresetFor(preset, typeof(Shadow))) return typeof(Shadow);
            return null;
        }

        /// <summary>Preset RectTransform + Image — phần chung của cả hai loại.</summary>
        private static bool _ApplyBase(Component marker, Image image, GGTypeBase type)
        {
            bool changed = false;

            if (image == null) image = marker.GetComponent<Image>();
            if (image != null && HasValues(type.imagePreset, typeof(Image)) && _ApplyPreset(type.imagePreset, image))
                changed = true;

            if (marker.transform is RectTransform rt && HasValues(type.rectTransformPreset, typeof(RectTransform)) &&
                _ApplyRect(type.rectTransformPreset, rt))
                changed = true;

            return changed;
        }

        private static bool _ApplyRect(Preset preset, RectTransform rt)
        {
            if (TouchesSize(preset) && _LayoutDriven(rt))
                Warnings.Add($"'{rt.name}': RectTransform đang bị layout (LayoutGroup/ContentSizeFitter/AspectRatioFitter) điều khiển — kích thước có thể bị tính lại.");
            return _ApplyPreset(preset, rt);
        }

        /// <summary>Preset RectTransform có áp kích thước/neo không.</summary>
        public static bool TouchesSize(Preset preset)
        {
            foreach (var p in AppliedPaths(preset))
                if (p.StartsWith("m_SizeDelta") || p.StartsWith("m_Anchor")) return true;
            return false;
        }

        /// <summary>Preset RectTransform còn áp thuộc tính vị trí (chưa exclude).</summary>
        public static bool TouchesPosition(Preset preset)
        {
            foreach (var p in AppliedPaths(preset))
                if (_Excluded(p, PositionProperties)) return true;
            return false;
        }

        /// <summary>Thêm m_AnchoredPosition/m_LocalPosition vào danh sách Exclude Property của preset rồi lưu.</summary>
        public static void ExcludePosition(Preset preset)
        {
            var list = new List<string>(preset.excludedProperties ?? new string[0]);
            foreach (var p in PositionProperties)
                if (!list.Contains(p)) list.Add(p);
            preset.excludedProperties = list.ToArray();
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssetIfDirty(preset);
        }

        private static bool _LayoutDriven(RectTransform rt)
        {
            if (rt.GetComponent<ContentSizeFitter>() != null || rt.GetComponent<UnityEngine.UI.AspectRatioFitter>() != null) return true;
            var element = rt.GetComponent<LayoutElement>();
            if (element != null && element.ignoreLayout) return false;
            var group = rt.parent != null ? rt.parent.GetComponent<LayoutGroup>() : null;
            if (group == null || !group.enabled) return false;
            if (group is GridLayoutGroup) return true;
            return group is HorizontalOrVerticalLayoutGroup h && (h.childControlWidth || h.childControlHeight);
        }

        /// <summary>
        /// Đồng bộ effect trên Text theo ô Outline/Shadow — Text chỉ dùng MỘT trong hai (so kiểu chính xác vì Outline kế thừa Shadow):
        /// preset Outline → còn đúng 1 Outline (thiếu thì thêm, thừa thì xoá), xoá mọi Shadow; preset Shadow thì ngược lại;
        /// ô trống → xoá cả hai. Preset Missing hoặc sai kiểu → không đụng gì. Trả về true nếu có thay đổi.
        /// </summary>
        private static bool _SyncEffect(Preset preset, Text text)
        {
            if (IsMissing(preset)) return false;
            var keepType = IsEmpty(preset) ? null : EffectType(preset);
            if (!IsEmpty(preset) && keepType == null) return false;

            bool changed = false;
            foreach (var effectType in new[] { typeof(Outline), typeof(Shadow) })
            {
                var existing = new List<Component>();
                foreach (var c in text.GetComponents(effectType))
                    if (c.GetType() == effectType) existing.Add(c);

                if (effectType != keepType)
                {
                    if (_RemoveEffects(existing, null, text, effectType)) changed = true;
                    continue;
                }

                if (existing.Count == 0)
                {
                    changed = true;
                    if (DryRun) continue;
                    var added = UseUndo ? Undo.AddComponent(text.gameObject, effectType) : text.gameObject.AddComponent(effectType);
                    _ApplyPreset(preset, added);
                    EditorUtility.SetDirty(text.gameObject);
                    continue;
                }

                // Giữ component không xoá được (thuộc prefab nguồn) nếu có, để không còn dư; không có thì giữ cái đầu tiên.
                var keep = existing.Find(c => !_CanRemove(c)) ?? existing[0];
                if (_RemoveEffects(existing, keep, text, effectType)) changed = true;
                if (_ApplyPreset(preset, keep)) changed = true;
            }
            return changed;
        }

        /// <summary>Xoá mọi component trong danh sách trừ keep. Trả về true nếu xoá (hoặc, khi chạy thử, sẽ xoá) ít nhất một.</summary>
        private static bool _RemoveEffects(List<Component> effects, Component keep, Text text, Type effectType)
        {
            bool removed = false;
            foreach (var c in effects)
            {
                if (c == keep) continue;
                // Component thuộc prefab nguồn không xoá được từ instance — prefab nguồn (xử lý trước) sẽ tự xoá.
                if (!_CanRemove(c))
                {
                    Warnings.Add($"'{text.name}': {effectType.Name} thuộc prefab lồng nhau, không xoá được từ đây.");
                    continue;
                }
                removed = true;
                if (DryRun) continue;
                if (UseUndo) Undo.DestroyObjectImmediate(c);
                else UnityEngine.Object.DestroyImmediate(c);
            }
            if (removed && !DryRun) EditorUtility.SetDirty(text.gameObject);
            return removed;
        }

        private static bool _CanRemove(Component c) =>
            !PrefabUtility.IsPartOfPrefabInstance(c) || PrefabUtility.IsAddedComponentOverride(c);

        /// <summary>
        /// Ô có tham chiếu nhưng file .preset đã mất (Missing) — khác với ô trống. Ô trống có thể là "fake null"
        /// của Editor nên không dùng ReferenceEquals được; Missing thì vẫn giữ instance ID khác 0.
        /// </summary>
        public static bool IsMissing(Preset preset) => !ReferenceEquals(preset, null) && preset == null && preset.GetInstanceID() != 0;

        /// <summary>Ô thật sự trống (chưa kéo preset nào).</summary>
        public static bool IsEmpty(Preset preset) => preset == null && !IsMissing(preset);

        /// <summary>Preset có đúng là preset của kiểu component này không (null = không).</summary>
        public static bool IsPresetFor(Preset preset, Type componentType)
        {
            if (preset == null) return false;
            if (preset.GetTargetFullTypeName() == componentType.FullName) return true;
            // Kiểu native (vd RectTransform) trả về tên rút gọn kiểu "UI.RectTransform", không phải FullName.
            return componentType.Namespace == "UnityEngine" && preset.GetTargetTypeName() == componentType.Name;
        }

        /// <summary>Preset đúng kiểu và còn ít nhất một thuộc tính để áp. Ô trống, preset sai kiểu, preset exclude hết = false (bỏ qua).</summary>
        public static bool HasValues(Preset preset, Type componentType) =>
            IsPresetFor(preset, componentType) && AppliedPaths(preset).Count > 0;

        /// <summary>Các thuộc tính sẽ thực sự được áp: có trong preset, trừ excludedProperties của preset và AlwaysExcluded.</summary>
        public static List<string> AppliedPaths(Preset preset)
        {
            var paths = new List<string>();
            if (preset == null) return paths;
            foreach (var mod in preset.PropertyModifications)
            {
                string path = mod.propertyPath;
                if (_Excluded(path, AlwaysExcluded) || _Excluded(path, preset.excludedProperties)) continue;
                if (!paths.Contains(path)) paths.Add(path);
            }
            return paths;
        }

        /// <summary>Sprite mà Image preset sẽ gán (m_Sprite), null nếu preset không áp sprite.</summary>
        public static Sprite PresetSprite(Preset preset)
        {
            if (!IsPresetFor(preset, typeof(Image)) || !AppliedPaths(preset).Contains("m_Sprite")) return null;
            foreach (var mod in preset.PropertyModifications)
                if (mod.propertyPath == "m_Sprite") return mod.objectReference as Sprite;
            return null;
        }

        /// <summary>Huỷ GameObject tạm dùng để áp preset. Gọi khi xong một lượt.</summary>
        public static void CleanupTemp()
        {
            foreach (var c in s_Temps.Values)
                if (c != null) UnityEngine.Object.DestroyImmediate(c.gameObject);
            s_Temps.Clear();
        }

        /// <summary>
        /// Áp preset lên bản sao tạm của component, rồi chỉ chép sang component thật những thuộc tính
        /// được phép và đang khác — nhờ vậy không đụng thuộc tính bị loại và không lưu file khi không đổi gì.
        /// Instance lồng nhau đang override một thuộc tính mà giá trị mới trùng prefab nguồn → gỡ override
        /// (thay vì ghi đè), để sửa prefab nguồn về sau vẫn lan xuống instance.
        /// </summary>
        private static bool _ApplyPreset(Preset preset, Component target)
        {
            var temp = _TempComponent(target.GetType());
            EditorUtility.CopySerialized(target, temp);
            preset.ApplyTo(temp);

            var src = new SerializedObject(temp);
            var dst = new SerializedObject(target);
            SerializedObject origin = null;
            if (PrefabUtility.IsPartOfPrefabInstance(target))
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(target);
                if (source != null) origin = new SerializedObject(source);
            }

            bool copied = false;
            var revert = new List<string>();
            foreach (string path in AppliedPaths(preset))
            {
                var from = src.FindProperty(path);
                var to = dst.FindProperty(path);
                if (from == null || to == null) continue;
                if (origin != null && to.prefabOverride)
                {
                    var original = origin.FindProperty(path);
                    if (original != null && SerializedProperty.DataEquals(from, original))
                    {
                        revert.Add(path);
                        continue;
                    }
                }
                if (SerializedProperty.DataEquals(from, to)) continue;
                if (!DryRun) dst.CopyFromSerializedProperty(from);
                copied = true;
            }
            if (!copied && revert.Count == 0) return false;
            if (DryRun) return true;

            if (copied)
            {
                if (UseUndo) dst.ApplyModifiedProperties();
                else dst.ApplyModifiedPropertiesWithoutUndo();
                if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                EditorUtility.SetDirty(target);
            }
            if (revert.Count > 0)
            {
                dst.Update();
                var mode = UseUndo ? InteractionMode.UserAction : InteractionMode.AutomatedAction;
                foreach (var path in revert)
                {
                    var prop = dst.FindProperty(path);
                    if (prop != null && prop.prefabOverride) PrefabUtility.RevertPropertyOverride(prop, mode);
                }
            }
            return true;
        }

        private static bool _Excluded(string path, string[] prefixes)
        {
            if (prefixes == null) return false;
            foreach (var p in prefixes)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (path == p || path.StartsWith(p + ".") || path.StartsWith(p + "[")) return true;
            }
            return false;
        }

        private static Component _TempComponent(Type type)
        {
            if (s_Temps.TryGetValue(type, out var temp) && temp != null) return temp;
            var go = new GameObject("MobUIToolTemp_" + type.Name, typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false);
            temp = go.GetComponent(type);
            if (temp == null) temp = go.AddComponent(type);
            s_Temps[type] = temp;
            return temp;
        }
    }
}
