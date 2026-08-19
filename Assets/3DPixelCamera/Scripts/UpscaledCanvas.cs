namespace PixelCamera
{
    using UnityEngine;

    /// <summary>
    /// The upscaled canvas which displays the game camera render.
    /// </summary>
    [ExecuteInEditMode]
    public class UpscaledCanvas : MonoBehaviour
    {
        const string materialVariableName = "_LowResTexture";
        Material canvasMaterial;

        void OnEnable()
        {
            this.Initialize();
        }

        /// <summary>
        /// Initializes the upscaled canvas.
        /// </summary>
        void Initialize()
        {
            if (!this.TryGetComponent<MeshRenderer>(out var meshRenderer))
            {
                Debug.LogError("MeshRenderer is required!");
            }
            else
            {
                this.canvasMaterial = meshRenderer.sharedMaterial;
                if (this.canvasMaterial == null)
                {
                    Debug.LogError("canvasMaterial is null. Set it in the MeshRenderer!");
                }
            }

            if (this.transform.parent.localScale != Vector3.one)
            {
                Debug.LogWarning("The Upscaled Display localScale should be Vector3.one. Changing the local scale!");
                this.transform.parent.localScale = Vector3.one;
            }
        }

        /// <summary>
        /// If the material has a render texture defined.
        /// </summary>
        public bool MaterialHasRenderTexture => this.canvasMaterial.HasProperty(materialVariableName);

        /// <summary>
        /// Resize the canvas to the provided aspect and height.
        /// </summary>
        public void ResizeCanvas(float aspect, float canvasHeight)
        {
            this.transform.localScale = new Vector3(canvasHeight * aspect, canvasHeight, 1f);
        }

        /// <summary>
        /// Sets the canvas render texture.
        /// </summary>
        public void SetCanvasRenderTexture(RenderTexture renderTexture)
        {
            this.canvasMaterial.SetTexture(materialVariableName, renderTexture);
        }
    }
}
