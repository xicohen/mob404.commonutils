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

        // Khối Cost (button kèm giá: icon tiền tệ + Text giá nằm trong icon). Icon preset hoặc Text preset là khoá của khối.
        // Loại có khối Cost không được Scan gắn tự động (trùng sprite với loại thường) — gắn marker bằng tay.
        [Tooltip("File .preset của RectTransform — áp lên RectTransform của icon giá")]
        public Preset costIconRectPreset;
        [Tooltip("File .preset của UnityEngine.UI.Image — áp lên icon giá, KHÔNG BAO GIỜ ghi sprite (đổi lúc chạy theo loại tiền)")]
        public Preset costIconPreset;
        [Tooltip("File .preset của RectTransform — áp lên RectTransform của Text giá")]
        public Preset costTextRectPreset;
        [Tooltip("File .preset của UnityEngine.UI.Text — áp lên Text giá (không bao giờ ghi nội dung chữ)")]
        public Preset costTextPreset;
        [Tooltip("File .preset của Outline HOẶC Shadow cho Text giá; ô trống = xoá cả hai")]
        public Preset costEffectPreset;
    }

    /// <summary>Loại Image: mọi Image dùng sprite nguồn mà chưa thuộc loại Button. Chỉ preset RectTransform/Image.</summary>
    [Serializable]
    public class GGImageType : GGTypeBase
    {
    }

    /// <summary>
    /// Loại tab (GameObject có Button, hai nửa ON/OFF — vd TabButton.m_GoOn / m_GoOff). Mỗi nửa gồm một node Image và
    /// một node Title (Text). Sync structure tạo node còn thiếu theo quy ước tên; rectTransformPreset/imagePreset của
    /// GGTypeBase áp lên GameObject gốc của tab.
    /// </summary>
    [Serializable]
    public class GGTabType : GGTypeBase
    {
        [Tooltip("File .preset của LayoutElement — áp lên GameObject gốc của tab (thiếu LayoutElement thì thêm)")]
        public Preset layoutElementPreset;

        [Tooltip("Tên node con của tab giữ trạng thái ĐANG CHỌN")] public string onName = "ImageOn";
        [Tooltip("Tên node con của tab giữ trạng thái CHƯA CHỌN")] public string offName = "ImageOff";
        [Tooltip("Tên node Image trong mỗi nửa ON/OFF")] public string imageName = "Image";
        [Tooltip("Tên node Text (Title) trong mỗi nửa ON/OFF")] public string titleName = "Title";

        public GGTabHalf on = new GGTabHalf();
        public GGTabHalf off = new GGTabHalf();

        [Tooltip("Xoá node/component cũ trong nửa ON/OFF không thuộc quy ước (vd Image trên ImageOn, node 'img'). Node có tham chiếu từ ngoài không bao giờ bị xoá.")]
        public bool removeLegacy;
        [Tooltip("Gắn I2 Localize lên Title của hai nửa, đặt term theo ô Term của marker")]
        public bool attachLocalize = true;
    }

    /// <summary>Preset của một nửa tab (ON hoặc OFF): node Image + node Title.</summary>
    [Serializable]
    public class GGTabHalf
    {
        public Preset imageRectPreset;
        public Preset imagePreset;
        public Preset textRectPreset;
        public Preset textPreset;
        [Tooltip("File .preset của Outline HOẶC Shadow — Title giữ đúng 1 component kiểu đó, xoá kiểu còn lại; ô trống = xoá cả hai")]
        public Preset effectPreset;
    }

    /// <summary>Prefab/scene chứa trực tiếp marker của một loại, kèm số marker trong file.</summary>
    public class GGLinkedAsset
    {
        public string path;
        public int count;
    }
}
