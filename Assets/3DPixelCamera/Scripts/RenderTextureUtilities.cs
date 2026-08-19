namespace PixelCamera
{
    using UnityEngine;

    // How the game resolution is synchronized for different aspect ratios.
    public enum ResolutionSynchronizationMode
    {
        SetHeight,
        SetWidth,
        SetBoth
    }

    /// <summary>
    /// Utilities to help with creating render textures.
    /// </summary>
    public static class RenderTextureUtilities
    {
        /// <summary>
        /// Gets the texture resolution with the aspect ratio and resolution.
        /// </summary>
        public static Vector2Int TextureResultion(float aspect, Vector2Int resolution, ResolutionSynchronizationMode resolutionSynchronizationMode)
        {
            return resolutionSynchronizationMode switch
            {
                ResolutionSynchronizationMode.SetHeight => new Vector2Int(Mathf.RoundToInt(resolution.y * aspect), resolution.y),
                ResolutionSynchronizationMode.SetWidth => new Vector2Int(resolution.x, Mathf.RoundToInt(resolution.x / aspect)),
                ResolutionSynchronizationMode.SetBoth => new Vector2Int(resolution.x, resolution.y),
                _ => Vector2Int.one,
            };
        }

        /// <summary>
        /// Creates a new render texture of the correct size.
        /// </summary>
        /// <returns>The created render texture and null if failed.</returns>
        public static RenderTexture CreateRenderTexture(Vector2Int textureSize)
        {
            var newTexture = new RenderTexture(textureSize.x, textureSize.y, 32, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Point
            };

            if (newTexture.Create())
            {
                return newTexture;
            }
            else
            {
                return null;
            }
        }
    }
}
