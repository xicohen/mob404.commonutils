using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Mob404.Common.UITemplate.Editor
{
    /// <summary>
    /// Áp một loại Tab lên một tab: đồng bộ cấu trúc (tạo node Image/Title còn thiếu trong mỗi nửa ON/OFF, xoá node cũ
    /// khi loại bật removeLegacy), áp preset gốc + từng nửa, gắn I2 Localize cho Title. Dùng chung cờ DryRun/UseUndo/Warnings
    /// của MobUIToolApplier.
    /// </summary>
    public static class MobUIToolTabApplier
    {
        // I2 nằm trong Assembly-CSharp của project — package không tham chiếu trực tiếp được nên tìm bằng tên.
        private const string LOCALIZE_TYPE = "I2.Loc.Localize";
        private const string LOCALIZE_TERM_FIELD = "mTerm";

        private static Type s_LocalizeType;
        private static bool s_LocalizeSearched;

        /// <summary>Áp loại lên tab. roots = gốc của scene/prefab đang xử lý (để kiểm tham chiếu từ ngoài trước khi xoá node cũ).</summary>
        public static bool ApplyToTab(GGTab marker, GGTabType type, GameObject[] roots)
        {
            bool changed = false;

            var rootImage = marker.GetComponent<Image>();
            if (rootImage != null && MobUIToolApplier.HasValues(type.imagePreset, typeof(Image)) &&
                MobUIToolApplier._ApplyPreset(type.imagePreset, rootImage))
                changed = true;
            if (marker.transform is RectTransform rt && MobUIToolApplier.HasValues(type.rectTransformPreset, typeof(RectTransform)) &&
                MobUIToolApplier._ApplyRect(type.rectTransformPreset, rt))
                changed = true;
            if (_ApplyLayoutElement(marker.gameObject, type.layoutElementPreset)) changed = true;

            if (_Half(marker, type, true, roots)) changed = true;
            if (_Half(marker, type, false, roots)) changed = true;
            return changed;
        }

        /// <summary>Tìm tab gốc (GameObject có Button) chứa object đang chọn.</summary>
        public static GameObject TabRoot(GameObject selected)
        {
            if (selected == null) return null;
            var marker = selected.GetComponentInParent<GGTab>(true);
            if (marker != null) return marker.gameObject;
            var button = selected.GetComponentInParent<Button>(true);
            return button != null ? button.gameObject : null;
        }

        private static bool _ApplyLayoutElement(GameObject go, UnityEditor.Presets.Preset preset)
        {
            if (!MobUIToolApplier.HasValues(preset, typeof(LayoutElement))) return false;
            var element = go.GetComponent<LayoutElement>();
            if (element == null)
            {
                if (MobUIToolApplier.DryRun) return true;
                element = MobUIToolApplier.UseUndo ? Undo.AddComponent<LayoutElement>(go) : go.AddComponent<LayoutElement>();
                MobUIToolApplier._ApplyPreset(preset, element);
                EditorUtility.SetDirty(go);
                return true;
            }
            return MobUIToolApplier._ApplyPreset(preset, element);
        }

        private static bool _Half(GGTab marker, GGTabType type, bool isOn, GameObject[] roots)
        {
            var half = isOn ? type.on : type.off;
            string label = isOn ? "ON" : "OFF";
            string halfName = isOn ? type.onName : type.offName;

            var halfRoot = isOn ? marker.onRoot : marker.offRoot;
            if (halfRoot == null)
            {
                var t = marker.transform.Find(halfName);
                if (t != null) halfRoot = t.gameObject;
            }
            if (halfRoot == null)
            {
                MobUIToolApplier.Warnings.Add($"'{marker.name}': không có node '{halfName}' — bỏ qua nửa {label}.");
                return false;
            }

            bool changed = false;

            // 1. Cấu trúc: node Image + node Title.
            var imageGo = _Child(halfRoot, type.imageName);
            if (imageGo == null)
            {
                changed = true;
                if (MobUIToolApplier.DryRun) MobUIToolApplier.Warnings.Add($"'{marker.name}/{halfRoot.name}': sẽ tạo node '{type.imageName}'.");
                else imageGo = _Create(halfRoot, type.imageName, typeof(Image), 0);
            }
            var titleGo = _Child(halfRoot, type.titleName);
            if (titleGo == null)
            {
                changed = true;
                if (MobUIToolApplier.DryRun) MobUIToolApplier.Warnings.Add($"'{marker.name}/{halfRoot.name}': sẽ tạo node '{type.titleName}'.");
                else titleGo = _Create(halfRoot, type.titleName, typeof(Text), 1);
            }

            var image = imageGo != null ? _Ensure<Image>(imageGo, marker.name, ref changed) : null;
            var text = titleGo != null ? _Ensure<Text>(titleGo, marker.name, ref changed) : null;

            // 2. Node/component cũ không thuộc quy ước.
            if (_Legacy(halfRoot, type, roots, marker.name)) changed = true;

            // 3. Preset.
            if (image != null)
            {
                if (MobUIToolApplier.HasValues(half.imageRectPreset, typeof(RectTransform)) &&
                    MobUIToolApplier._ApplyRect(half.imageRectPreset, image.rectTransform))
                    changed = true;
                if (MobUIToolApplier.HasValues(half.imagePreset, typeof(Image)) && MobUIToolApplier._ApplyPreset(half.imagePreset, image))
                    changed = true;
            }
            // Text preset là khoá của khối Text (giống tab Button): không có thì bỏ qua Rect Text + Outline/Shadow.
            if (text != null && MobUIToolApplier.IsPresetFor(half.textPreset, typeof(Text)))
            {
                if (MobUIToolApplier.HasValues(half.textRectPreset, typeof(RectTransform)) &&
                    MobUIToolApplier._ApplyRect(half.textRectPreset, text.rectTransform))
                    changed = true;
                if (MobUIToolApplier.HasValues(half.textPreset, typeof(Text)) && MobUIToolApplier._ApplyPreset(half.textPreset, text))
                    changed = true;
                if (MobUIToolApplier._SyncEffect(half.effectPreset, text)) changed = true;
            }

            // 4. I2 Localize.
            if (text != null && type.attachLocalize && _Localize(text, marker.term, marker.name)) changed = true;

            // 5. Lưu tham chiếu vào marker.
            if (_SetRefs(marker, isOn, halfRoot, image, text)) changed = true;
            return changed;
        }

        private static GameObject _Child(GameObject parent, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var t = parent.transform.Find(name);
            return t != null ? t.gameObject : null;
        }

        /// <summary>Tạo node con giãn kín cha (preset Rect sẽ đặt lại kích thước/vị trí).</summary>
        private static GameObject _Create(GameObject parent, string name, Type graphicType, int siblingIndex)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = parent.layer };
            if (MobUIToolApplier.UseUndo) Undo.RegisterCreatedObjectUndo(go, "MobUITool create " + name);
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            rt.SetSiblingIndex(Mathf.Min(siblingIndex, parent.transform.childCount - 1));
            go.AddComponent(graphicType);
            EditorUtility.SetDirty(parent);
            return go;
        }

        private static T _Ensure<T>(GameObject go, string owner, ref bool changed) where T : Graphic
        {
            var c = go.GetComponent<T>();
            if (c != null) return c;
            var other = go.GetComponent<Graphic>();
            if (other != null)
            {
                MobUIToolApplier.Warnings.Add($"'{owner}/{go.name}': đã có {other.GetType().Name}, không thêm {typeof(T).Name} được.");
                return null;
            }
            changed = true;
            if (MobUIToolApplier.DryRun) return null;
            c = MobUIToolApplier.UseUndo ? Undo.AddComponent<T>(go) : go.AddComponent<T>();
            EditorUtility.SetDirty(go);
            return c;
        }

        /// <summary>
        /// Graphic trên chính nửa ON/OFF và node con ngoài quy ước. removeLegacy tắt → chỉ cảnh báo. Bật → xoá, trừ thứ
        /// có tham chiếu từ ngoài hoặc thuộc prefab lồng nhau.
        /// </summary>
        private static bool _Legacy(GameObject halfRoot, GGTabType type, GameObject[] roots, string owner)
        {
            var candidates = new List<Object>();
            foreach (var g in halfRoot.GetComponents<Graphic>()) candidates.Add(g);
            foreach (Transform child in halfRoot.transform)
                if (child.name != type.imageName && child.name != type.titleName) candidates.Add(child.gameObject);
            if (candidates.Count == 0) return false;

            var referenced = _ReferencedFromOutside(candidates, roots);
            bool changed = false;
            foreach (var c in candidates)
            {
                string label = c is GameObject go ? $"node '{halfRoot.name}/{go.name}'" : $"{c.GetType().Name} trên '{halfRoot.name}'";
                if (referenced.TryGetValue(c, out var by))
                {
                    MobUIToolApplier.Warnings.Add($"'{owner}': giữ {label} — có tham chiếu từ {by}.");
                    continue;
                }
                if (!type.removeLegacy)
                {
                    MobUIToolApplier.Warnings.Add($"'{owner}': {label} không thuộc cấu trúc mới — tick 'Remove legacy' để xoá.");
                    continue;
                }
                var component = c as Component;
                var target = component != null ? (Object)component : c;
                bool removable = component != null ? MobUIToolApplier._CanRemove(component)
                    : !PrefabUtility.IsPartOfPrefabInstance(c) || PrefabUtility.IsAddedGameObjectOverride((GameObject)c);
                if (!removable)
                {
                    MobUIToolApplier.Warnings.Add($"'{owner}': {label} thuộc prefab lồng nhau — xoá ở prefab nguồn.");
                    continue;
                }
                changed = true;
                MobUIToolApplier.Warnings.Add($"'{owner}': {(MobUIToolApplier.DryRun ? "sẽ xoá" : "đã xoá")} {label}.");
                if (MobUIToolApplier.DryRun) continue;
                if (MobUIToolApplier.UseUndo) Undo.DestroyObjectImmediate(target);
                else Object.DestroyImmediate(target);
            }
            if (changed && !MobUIToolApplier.DryRun) EditorUtility.SetDirty(halfRoot);
            return changed;
        }

        /// <summary>
        /// Ứng viên nào (GameObject — tính cả component/node con của nó — hoặc component) đang được MonoBehaviour khác
        /// trong file tham chiếu tới. Bỏ qua tham chiếu nằm trong chính ứng viên và marker GGTab.
        /// </summary>
        private static Dictionary<Object, string> _ReferencedFromOutside(List<Object> candidates, GameObject[] roots)
        {
            var owner = new Dictionary<Object, Object>();
            foreach (var c in candidates)
            {
                if (c is GameObject go)
                {
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    {
                        owner[t.gameObject] = c;
                        foreach (var comp in t.GetComponents<Component>())
                            if (comp != null) owner[comp] = c;
                    }
                }
                else owner[c] = c;
            }

            var result = new Dictionary<Object, string>();
            if (roots == null) return result;
            foreach (var root in roots)
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb is GGTab || owner.ContainsKey(mb)) continue;
                var it = new SerializedObject(mb).GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                    if (!owner.TryGetValue(it.objectReferenceValue, out var cand) || result.ContainsKey(cand)) continue;
                    result[cand] = $"{mb.name}.{mb.GetType().Name}.{it.propertyPath}";
                }
            }
            return result;
        }

        private static bool _Localize(Text text, string term, string owner)
        {
            var type = _LocalizeType();
            if (type == null)
            {
                MobUIToolApplier.Warnings.Add($"'{owner}': không tìm thấy {LOCALIZE_TYPE} trong project — bỏ qua Localize.");
                return false;
            }

            bool changed = false;
            var loc = text.GetComponent(type);
            if (loc == null)
            {
                changed = true;
                if (MobUIToolApplier.DryRun) return true;
                loc = MobUIToolApplier.UseUndo ? Undo.AddComponent(text.gameObject, type) : text.gameObject.AddComponent(type);
                EditorUtility.SetDirty(text.gameObject);
            }
            if (string.IsNullOrEmpty(term))
            {
                MobUIToolApplier.Warnings.Add($"'{owner}/{text.name}': chưa có term — điền ô Term trên marker GGTab.");
                return changed;
            }

            var so = new SerializedObject(loc);
            var prop = so.FindProperty(LOCALIZE_TERM_FIELD);
            if (prop == null || prop.stringValue == term) return changed;
            if (MobUIToolApplier.DryRun) return true;
            prop.stringValue = term;
            if (MobUIToolApplier.UseUndo) so.ApplyModifiedProperties();
            else so.ApplyModifiedPropertiesWithoutUndo();
            _Record(loc);
            return true;
        }

        private static Type _LocalizeType()
        {
            if (s_LocalizeSearched) return s_LocalizeType;
            s_LocalizeSearched = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                s_LocalizeType = asm.GetType(LOCALIZE_TYPE);
                if (s_LocalizeType != null) break;
            }
            return s_LocalizeType;
        }

        private static bool _SetRefs(GGTab marker, bool isOn, GameObject halfRoot, Image image, Text text)
        {
            var so = new SerializedObject(marker);
            bool changed = false;
            changed |= _SetRef(so, isOn ? nameof(GGTab.onRoot) : nameof(GGTab.offRoot), halfRoot);
            if (image != null) changed |= _SetRef(so, isOn ? nameof(GGTab.onImage) : nameof(GGTab.offImage), image);
            if (text != null) changed |= _SetRef(so, isOn ? nameof(GGTab.onText) : nameof(GGTab.offText), text);
            if (!changed || MobUIToolApplier.DryRun) return changed;
            if (MobUIToolApplier.UseUndo) so.ApplyModifiedProperties();
            else so.ApplyModifiedPropertiesWithoutUndo();
            _Record(marker);
            return true;
        }

        private static bool _SetRef(SerializedObject so, string field, Object value)
        {
            var p = so.FindProperty(field);
            if (p == null || p.objectReferenceValue == value) return false;
            p.objectReferenceValue = value;
            return true;
        }

        private static void _Record(Component c)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(c)) PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            EditorUtility.SetDirty(c);
        }
    }
}
