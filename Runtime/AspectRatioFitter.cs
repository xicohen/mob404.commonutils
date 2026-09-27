using UnityEngine;

namespace Mob404.Common
{
    public class AspectRatioFitter : MonoBehaviour
    {
        public float Width = 1920;
        public float Height = 1080;

        private void Awake()
        {
            var aspectRatioFitter = this.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
            if (aspectRatioFitter != null)
            {
                aspectRatioFitter.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;
                aspectRatioFitter.aspectRatio = Width / Height;
            }
        }
    }
}
