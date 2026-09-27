using UnityEngine;

namespace Mob404.Common
{
    // Dich chuyen anchoredPosition cua GameObject gan script theo Screen.safeArea,
    // dua tren huong anchor (AnchorType) da chon. Khong phu thuoc scene convention nao,
    // tu tim Canvas cha neu m_Canvas chua duoc gan.
    public class SafeAreaAdapter : MonoBehaviour
    {
        public Canvas m_Canvas;
        public AnchorType m_AnchorType = AnchorType.NONE;

        private void Start()
        {
            DoIt();
        }

        public void DoIt()
        {
            if (m_Canvas == null)
            {
                m_Canvas = GetComponentInParent<Canvas>();
                if (m_Canvas == null) return;
            }
            if (m_AnchorType == AnchorType.NONE)
                return;

            RectTransform canvasRect = m_Canvas.GetComponent<RectTransform>();
            Vector2 cvSize = new Vector2(canvasRect.rect.width, canvasRect.rect.height);

            CalculateSafePadding(cvSize, out float left, out float right, out float top, out float bottom);

            left *= 0.75f;
            right *= 0.75f;
            top *= 0.75f;
            bottom *= 0.75f;

            float xOffset = 0f;
            float yOffset = 0f;

            switch (m_AnchorType)
            {
                case AnchorType.TOP:
                    yOffset = -top;
                    break;
                case AnchorType.BOTTOM:
                    yOffset = bottom;
                    break;
                case AnchorType.LEFT:
                    xOffset = left;
                    break;
                case AnchorType.RIGHT:
                    xOffset = -right;
                    break;
                case AnchorType.TOP_LEFT:
                    xOffset = left;
                    yOffset = -top;
                    break;
                case AnchorType.TOP_RIGHT:
                    xOffset = -right;
                    yOffset = -top;
                    break;
                case AnchorType.BOTTOM_LEFT:
                    xOffset = left;
                    yOffset = bottom;
                    break;
                case AnchorType.BOTTOM_RIGHT:
                    xOffset = -right;
                    yOffset = bottom;
                    break;
            }

            RectTransform selfRect = GetComponent<RectTransform>();
            if (selfRect == null) return;
            selfRect.anchoredPosition += new Vector2(xOffset, yOffset);
        }

        private static void CalculateSafePadding(Vector2 canvasSize, out float left, out float right, out float top, out float bottom)
        {
            int screenW = Screen.width;
            int screenH = Screen.height;
            Rect safe = Screen.safeArea;

            float paddingLeftPx = safe.x;
            float paddingTopPx = safe.y;
            float paddingRightPx = screenW - paddingLeftPx - safe.width;
            float paddingBottomPx = screenH - paddingTopPx - safe.height;

            left = paddingLeftPx / screenW * canvasSize.x;
            right = paddingRightPx / screenW * canvasSize.x;
            top = paddingTopPx / screenH * canvasSize.y;
            bottom = paddingBottomPx / screenH * canvasSize.y;
        }
    }

    public enum AnchorType
    {
        NONE = 0,
        TOP_LEFT = 1,
        TOP_RIGHT = 2,
        BOTTOM_LEFT = 3,
        BOTTOM_RIGHT = 4,
        LEFT = 5,
        RIGHT = 6,
        TOP = 7,
        BOTTOM = 8
    }
}
