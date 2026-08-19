using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Single source of truth for "which camera renders the game world".
    ///
    /// With the 3DPixelCamera integration, Camera.main points at the *view* camera that renders
    /// the pixelated quad — not the actual game-world camera. Any code that needs to project
    /// mouse coordinates into the world, or read the world-view direction, must use the
    /// PixelCameraManager's Camera. This helper hides that lookup behind one method.
    ///
    /// Falls back to Camera.main if no PixelCameraManager is present in the scene, so pre-pixel
    /// scenes (menu / lobby) still work.
    /// </summary>
    public static class GameWorldCamera
    {
        private static Camera cached;

        public static Camera Resolve()
        {
            if (cached != null && cached.enabled) return cached;
            // PixelCameraManager sits on the game camera itself.
            var mgr = Object.FindObjectOfType<PixelCamera.PixelCameraManager>();
            if (mgr != null)
            {
                cached = mgr.GetComponent<Camera>();
                if (cached != null) return cached;
            }
            cached = Camera.main;
            return cached;
        }

        public static void Invalidate() => cached = null;
    }
}
