using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;
using UnityEngine.UI;

namespace Mob404.Common.UITemplate.Editor
{
    /// <summary>
    /// Phát hiện chỗ một tab đang khác preset của loại (kể cả bật/tắt component) và lưu ngược giá trị của tab vào preset —
    /// để người dùng chỉnh thẳng trên tab rồi bấm một nút, không phải tạo/chọn preset bằng tay.
    /// </summary>
    public static class MobUIToolTabCapture
    {
        public class Slot
        {
            public string label;
            public Component component;
            public Preset preset;
            public Action<Preset> set;
            public MobUIToolPresetFactory.Slot kind;
            public bool rootRect;
            public bool isEffect;
            /// <summary>Icon giá: so/lưu không tính m_Sprite.</summary>
            public bool excludeSprite;
            /// <summary>true = ô của loại Tab (đặt tên Rect_Tab_…, Rect node con giữ vị trí); false = loại Button.</summary>
            public bool tab = true;
            /// <summary>Ô của loại Block: tên file theo owner (tên loại), Rect gốc bỏ cả neo/pivot.</summary>
            public bool block;
            public string owner;
            public readonly List<string> diffs = new List<string>();
        }

        /// <summary>
        /// Loại Block: Rect/Image gốc + Rect/Image/Text/Outline-Shadow của từng phần con tìm theo path. Phần chưa có node
        /// (Apply sẽ tạo/dời) thì bỏ qua. Không tính bật/tắt.
        /// </summary>
        public static List<Slot> BuildBlock(GGBlock marker, GGBlockType type)
        {
            var slots = new List<Slot>();
            slots.Add(_Slot("Gốc — Rect", marker.transform as RectTransform, type.rectTransformPreset, p => type.rectTransformPreset = p, MobUIToolPresetFactory.Slot.Rect, true));
            slots.Add(_Slot("Gốc — Image", marker.GetComponent<Image>(), type.imagePreset, p => type.imagePreset = p, MobUIToolPresetFactory.Slot.Image));
            foreach (var part in type.parts)
            {
                if (part == null || string.IsNullOrEmpty(part.path)) continue;
                var node = marker.transform.Find(part.path) as RectTransform;
                if (node == null) continue;
                var text = node.GetComponent<Text>();
                slots.Add(_Slot($"{part.path} — Rect", node, part.rectPreset, p => part.rectPreset = p, MobUIToolPresetFactory.Slot.Rect));
                slots.Add(_Slot($"{part.path} — Image", node.GetComponent<Image>(), part.imagePreset, p => part.imagePreset = p, MobUIToolPresetFactory.Slot.Image));
                slots.Add(_Slot($"{part.path} — Text", text, part.textPreset, p => part.textPreset = p, MobUIToolPresetFactory.Slot.Text));
                if (text == null) continue;
                var es = _Slot($"{part.path} — Outline / Shadow", _Effect(text), part.effectPreset, p => part.effectPreset = p, MobUIToolPresetFactory.Slot.Effect);
                es.isEffect = true;
                slots.Add(es);
            }
            foreach (var s in slots)
            {
                s.tab = false;
                s.block = true;
                s.owner = type.name;
            }
            try
            {
                foreach (var s in slots) _Diff(s);
            }
            finally
            {
                MobUIToolApplier.CleanupTemp();
            }
            slots.RemoveAll(s => s.component == null && s.preset == null && !s.isEffect);
            return slots;
        }

        /// <summary>
        /// Loại Button: ô Rect Button / Image trên GameObject của marker; Text / Rect Text / Outline-Shadow trên Text con.
        /// Không tính bật/tắt (Button giữ quy tắc không đổi m_Enabled).
        /// </summary>
        public static List<Slot> BuildButton(GGButton marker, GGButtonType type)
        {
            var slots = new List<Slot>();
            var image = marker.image != null ? marker.image : marker.GetComponent<Image>();
            var text = marker.text != null ? marker.text : MobUIToolScanner.FindText(marker.transform, out _);
            slots.Add(_Slot("Button — Rect", marker.transform as RectTransform, type.rectTransformPreset, p => type.rectTransformPreset = p, MobUIToolPresetFactory.Slot.Rect, true));
            slots.Add(_Slot("Button — Image", image, type.imagePreset, p => type.imagePreset = p, MobUIToolPresetFactory.Slot.Image));
            slots.Add(_Slot("Text", text, type.textPreset, p => type.textPreset = p, MobUIToolPresetFactory.Slot.Text));
            // Có Text thì so cả Rect Text + effect: lần Save đầu tạo đủ bộ (Text preset là khoá của khối khi Apply).
            if (text != null)
            {
                slots.Add(_Slot("Text — Rect", text.rectTransform, type.textRectTransformPreset, p => type.textRectTransformPreset = p, MobUIToolPresetFactory.Slot.TextRect));
                var es = _Slot("Text — Outline / Shadow", _Effect(text), type.effectPreset, p => type.effectPreset = p, MobUIToolPresetFactory.Slot.Effect);
                es.isEffect = true;
                slots.Add(es);
            }
            // Loại chưa có preset Cost nhưng marker đã gán tay ô Cost → vẫn so, để Save to preset tạo bộ preset Cost đầu tiên.
            if (MobUIToolApplier.HasCostKey(type) || marker.costIcon != null || marker.costText != null) _AddCost(slots, marker, type, text);
            foreach (var s in slots) s.tab = false;
            try
            {
                foreach (var s in slots) _Diff(s);
            }
            finally
            {
                MobUIToolApplier.CleanupTemp();
            }
            slots.RemoveAll(s => s.component == null && s.preset == null && !s.isEffect);
            return slots;
        }

        /// <summary>Khối Cost: Rect/Image của icon giá (không tính sprite) + Text/Rect/Outline-Shadow của Text giá.</summary>
        private static void _AddCost(List<Slot> slots, GGButton marker, GGButtonType type, Text mainText)
        {
            MobUIToolScanner.FindCost(marker.transform, mainText, out var foundIcon, out var foundText);
            var icon = marker.costIcon != null ? marker.costIcon : foundIcon;
            var costText = marker.costText != null ? marker.costText : foundText;
            slots.Add(_Slot("Cost — Rect Icon", icon != null ? icon.rectTransform : null, type.costIconRectPreset, p => type.costIconRectPreset = p, MobUIToolPresetFactory.Slot.Rect));
            var iconSlot = _Slot("Cost — Icon", icon, type.costIconPreset, p => type.costIconPreset = p, MobUIToolPresetFactory.Slot.Image);
            iconSlot.excludeSprite = true;
            slots.Add(iconSlot);
            slots.Add(_Slot("Cost — Text", costText, type.costTextPreset, p => type.costTextPreset = p, MobUIToolPresetFactory.Slot.Text));
            if (costText == null) return;
            slots.Add(_Slot("Cost — Rect Text", costText.rectTransform, type.costTextRectPreset, p => type.costTextRectPreset = p, MobUIToolPresetFactory.Slot.TextRect));
            var es = _Slot("Cost — Outline / Shadow", _Effect(costText), type.costEffectPreset, p => type.costEffectPreset = p, MobUIToolPresetFactory.Slot.Effect);
            es.isEffect = true;
            slots.Add(es);
        }

        private static Component _Effect(Text text)
        {
            foreach (var c in text.GetComponents<Shadow>())
                if (c.GetType() == typeof(Outline)) return c;
            foreach (var c in text.GetComponents<Shadow>())
                if (c.GetType() == typeof(Shadow)) return c;
            return null;
        }

        /// <summary>Mọi ô preset của loại, gắn với component tương ứng trên tab, kèm danh sách khác biệt.</summary>
        public static List<Slot> Build(GGTab marker, GGTabType type)
        {
            var slots = new List<Slot>();
            var rt = marker.transform as RectTransform;
            slots.Add(_Slot("Tab — Rect", rt, type.rectTransformPreset, p => type.rectTransformPreset = p, MobUIToolPresetFactory.Slot.Rect, true));
            slots.Add(_Slot("Tab — Image", marker.GetComponent<Image>(), type.imagePreset, p => type.imagePreset = p, MobUIToolPresetFactory.Slot.Image));
            slots.Add(_Slot("Tab — Layout Element", marker.GetComponent<LayoutElement>(), type.layoutElementPreset, p => type.layoutElementPreset = p, MobUIToolPresetFactory.Slot.Layout));
            _AddHalf(slots, marker, type, true);
            _AddHalf(slots, marker, type, false);
            foreach (var extra in type.extras) _AddExtra(slots, marker, type, extra);

            bool prev = MobUIToolApplier.IncludeEnabled;
            MobUIToolApplier.IncludeEnabled = true;
            try
            {
                foreach (var s in slots) _Diff(s);
            }
            finally
            {
                MobUIToolApplier.IncludeEnabled = prev;
                MobUIToolApplier.CleanupTemp();
            }
            slots.RemoveAll(s => s.component == null && s.preset == null && !s.isEffect);
            return slots;
        }

        public static int CountDiffs(List<Slot> slots)
        {
            int n = 0;
            foreach (var s in slots) n += s.diffs.Count;
            return n;
        }

        /// <summary>
        /// Lưu giá trị của tab vào preset cho mọi ô đang khác. Preset dùng chung với ô/loại khác → tách thành file mới cho ô
        /// này (không làm đổi ô kia). Trả về danh sách việc đã làm.
        /// </summary>
        public static List<string> Save(MobUIToolConfig config, List<Slot> slots)
        {
            var log = new List<string>();
            foreach (var s in slots)
            {
                if (s.diffs.Count == 0) continue;
                if (s.isEffect && s.component == null)
                {
                    s.set(null);
                    log.Add($"{s.label}: bỏ preset (Title không có Outline/Shadow).");
                    continue;
                }
                if (s.component == null) continue;

                bool typeMismatch = s.preset != null && s.isEffect && !MobUIToolApplier.IsPresetFor(s.preset, s.component.GetType());
                if (s.preset == null || typeMismatch || RefCount(config, s.preset) > 1)
                {
                    string reason = s.preset == null ? "ô trống" : typeMismatch ? "đổi kiểu effect" : "preset dùng chung — tách riêng";
                    string msg;
                    var created = s.block
                        ? MobUIToolPresetFactory.SaveBlockPreset(config, s.kind, s.component, s.rootRect, s.owner, out msg)
                        : s.tab
                            ? MobUIToolPresetFactory.SaveTabPreset(config, s.kind, s.component, s.rootRect, out msg)
                            : MobUIToolPresetFactory.SaveButtonPreset(config, s.kind, s.component, s.rootRect, out msg);
                    s.set(created);
                    log.Add($"{s.label}: {reason} → {msg}");
                    continue;
                }

                var excluded = s.preset.excludedProperties ?? new string[0];
                s.preset.UpdateProperties(s.component);
                if (s.component is RectTransform)
                    excluded = _With(excluded, !s.rootRect ? MobUIToolPresetFactory.ChildRectExcluded
                        : s.block ? MobUIToolPresetFactory.BlockRootExcluded : MobUIToolApplier.PositionProperties);
                s.preset.excludedProperties = excluded;
                EditorUtility.SetDirty(s.preset);
                AssetDatabase.SaveAssetIfDirty(s.preset);
                log.Add($"{s.label}: cập nhật {AssetDatabase.GetAssetPath(s.preset)} ({s.diffs.Count} thuộc tính).");
            }
            config.Save();
            return log;
        }

        /// <summary>Số ô preset (mọi loại Button/Image/Tab trong config) đang trỏ tới preset này.</summary>
        public static int RefCount(MobUIToolConfig config, Preset preset)
        {
            int n = 0;
            foreach (var t in config.types) n += _Count(t, preset);
            foreach (var t in config.imageTypes) n += _Count(t, preset);
            foreach (var t in config.tabTypes) n += _Count(t, preset);
            foreach (var t in config.blockTypes) n += _Count(t, preset);
            return n;
        }

        private static int _Count(object obj, Preset preset)
        {
            int n = 0;
            foreach (var f in obj.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType == typeof(Preset))
                {
                    if ((Preset)f.GetValue(obj) == preset) n++;
                }
                else if (f.FieldType == typeof(GGTabHalf))
                {
                    var half = f.GetValue(obj);
                    if (half != null) n += _Count(half, preset);
                }
                else if (f.FieldType == typeof(List<GGTabExtra>))
                {
                    var extras = (List<GGTabExtra>)f.GetValue(obj);
                    if (extras != null)
                        foreach (var e in extras)
                            if (e != null) n += _Count(e, preset);
                }
                else if (f.FieldType == typeof(List<GGBlockPart>))
                {
                    var parts = (List<GGBlockPart>)f.GetValue(obj);
                    if (parts != null)
                        foreach (var part in parts)
                            if (part != null) n += _Count(part, preset);
                }
            }
            return n;
        }

        private static void _AddHalf(List<Slot> slots, GGTab marker, GGTabType type, bool isOn)
        {
            var half = isOn ? type.on : type.off;
            string tag = isOn ? "ON" : "OFF";
            var halfRoot = isOn ? marker.onRoot : marker.offRoot;
            if (halfRoot == null)
            {
                var t = marker.transform.Find(isOn ? type.onName : type.offName);
                if (t != null) halfRoot = t.gameObject;
            }
            Image image = isOn ? marker.onImage : marker.offImage;
            Text text = isOn ? marker.onText : marker.offText;
            if (halfRoot != null)
            {
                if (image == null) { var t = halfRoot.transform.Find(type.imageName); if (t != null) image = t.GetComponent<Image>(); }
                if (text == null) { var t = halfRoot.transform.Find(type.titleName); if (t != null) text = t.GetComponent<Text>(); }
            }

            slots.Add(_Slot($"{tag} — Rect Image", image != null ? image.rectTransform : null, half.imageRectPreset, p => half.imageRectPreset = p, MobUIToolPresetFactory.Slot.Rect));
            slots.Add(_Slot($"{tag} — Image", image, half.imagePreset, p => half.imagePreset = p, MobUIToolPresetFactory.Slot.Image));
            slots.Add(_Slot($"{tag} — Text", text, half.textPreset, p => half.textPreset = p, MobUIToolPresetFactory.Slot.Text));
            slots.Add(_Slot($"{tag} — Rect Text", text != null ? text.rectTransform : null, half.textRectPreset, p => half.textRectPreset = p, MobUIToolPresetFactory.Slot.TextRect));

            Component effect = null;
            if (text != null)
            {
                foreach (var c in text.GetComponents<Shadow>())
                    if (c.GetType() == typeof(Outline)) { effect = c; break; }
                if (effect == null)
                    foreach (var c in text.GetComponents<Shadow>())
                        if (c.GetType() == typeof(Shadow)) { effect = c; break; }
            }
            var es = _Slot($"{tag} — Outline / Shadow", effect, half.effectPreset, p => half.effectPreset = p, MobUIToolPresetFactory.Slot.Effect);
            es.isEffect = text != null;
            slots.Add(es);
        }

        /// <summary>Phần phụ: chỉ so khi tab có node khớp tiền tố (tab không có thì không báo "ô preset trống" giả).</summary>
        private static void _AddExtra(List<Slot> slots, GGTab marker, GGTabType type, GGTabExtra extra)
        {
            if (extra == null || string.IsNullOrEmpty(extra.prefix)) return;
            var node = MobUIToolTabApplier.FindExtra(marker.transform, type, extra.prefix);
            if (node == null) return;
            slots.Add(_Slot($"{extra.prefix} — Rect", node, extra.rectPreset, p => extra.rectPreset = p, MobUIToolPresetFactory.Slot.Rect));
            slots.Add(_Slot($"{extra.prefix} — Image", node.GetComponent<Image>(), extra.imagePreset, p => extra.imagePreset = p, MobUIToolPresetFactory.Slot.Image));
        }

        /// <summary>
        /// Tiền tố chung của node phụ: so tên node đang chọn với node con của các tab anh em (vd NotifyAccessory, NotifyArmor,
        /// NotifyShoes → Notify), cắt về ranh giới từ PascalCase. Không có tab anh em hoặc tiền tố quá ngắn → dùng nguyên tên.
        /// </summary>
        public static string ExtraPrefix(Transform node)
        {
            var tab = node.parent;
            var group = tab != null ? tab.parent : null;
            if (group == null) return node.name;

            string prefix = node.name;
            bool found = false;
            foreach (Transform other in group)
            {
                if (other == tab || other.GetComponent<Button>() == null) continue;
                string best = null;
                foreach (Transform c in other)
                {
                    string common = _CommonPrefix(node.name, c.name);
                    if (best == null || common.Length > best.Length) best = common;
                }
                if (best == null || best.Length < 3) continue;
                prefix = _CommonPrefix(prefix, best);
                found = true;
            }
            // "NotifyA" (Accessory/Armor) → "Notify": lùi tới chỗ tên gốc bắt đầu một từ mới.
            while (prefix.Length > 0 && prefix.Length < node.name.Length)
            {
                char next = node.name[prefix.Length];
                if (char.IsUpper(next) || char.IsDigit(next) || next == '_') break;
                prefix = prefix.Substring(0, prefix.Length - 1);
            }
            return found && prefix.Length >= 3 ? prefix : node.name;
        }

        private static string _CommonPrefix(string a, string b)
        {
            int n = 0;
            while (n < a.Length && n < b.Length && a[n] == b[n]) n++;
            return a.Substring(0, n);
        }

        private static Slot _Slot(string label, Component c, Preset preset, Action<Preset> set, MobUIToolPresetFactory.Slot kind, bool rootRect = false) =>
            new Slot { label = label, component = c, preset = MobUIToolApplier.IsMissing(preset) ? null : preset, set = set, kind = kind, rootRect = rootRect };

        private static void _Diff(Slot s)
        {
            if (s.isEffect)
            {
                if (s.preset == null && s.component != null) { s.diffs.Add($"Text có {s.component.GetType().Name}, ô preset trống"); return; }
                if (s.preset != null && s.component == null) { s.diffs.Add("Text không có Outline/Shadow, ô preset có"); return; }
                if (s.preset == null) return;
                if (!MobUIToolApplier.IsPresetFor(s.preset, s.component.GetType())) { s.diffs.Add($"Text dùng {s.component.GetType().Name}, preset là {s.preset.GetTargetTypeName()}"); return; }
            }
            if (s.component == null) return;
            if (s.preset == null) { s.diffs.Add("ô preset trống"); return; }
            MobUIToolApplier.ExcludeSprite = s.excludeSprite;
            try
            {
                s.diffs.AddRange(MobUIToolApplier.Differences(s.preset, s.component));
            }
            finally
            {
                MobUIToolApplier.ExcludeSprite = false;
            }
        }

        private static string[] _With(string[] list, string[] add)
        {
            var l = new List<string>(list);
            foreach (var a in add) if (!l.Contains(a)) l.Add(a);
            return l.ToArray();
        }
    }
}
