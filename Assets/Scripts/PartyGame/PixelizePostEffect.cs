using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Cheap camera post-processing that pixelates a camera's output. Blit the source into a
    /// low-resolution RenderTexture with Point filtering, then blit that RT back to the destination.
    /// The result is a hard, chunky pixelated look — no shader work, no dependencies.
    ///
    /// Intended for UI-heavy scenes (menu, lobby, HUD-only cameras) where the 3DPixelCamera's
    /// full RT / view-quad pipeline would be overkill. For the actual gameplay camera, keep
    /// using PixelCameraManager — this component and that one shouldn't be on the same camera.
    ///
    /// Attach to a Camera. Works in Built-in RP; Camera must have `allowHDR = false` for a stable
    /// look and the render target must not be already set on the camera.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class PixelizePostEffect : MonoBehaviour
    {
        [Tooltip("Target vertical resolution of the pixelated frame. Horizontal is scaled to match the current screen aspect. 360 = same density as the gameplay camera (640x360 for 16:9).")]
        [SerializeField] private int targetHeight = 360;

        [Tooltip("Minimum vertical resolution — guards against extreme downscales that would look like garbage on tiny render windows.")]
        [SerializeField] private int minTargetHeight = 90;

        private RenderTexture rt;
        private int lastW;
        private int lastH;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureAssetShader()
        {
            // Nothing to preload — Blit uses the built-in default shader. This method is here
            // as a hook if we ever swap to a custom material.
        }

        private void OnDisable()
        {
            if (rt != null)
            {
                rt.Release();
                DestroyImmediate(rt);
                rt = null;
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            int th = Mathf.Max(minTargetHeight, targetHeight);
            // Preserve the source aspect ratio so screen ratio changes don't stretch pixels.
            float aspect = source.height > 0 ? (float)source.width / source.height : 16f / 9f;
            int tw = Mathf.Max(minTargetHeight, Mathf.RoundToInt(th * aspect));

            if (rt == null || tw != lastW || th != lastH)
            {
                if (rt != null) { rt.Release(); DestroyImmediate(rt); }
                rt = new RenderTexture(tw, th, 0, source.format)
                {
                    // Point sampling is what gives us the hard pixel edges on the upscale.
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 1,
                    name = "PixelizePostEffect_RT"
                };
                rt.Create();
                lastW = tw;
                lastH = th;
            }

            var prevSrcFilter = source.filterMode;
            source.filterMode = FilterMode.Point; // avoid smoothing on the downsample
            Graphics.Blit(source, rt);
            source.filterMode = prevSrcFilter;

            var prevFilter = rt.filterMode;
            rt.filterMode = FilterMode.Point;
            Graphics.Blit(rt, destination);
            rt.filterMode = prevFilter;
        }
    }
}
