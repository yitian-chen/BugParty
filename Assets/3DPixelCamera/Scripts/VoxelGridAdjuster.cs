namespace PixelCamera
{
    using UnityEngine;

    /// <summary>
    /// Adjusts the transform of this game object to a grid that ensures constant sampling by the camera.
    /// </summary>
    [ExecuteInEditMode]
    public class VoxelGridAdjuster : MonoBehaviour
    {
        /// <summary>
        /// Allows for full control of a transform while still locking an object to the camera grid.
        /// </summary>
        public Transform FollowedTransform;

        /// <summary>
        /// The pixel camera manager which decides the grid.
        /// </summary>
        PixelCameraManager pixelCameraManager;

        void OnEnable()
        {
            this.Initialize();
        }

        void LateUpdate()
        {
            this.transform.position = this.pixelCameraManager.PositionToGrid(this.FollowedTransform.position);
        }

        /// <summary>
        /// Initializes the adjuster.
        /// </summary>
        void Initialize()
        {
            if (this.pixelCameraManager == null)
            {
                this.pixelCameraManager = FindAnyObjectByType(typeof(PixelCameraManager)) as PixelCameraManager;
                if (this.pixelCameraManager == null)
                {
                    Debug.LogError("pixelCameraManager is null. Set it in editor!");
                }
            }
        }
    }
}