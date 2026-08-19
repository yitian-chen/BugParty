namespace PixelCamera
{
    using UnityEngine;

    /// <summary>
    /// Tooltips for Unity editor
    /// </summary>
    public static class Tooltips
    {
        public const string TT_FOLLOWED_TRANSFORM = "Transform that this camera follow while doing pixel perfect corrections. Very useful for camera controllers. Allows for full control of a transform.";

        public const string TT_GRID_MOVEMENT = "Stationary objects in 3D world look stationary without jittering colors or outlines. Camera moves on a voxel grid.";

        public const string TT_SUB_PIXEL = "Subpixel adjustments counter the blocky nature of moving along a grid.";

        public const string TT_FOLLOW_ROTATION = "Should the camera transform follow the followed transforms rotation as well as position.";

        public const string TT_GAME_RESOLUTION = "The resolution of the game render texture. Lower values look more pixelated.";

        public const string TT_RESOLUTION_SYNCHRONIZATION_MODE = "How 'GameResolution' should be calculated. Synchronization ensures consistent pixelation for different device resolutions and artstyles.";

        public const string TT_CONTROL_GAME_ZOOM = "Should this script control the game cameras orthographic size. This can be turned off if another controller is already controlling it to avoid unexpected behaviour.";

        public const string TT_GAME_ZOOM = "The resolution of the game appears to stay the same but everything becomes more detailed while zooming in. The game cameras orhtographic size.";

        public const string TT_VIEW_ZOOM = "Zoom where pixels stay a constant size and the view camera zooms in. The view cameras orhtographic size.";
    }

    /// <summary>
    /// The main script that manges the pixel camera system.
    /// </summary>
    [ExecuteInEditMode]
    public class PixelCameraManager : MonoBehaviour
    {
        [Tooltip(Tooltips.TT_FOLLOWED_TRANSFORM)] public Transform FollowedTransform;

        [Header("Settings")]
        [Tooltip(Tooltips.TT_GRID_MOVEMENT)] public bool VoxelGridMovement = true;
        [Tooltip(Tooltips.TT_SUB_PIXEL)] public bool SubpixelAdjustments = true;
        [Tooltip(Tooltips.TT_FOLLOW_ROTATION)] public bool FollowRotation = true;

        [Header("Resolution")]
        [Tooltip(Tooltips.TT_RESOLUTION_SYNCHRONIZATION_MODE)] public ResolutionSynchronizationMode resolutionSynchronizationMode = ResolutionSynchronizationMode.SetHeight;
        public Vector2Int GameResolution = new Vector2Int(640, 360);

        [Header("Zoom")]
        [Tooltip(Tooltips.TT_CONTROL_GAME_ZOOM)] public bool ControlGameZoom = true;
        [Tooltip(Tooltips.TT_GAME_ZOOM)] public float GameCameraZoom = 5f;
        [Tooltip(Tooltips.TT_VIEW_ZOOM)][Range(-1f, 1f)] public float ViewCameraZoom = 1f;

        Camera gameCamera;
        CanvasViewCamera viewCamera;
        UpscaledCanvas upscaledCanvas;

        float renderTextureAspect;

        void OnEnable()
        {
            this.Initialize();
        }

        void LateUpdate()
        {
            this.UpdateCameraSystem();
        }

        /// <summary>
        /// How tall and wide a pixel on the camera is in world units.
        /// </summary>
        float PixelWorldSize => 2f * this.gameCamera.orthographicSize / this.gameCamera.pixelHeight;

        /// <summary>
        /// Gets the target texture resolution and Vector2Int.left if none is defined.
        /// </summary>
        Vector2Int TargetTextureResolution => this.gameCamera.targetTexture == null ? Vector2Int.left : new Vector2Int(this.gameCamera.targetTexture.width, this.gameCamera.targetTexture.height);

        /// <summary>
        /// Rounds a vector3 to the closest camera pixel sized voxel.
        /// </summary>
        public Vector3 PositionToGrid(Vector3 worldPosition)
        {
            var localPosition = this.transform.InverseTransformDirection(worldPosition);
            var localPositionInPixels = localPosition / this.PixelWorldSize;
            var integerMovement = (Vector3)Vector3Int.RoundToInt(localPositionInPixels);
            var movement = integerMovement * this.PixelWorldSize;
            return (movement.x * this.transform.right)
                 + (movement.y * this.transform.up)
                 + (movement.z * this.transform.forward);
        }

        /// <summary>
        /// Sets the game camera zoom with some checks as orthographic cameras can not tolerate size = 0.
        /// </summary>
        float SetGameZoom(float zoom)
        {
            var checkedZoom = Mathf.Approximately(zoom, 0f) ? 0.01f : zoom;
            this.gameCamera.orthographicSize = checkedZoom;
            return checkedZoom;
        }
        /// <summary>
        /// Synchronizes the clip planes. The view camera offset should be taken into account.
        /// </summary>
        void SynchronizeClipPlanes()
        {
            this.viewCamera.SetClipPlanes(0f, this.gameCamera.farClipPlane - this.viewCamera.transform.localPosition.z);
        }

        /// <summary>
        /// Initializes the camera system.
        /// </summary>
        private void Initialize()
        {
            if (this.gameCamera == null)
            {
                if (!this.TryGetComponent(out this.gameCamera))
                {
                    Debug.LogError("Camera is null. Attach required component!");
                }
            }

            if (this.viewCamera == null)
            {
                this.viewCamera = FindAnyObjectByType(typeof(CanvasViewCamera)) as CanvasViewCamera;
                if (this.viewCamera == null)
                {
                    Debug.LogError("viewCamera is null. Set it in editor!");
                }
            }

            if (this.upscaledCanvas == null)
            {
                this.upscaledCanvas = FindAnyObjectByType(typeof(UpscaledCanvas)) as UpscaledCanvas;
                if (this.upscaledCanvas == null)
                {
                    Debug.LogError("upscaledCanvas is null. Set it in editor!");
                }
            }

            if (this.transform.parent == null)
            {
                Debug.LogError("There is no parent object! Are you sure this is setup correctly. Check the prefab!");
            }

            if (this.transform.parent.childCount > 2)
            {
                Debug.LogWarning("Pixel Camera Managers parent should only have 2 child objects. This and a transform that it follows.");
            }

            if (this.FollowedTransform == null)
            {
                Debug.LogError("Followed Transform is null. Create a empty sibling object and set in editor.");
            }

            this.SynchronizeClipPlanes();
        }

        /// <summary>
        /// Sets the render texture of the upscaled canvas and game camera.
        /// </summary>
        void SetRenderTexture(float aspect, RenderTexture newRenderTexture)
        {
            this.upscaledCanvas.SetCanvasRenderTexture(newRenderTexture);
            this.gameCamera.targetTexture = newRenderTexture;
            this.renderTextureAspect = aspect;
        }

        /// <summary>
        /// Updates the whole pixel camera system.
        /// </summary>
        void UpdateCameraSystem()
        {
            // On aspect change
            var aspectRatioChanged = this.renderTextureAspect != this.viewCamera.Aspect;
            var pixelResolutionChanged = this.GameResolution != this.TargetTextureResolution;
            var resizeCanvas = false;

            if (aspectRatioChanged || pixelResolutionChanged || this.gameCamera.targetTexture == null)
            {
                // Change fitting aspect ratio
                this.GameResolution = RenderTextureUtilities.TextureResultion(this.viewCamera.Aspect, this.GameResolution, this.resolutionSynchronizationMode);

                if (this.gameCamera.targetTexture != null)
                {
                    this.gameCamera.targetTexture.Release();
                }

                var newRenderTexture = RenderTextureUtilities.CreateRenderTexture(this.GameResolution);

                this.SetRenderTexture(this.viewCamera.Aspect, newRenderTexture);
                resizeCanvas = true;
            }
            else if (Application.isEditor && this.upscaledCanvas.MaterialHasRenderTexture)
            {
                this.SetRenderTexture(this.renderTextureAspect, this.gameCamera.targetTexture);
                resizeCanvas = true;
            }

            // Zooming
            var orthographicSizeChanged = this.gameCamera.orthographicSize != this.GameCameraZoom;
            if (!this.ControlGameZoom)
            {
                this.GameCameraZoom = this.gameCamera.orthographicSize;
                resizeCanvas = true;
            }

            if (this.ControlGameZoom && orthographicSizeChanged)
            {
                this.GameCameraZoom = this.SetGameZoom(this.GameCameraZoom);
                resizeCanvas = true;
            }

            if (orthographicSizeChanged || pixelResolutionChanged || this.ViewCameraZoom != this.viewCamera.Zoom)
            {
                var canvasOnScreenLimit = 1 - (2f / this.GameResolution.y); // This ensures that the view camera doesn't move over the canvas edge
                if (this.GameResolution.y < 3)
                {
                    canvasOnScreenLimit = 1f;
                    Debug.LogWarning("The resolution is too small, this might lead to unexpected behaviour");
                }

                this.ViewCameraZoom = Mathf.Approximately(this.ViewCameraZoom, 0f) ? 0.01f : Mathf.Clamp(this.ViewCameraZoom, -1, 1f);
                this.viewCamera.SetZoom(this.ViewCameraZoom * canvasOnScreenLimit, this.GameCameraZoom);
            }

            // Resize canvas if aspect changed or camera orthographic size changed
            if (resizeCanvas)
            {
                var gameResolutionAspect = (float)this.GameResolution.x / this.GameResolution.y;
                this.upscaledCanvas.ResizeCanvas(gameResolutionAspect, this.GameCameraZoom * 2f);
            }

            // Transform updates
            if (this.FollowRotation)
            {
                this.transform.rotation = this.FollowedTransform.rotation;
            }

            this.transform.position = this.VoxelGridMovement ? this.PositionToGrid(this.FollowedTransform.position) : this.FollowedTransform.position;

            if (this.SubpixelAdjustments)
            {
                var targetViewPosition = this.gameCamera.WorldToViewportPoint(this.FollowedTransform.position);
                this.viewCamera.AdjustSubPixelPosition(targetViewPosition, this.upscaledCanvas.transform.localScale);
            }
        }
    }
}
