using System;
using System.Collections.Generic;
using UnityEditor.Presets;
using UnityEngine;
using UnityEngine.Serialization;

namespace Mob404.Common.UITemplate.Editor
{
    /// <summary>Phần chung của loại Button và loại Image: sprite nguồn + preset RectTransform/Image.</summary>
    [Serializable]
    public class GGTypeBase
    {
        public string id;
        public string name;

        [Tooltip("Image dùng sprite này sẽ được gắn vào loại khi Scan")]
        public Sprite sourceSprite;

        [Tooltip("File .preset của RectTransform — áp lên RectTransform của đối tượng (Exclude Property những gì không muốn đổi, vd vị trí)")]
        public Preset rectTransformPreset;
        [Tooltip("File .preset của UnityEngine.UI.Image — áp lên Image của đối tượng")]
        public Preset imagePreset;

        // Cache dựng lại từ YAML (MobUIToolScanner.RefreshIndex) — không lưu vào config để tránh diff git mỗi lần quét.
        [NonSerialized] private List<GGLinkedAsset> _linkedAssets;

        /// <summary>Prefab/scene chứa trực tiếp marker của loại (không tính instance lồng nhau).</summary>
        public List<GGLinkedAsset> linkedAssets => _linkedAssets ??= new List<GGLinkedAsset>();

        public int TotalCount
        {
            get
            {
                int total = 0;
                foreach (var a in linkedAssets) total += a.count;
                return total;
            }
        }
    }

    /// <summary>
    /// Loại button (Image có Button cùng GameObject): thêm khối Text cho Text con. Text preset là khoá của khối —
    /// không có Text preset thì Rect Text và Outline/Shadow cũng không áp.
    /// </summary>
    [Serializable]
    public class GGButtonType : GGTypeBase
    {
        [Tooltip("File .preset của RectTransform — áp lên RectTransform của Text con (Exclude Property những gì không muốn đổi)")]
        public Preset textRectTransformPreset;
        [Tooltip("File .preset của UnityEngine.UI.Text — áp lên Text con của button (không bao giờ ghi nội dung chữ)")]
        public Preset textPreset;
        [Tooltip("File .preset của Outline HOẶC Shadow — Text con giữ đúng 1 component kiểu đó, xoá kiểu còn lại; ô trống = xoá cả hai")]
        [FormerlySerializedAs("outlinePreset")]
        public Preset effectPreset;
    }

    /// <summary>Loại Image: mọi Image dùng sprite nguồn mà chưa thuộc loại Button. Chỉ preset RectTransform/Image.</summary>
    [Serializable]
    public class GGImageType : GGTypeBase
    {
    }

    /// <summary>Prefab/scene chứa trực tiếp marker của một loại, kèm số marker trong file.</summary>
    public class GGLinkedAsset
    {
        public string path;
        public int count;
    }
}
