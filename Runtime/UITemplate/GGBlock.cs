using UnityEngine;

namespace Mob404.Common.UITemplate
{
    /// <summary>
    /// Marker đánh dấu một khối UI ghép (vd ô lực chiến: nền + icon + Text) thuộc loại nào trong tab "Block" của MobUITool.
    /// Chỉ chứa dữ liệu, không có logic runtime — node con của khối được tìm theo đường dẫn khai báo trong loại.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Mob404/GG Block")]
    public class GGBlock : MonoBehaviour
    {
        public string typeId;
    }
}
