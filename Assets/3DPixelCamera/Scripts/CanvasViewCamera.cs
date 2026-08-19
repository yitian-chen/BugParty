namespace PixelCamera
{
    using UnityEngine;

    /// <summary>
    /// The canvas view camera. This smoothes the view on the pixelated game camera by adjusting its position relative to the upscaled display.
    /// </summary>
    [ExecuteInEditMode]
	public class CanvasViewCamera : MonoBehaviour
    {
        Camera canvasCamera;
        public float Aspect => this.canvasCamera.aspect;
        public float Zoom { get ; private set; }

        void OnEnable()
		{
            this.Zoom = -1;
            this.Initialize();
        }

        /// <summary>
        /// Initializes this component.
        /// </summary>
        void Initialize()
        {
            if (!this.TryGetComponent(out this.canvasCamera))
            {
                Debug.LogError("A camera component is required!");
            }
            else if (this.canvasCamera.orthographic == false)
            {
                Debug.LogWarning("The pixel camera system only works in orthographic camera mode. Changing the view camera to orthographic!");
                this.canvasCamera.orthographic = true;
            }
        }

        /// <summary>
        /// Adjusts the position on the display canvas to smooth movement.
        /// </summary>
        public void AdjustSubPixelPosition(Vector2 targetViewPosition, Vector2 canvasLocalScale)
		{
            var localPosition = (targetViewPosition - new Vector2(0.5f, 0.5f)) * canvasLocalScale;

            this.transform.localPosition = new Vector3(localPosition.x, localPosition.y, -1f);
        }

        /// <summary>
        /// Sets the orthographic zoom of this camera.
        /// </summary>
        public void SetZoom(float inputZoom, float halfCanvasHeight)
        {
            this.canvasCamera.orthographicSize = inputZoom * halfCanvasHeight;
            this.Zoom = inputZoom;
        }

        /// <summary>
        /// Sets the near and far clip planes of the view camera.
        /// </summary>
        public void SetClipPlanes(float near, float far)
        {
            this.canvasCamera.nearClipPlane = near;
            this.canvasCamera.farClipPlane = far;
        }
    }
}