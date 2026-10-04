using UnityEngine;

namespace Mob404.Common
{
    // Gan len RectTransform stretch full cha. Giu vung noi dung nam trong khung Width x Height
    // (vd kich thuoc background): man rong hon khung -> chua 2 ben, man cao hon khung -> chua tren duoi,
    // con lai phu kin cha. aspectRatio = min(chaW, Width) / min(chaH, Height), tinh lai moi khi kich thuoc cha doi.
    public class AspectRatioFitter : MonoBehaviour
    {
        public float Width = 1920;
        public float Height = 1080;

        private UnityEngine.UI.AspectRatioFitter m_Fitter;

        private void Awake()
        {
            m_Fitter = this.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
            m_Fitter.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;
            UpdateAspectRatio();
        }

        // Cha (Canvas) doi kich thuoc -> rect cua object stretch nay cung doi -> Unity goi ham nay
        private void OnRectTransformDimensionsChange()
        {
            UpdateAspectRatio();
        }

        private void UpdateAspectRatio()
        {
            if (m_Fitter == null) return;
            RectTransform parent = this.transform.parent as RectTransform;
            if (parent == null) return;

            Vector2 parentSize = parent.rect.size;
            if (parentSize.x <= 0 || parentSize.y <= 0) return;

            float ratio = Mathf.Min(parentSize.x, Width) / Mathf.Min(parentSize.y, Height);
            if (!Mathf.Approximately(m_Fitter.aspectRatio, ratio))
                m_Fitter.aspectRatio = ratio;
        }
    }
}
