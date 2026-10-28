using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;
using UnityEngine.UI;

namespace Mob404.Common.UITemplate.Editor
{
    /// <summary>
    /// Tạo file .preset từ object mẫu đang chọn cho một ô của loại: tự đặt tên theo quy tắc trong README
    /// (Rect_/Img_/Txt_/Outline_/Shadow_) và lưu vào Presets/&lt;Rect|Image|Text|Effect&gt;/ cạnh config.
    /// </summary>
    public static class MobUIToolPresetFactory
    {
        public enum Slot { Rect, Image, TextRect, Text, Effect, Layout }

        /// <summary>Trả về preset vừa tạo, hoặc null (message cho biết lý do).</summary>
        public static Preset CreateFromSelection(MobUIToolConfig config, Slot slot, bool imageTab, out string message)
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                message = "Chọn một object mẫu trong Hierarchy (hoặc Prefab Mode) trước.";
                return null;
            }

            var root = _Root(selected, imageTab);
            Text text = null;
            if (slot == Slot.TextRect || slot == Slot.Text || slot == Slot.Effect)
            {
                text = selected.GetComponent<Text>();
                if (text == null)
                {
                    var marker = root.GetComponent<GGButton>();
                    text = marker != null && marker.text != null ? marker.text : MobUIToolScanner.FindText(root.transform, out _);
                }
            }

            Component component = null;
            switch (slot)
            {
                case Slot.Rect: component = root.transform as RectTransform; break;
                case Slot.Image: component = root.GetComponent<Image>(); break;
                case Slot.TextRect: component = text != null ? text.rectTransform : null; break;
                case Slot.Text: component = text; break;
                case Slot.Effect:
                    if (text != null)
                    {
                        foreach (var c in text.GetComponents<Shadow>())
                            if (c.GetType() == typeof(Outline)) { component = c; break; }
                        if (component == null)
                            foreach (var c in text.GetComponents<Shadow>())
                                if (c.GetType() == typeof(Shadow)) { component = c; break; }
                    }
                    break;
            }
            if (component == null)
            {
                message = $"'{selected.name}' không có component phù hợp cho ô {slot} (Text/Outline/Shadow lấy từ Text con của button).";
                return null;
            }

            var preset = new Preset(component);
            // Rect gốc không mang vị trí — áp hàng loạt mà kèm vị trí sẽ dời mọi đối tượng về một chỗ.
            // Rect Text là node con: giữ vị trí tương đối trong button, chỉ bỏ m_LocalPosition.
            if (component is RectTransform)
                preset.excludedProperties = slot == Slot.TextRect ? ChildRectExcluded : MobUIToolApplier.PositionProperties;

            string folder = $"{System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(config)).Replace('\\', '/')}/Presets/{_Folder(slot)}";
            MobUIToolConfig.EnsureFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{_Name(slot, component, imageTab)}.preset");
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            message = $"Đã tạo {path} từ '{component.name}'.";
            return preset;
        }

        /// <summary>
        /// Tab "Tab": tạo preset từ component đúng kiểu NGAY TRÊN object đang chọn (chọn đúng node Image/Title của nửa
        /// ON/OFF, hoặc tab gốc). excludePosition = chỉ ô Rect của tab gốc — node con của tab giữ vị trí tương đối.
        /// </summary>
        public static Preset CreateForTab(MobUIToolConfig config, Slot slot, System.Type componentType, bool excludePosition, out string message)
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                message = "Chọn đúng node mẫu trong Hierarchy (tab gốc, hoặc node Image/Title của nửa ON/OFF) trước.";
                return null;
            }

            var component = _OnNode(selected, slot, componentType, out message);
            return component != null ? SaveTabPreset(config, slot, component, excludePosition, out message) : null;
        }

        /// <summary>Loại Block: tạo preset từ component NGAY TRÊN node đang chọn (gốc khối hoặc đúng node con mẫu).</summary>
        public static Preset CreateForBlock(MobUIToolConfig config, Slot slot, System.Type componentType, bool rootRect, string owner, out string message)
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                message = "Chọn đúng node mẫu trong Hierarchy (gốc khối, hoặc node con của phần) trước.";
                return null;
            }
            var component = _OnNode(selected, slot, componentType, out message);
            return component != null ? SaveBlockPreset(config, slot, component, rootRect, owner, out message) : null;
        }

        /// <summary>Khối Cost của loại Button: tạo preset từ component NGAY TRÊN node đang chọn (icon giá hoặc Text giá).</summary>
        public static Preset CreateForCost(MobUIToolConfig config, Slot slot, System.Type componentType, out string message)
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                message = "Chọn đúng node mẫu trong Hierarchy (icon giá, vd IconCost, hoặc Text giá, vd TextCost) trước.";
                return null;
            }
            var component = _OnNode(selected, slot, componentType, out message);
            return component != null ? SaveButtonPreset(config, slot, component, false, out message) : null;
        }

        private static Component _OnNode(GameObject selected, Slot slot, System.Type componentType, out string message)
        {
            message = null;
            Component component = null;
            if (slot == Slot.Effect)
            {
                foreach (var c in selected.GetComponents<Shadow>())
                    if (c.GetType() == typeof(Outline)) { component = c; break; }
                if (component == null)
                    foreach (var c in selected.GetComponents<Shadow>())
                        if (c.GetType() == typeof(Shadow)) { component = c; break; }
            }
            else component = selected.GetComponent(componentType);
            if (component == null)
                message = $"'{selected.name}' không có {(slot == Slot.Effect ? "Outline/Shadow" : componentType.Name)} — chọn đúng node mẫu.";
            return component;
        }

        /// <summary>
        /// Tạo file preset cho một ô của loại Tab từ component cho sẵn. rootRect = Rect của tab gốc (bỏ cả vị trí);
        /// Rect node con giữ m_AnchoredPosition nhưng bỏ m_LocalPosition (Unity tự tính lại, lưu vào preset chỉ gây lệch giả).
        /// </summary>
        public static Preset SaveTabPreset(MobUIToolConfig config, Slot slot, Component component, bool rootRect, out string message)
        {
            var preset = new Preset(component);
            if (component is RectTransform) preset.excludedProperties = rootRect ? MobUIToolApplier.PositionProperties : ChildRectExcluded;

            string folder = $"{System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(config)).Replace('\\', '/')}/Presets/{_Folder(slot)}";
            MobUIToolConfig.EnsureFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{_TabName(slot, component)}.preset");
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            message = $"Đã tạo {path} từ '{component.name}'.";
            return preset;
        }

        /// <summary>
        /// Tạo file preset cho một ô của loại Button từ component cho sẵn (đặt tên như nút +). rootRect = Rect của button
        /// gốc (bỏ cả vị trí); Rect node con (Text, icon/Text giá) giữ m_AnchoredPosition, chỉ bỏ m_LocalPosition.
        /// </summary>
        public static Preset SaveButtonPreset(MobUIToolConfig config, Slot slot, Component component, bool rootRect, out string message)
        {
            var preset = new Preset(component);
            if (component is RectTransform) preset.excludedProperties = rootRect ? MobUIToolApplier.PositionProperties : ChildRectExcluded;

            string folder = $"{System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(config)).Replace('\\', '/')}/Presets/{_Folder(slot)}";
            MobUIToolConfig.EnsureFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{_Name(slot, component, false)}.preset");
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            message = $"Đã tạo {path} từ '{component.name}'.";
            return preset;
        }

        /// <summary>Thuộc tính bỏ qua của Rect node con (tab và button).</summary>
        public static readonly string[] ChildRectExcluded = { "m_LocalPosition" };

        /// <summary>
        /// Rect gốc của khối (Block): bỏ cả neo + pivot — mỗi màn neo khối theo bố cục riêng, đổi neo/pivot mà giữ
        /// m_AnchoredPosition sẽ làm khối dời chỗ. Chỉ còn kích thước, scale (lật chiều), xoay.
        /// </summary>
        public static readonly string[] BlockRootExcluded = { "m_AnchoredPosition", "m_LocalPosition", "m_AnchorMin", "m_AnchorMax", "m_Pivot" };

        /// <summary>
        /// Tạo file preset cho một ô của loại Block từ component cho sẵn. owner = tên loại (đưa vào tên file để hai loại có
        /// node trùng tên không lẫn nhau). rootRect = Rect của gốc khối (BlockRootExcluded); node con giữ vị trí trong khối.
        /// </summary>
        public static Preset SaveBlockPreset(MobUIToolConfig config, Slot slot, Component component, bool rootRect, string owner, out string message)
        {
            var preset = new Preset(component);
            if (component is RectTransform) preset.excludedProperties = rootRect ? BlockRootExcluded : ChildRectExcluded;

            string folder = $"{System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(config)).Replace('\\', '/')}/Presets/{_Folder(slot)}";
            MobUIToolConfig.EnsureFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{_BlockName(slot, component, Pascal(owner))}.preset");
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            message = $"Đã tạo {path} từ '{component.name}'.";
            return preset;
        }

        private static string _BlockName(Slot slot, Component c, string owner)
        {
            string node = Pascal(c.name);
            switch (slot)
            {
                case Slot.Rect:
                case Slot.TextRect:
                    var rt = (RectTransform)c;
                    return $"Rect_{owner}_{node}_{_Num(rt.rect.width)}x{_Num(rt.rect.height)}";
                case Slot.Image:
                    var image = (Image)c;
                    return $"Img_{owner}_{node}_{(image.sprite != null ? Pascal(image.sprite.name) : _Hex(image.color))}";
                case Slot.Text:
                    var text = (Text)c;
                    return $"Txt_{owner}_{node}_{text.fontSize}_{_Hex(text.color)}";
                default:
                    return _Name(slot, c, false);
            }
        }

        private static string _TabName(Slot slot, Component c)
        {
            switch (slot)
            {
                case Slot.Layout:
                    var le = (LayoutElement)c;
                    return $"Layout_Tab_{_Num(le.preferredWidth)}x{_Num(le.preferredHeight)}";
                case Slot.Text:
                    var text = (Text)c;
                    return $"Txt_Tab_{text.fontSize}_{_Hex(text.color)}";
                case Slot.Rect:
                case Slot.TextRect:
                    var rt = (RectTransform)c;
                    return $"Rect_Tab_{Pascal(c.name)}_{_Num(rt.rect.width)}x{_Num(rt.rect.height)}";
                case Slot.Image:
                    var image = (Image)c;
                    return $"Img_Tab_{(image.sprite != null ? Pascal(image.sprite.name) : _Hex(image.color))}";
                default:
                    return _Name(slot, c, false);
            }
        }

        private static GameObject _Root(GameObject selected, bool imageTab)
        {
            if (imageTab)
            {
                var marker = selected.GetComponentInParent<GGImage>(true);
                if (marker != null) return marker.gameObject;
                if (selected.GetComponent<Image>() != null) return selected;
                var image = selected.GetComponentInParent<Image>(true);
                return image != null ? image.gameObject : selected;
            }
            var buttonMarker = selected.GetComponentInParent<GGButton>(true);
            if (buttonMarker != null) return buttonMarker.gameObject;
            var button = selected.GetComponentInParent<Button>(true);
            return button != null ? button.gameObject : selected;
        }

        private static string _Folder(Slot slot)
        {
            switch (slot)
            {
                case Slot.Rect:
                case Slot.TextRect: return "Rect";
                case Slot.Image: return "Image";
                case Slot.Text: return "Text";
                case Slot.Layout: return "Layout";
                default: return "Effect";
            }
        }

        private static string _Name(Slot slot, Component c, bool imageTab)
        {
            string owner = imageTab ? "Img" : "Btn";
            switch (slot)
            {
                case Slot.Rect:
                case Slot.TextRect:
                    var rt = (RectTransform)c;
                    return $"Rect_{(slot == Slot.TextRect ? "Txt" : owner)}_{_Num(rt.rect.width)}x{_Num(rt.rect.height)}";
                case Slot.Image:
                    var image = (Image)c;
                    return $"Img_{owner}_{(image.sprite != null ? Pascal(image.sprite.name) : _Hex(image.color))}";
                case Slot.Text:
                    var text = (Text)c;
                    return $"Txt_Btn_{text.fontSize}_{_Hex(text.color)}";
                default:
                    var effect = (Shadow)c;
                    return $"{effect.GetType().Name}_{_Hex(effect.effectColor)}_{_Num(effect.effectDistance.x)}x{_Num(effect.effectDistance.y)}";
            }
        }

        private static string _Num(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        private static string _Hex(Color c) =>
            ColorUtility.ToHtmlStringRGB(c) + (c.a < 0.995f ? Mathf.RoundToInt(c.a * 100).ToString() : "");

        /// <summary>"btn_join_server" → "BtnJoinServer".</summary>
        public static string Pascal(string s)
        {
            var sb = new StringBuilder();
            foreach (var part in s.Split('_', '-', ' ', '.'))
            {
                if (part.Length == 0) continue;
                sb.Append(char.ToUpperInvariant(part[0])).Append(part, 1, part.Length - 1);
            }
            return sb.ToString();
        }
    }
}
