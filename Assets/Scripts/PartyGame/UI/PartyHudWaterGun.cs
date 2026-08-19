using UnityEngine;

namespace PartyGame.UI
{
    /// <summary>
    /// Zero-scene-setup IMGUI HUD for the local player's water gun.
    /// Renders 5 dots (filled/empty) and a reload progress bar when reloading.
    /// Attach to any GameObject in the game scene (the HUD Canvas is fine).
    /// </summary>
    public class PartyHudWaterGun : MonoBehaviour
    {
        [SerializeField] private int localPlayerIndex = 0;

        private PartyPlayer bound;

        private void Update()
        {
            if (bound != null) return;
            var players = FindObjectsOfType<PartyPlayer>();
            foreach (var p in players)
            {
                if (p.PlayerIndex == localPlayerIndex && p.IsLocalController && !p.IsBot)
                {
                    bound = p;
                    return;
                }
            }
        }

        private void OnGUI()
        {
            if (bound == null) return;
            if (PartyGameManager.Instance == null || !PartyGameManager.Instance.IsGamePlaying()) return;

            int clip = bound.WaterClipSize;
            int ammo = Mathf.Clamp(bound.WaterAmmo, 0, clip);
            bool reloading = bound.WaterReloading;
            float reloadN = bound.WaterReloadNormalized;

            // Position: bottom-left corner.
            float dotSize = 22f;
            float gap = 8f;
            float x0 = 30f;
            float y0 = Screen.height - 90f;

            GUI.Label(new Rect(x0, y0 - 24f, 200f, 22f), "水枪 (LMB 射 / RMB 装填)");

            for (int i = 0; i < clip; i++)
            {
                Rect r = new Rect(x0 + i * (dotSize + gap), y0, dotSize, dotSize);
                GUI.color = i < ammo ? new Color(0.35f, 0.7f, 1f, 1f) : new Color(1f, 1f, 1f, 0.25f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
            }
            GUI.color = Color.white;

            if (reloading)
            {
                float barW = clip * (dotSize + gap) - gap;
                Rect bg = new Rect(x0, y0 + dotSize + 6f, barW, 8f);
                GUI.color = new Color(0, 0, 0, 0.4f);
                GUI.DrawTexture(bg, Texture2D.whiteTexture);
                Rect fg = new Rect(bg.x, bg.y, bg.width * reloadN, bg.height);
                GUI.color = new Color(0.9f, 0.9f, 0.2f, 1f);
                GUI.DrawTexture(fg, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(x0, bg.y + 12f, 200f, 20f), $"装填中… {(int)(reloadN * 100)}%");
            }
        }
    }
}
