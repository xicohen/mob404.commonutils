using UnityEngine;
using UnityEngine.UI;

namespace Mob404.Common.UITemplate
{
    /// <summary>
    /// Marker đánh dấu một tab (GameObject có Button, hai nửa ON/OFF) thuộc loại nào trong tab "Tab" của MobUITool.
    /// Chỉ chứa dữ liệu, không có logic runtime — mọi thao tác style/cấu trúc đều chạy trong Editor.
    /// Ô để trống được tool tự tìm theo quy ước tên của loại (vd ImageOn/Image, ImageOn/Title).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Mob404/GG Tab")]
    public class GGTab : MonoBehaviour
    {
        public string typeId;
        [Tooltip("Term I2 Localization gắn cho Title của cả hai nửa ON/OFF (để trống = chỉ gắn Localize, chưa đặt term)")]
        public string term;
        public GameObject onRoot;
        public GameObject offRoot;
        public Image onImage;
        public Text onText;
        public Image offImage;
        public Text offText;
    }
}
