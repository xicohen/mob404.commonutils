using UnityEngine;
using UnityEngine.UI;

namespace Mob404.Common.UITemplate
{
    /// <summary>
    /// Marker đánh dấu một button thuộc loại nào trong tool MobUITool (Tools/Mob404/MobUITool).
    /// Chỉ chứa dữ liệu, không có logic runtime — mọi thao tác style đều chạy trong Editor.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Mob404/GG Button")]
    public class GGButton : MonoBehaviour
    {
        public string typeId;
        public Image image;
        public Text text;

        [Tooltip("Icon giá (vd IconCost) — chỉ dùng với loại có khối Cost. Trống = tự nhận diện lúc Apply")]
        public Image costIcon;
        [Tooltip("Text giá nằm trong icon (vd TextCost) — chỉ dùng với loại có khối Cost. Trống = tự nhận diện lúc Apply")]
        public Text costText;
    }
}
