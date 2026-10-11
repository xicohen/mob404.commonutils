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
            public readonly List<string> diffs = new List<string>();
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
                    var created = MobUIToolPresetFactory.SaveTabPreset(config, s.kind, s.component, s.rootRect, out string msg);
                    s.set(created);
                    log.Add($"{s.label}: {reason} → {msg}");
                    continue;
                }

                var excluded = s.preset.excludedProperties ?? new string[0];
                s.preset.UpdateProperties(s.component);
                if (s.component is RectTransform && !s.rootRect)
                    excluded = _With(excluded, MobUIToolPresetFactory.ChildRectExcluded);
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

        private static Slot _Slot(string label, Component c, Preset preset, Action<Preset> set, MobUIToolPresetFactory.Slot kind, bool rootRect = false) =>
            new Slot { label = label, component = c, preset = MobUIToolApplier.IsMissing(preset) ? null : preset, set = set, kind = kind, rootRect = rootRect };

        private static void _Diff(Slot s)
        {
            if (s.isEffect)
            {
                if (s.preset == null && s.component != null) { s.diffs.Add($"Title có {s.component.GetType().Name}, ô preset trống"); return; }
                if (s.preset != null && s.component == null) { s.diffs.Add("Title không có Outline/Shadow, ô preset có"); return; }
                if (s.preset == null) return;
                if (!MobUIToolApplier.IsPresetFor(s.preset, s.component.GetType())) { s.diffs.Add($"Title dùng {s.component.GetType().Name}, preset là {s.preset.GetTargetTypeName()}"); return; }
            }
            if (s.component == null) return;
            if (s.preset == null) { s.diffs.Add("ô preset trống"); return; }
            s.diffs.AddRange(MobUIToolApplier.Differences(s.preset, s.component));
        }

        private static string[] _With(string[] list, string[] add)
        {
            var l = new List<string>(list);
            foreach (var a in add) if (!l.Contains(a)) l.Add(a);
            return l.ToArray();
        }
    }
}
