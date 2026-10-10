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
        public enum Slot { Rect, Image, TextRect, Text, Effect }

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
            // Preset Rect mặc định không mang vị trí — áp hàng loạt mà kèm vị trí sẽ dời mọi đối tượng về một chỗ.
            if (component is RectTransform) preset.excludedProperties = MobUIToolApplier.PositionProperties;

            string folder = $"{System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(config)).Replace('\\', '/')}/Presets/{_Folder(slot)}";
            MobUIToolConfig.EnsureFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{_Name(slot, component, imageTab)}.preset");
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            message = $"Đã tạo {path} từ '{component.name}'.";
            return preset;
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
