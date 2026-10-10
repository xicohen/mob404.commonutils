using UnityEngine;
using UnityEngine.UI;

namespace Mob404.Common.UITemplate
{
    /// <summary>
    /// Marker đánh dấu một Image thuộc loại nào trong tab Image của tool MobUITool (Tools/Mob404/MobUITool).
    /// Chỉ chứa dữ liệu, không có logic runtime — mọi thao tác style đều chạy trong Editor.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Mob404/GG Image")]
    public class GGImage : MonoBehaviour
    {
        public string typeId;
        public Image image;
    }
}
