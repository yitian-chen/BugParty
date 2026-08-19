using UnityEngine;
using UnityEngine.UI;

namespace PartyGame
{
    /// <summary>
    /// Owner-side crosshair for the water gun. Renders as a UI element on a dedicated
    /// ScreenSpace-Overlay canvas so it draws on top of the entire 3D scene, including
    /// other players' rafts. Follows the mouse position; hidden while not playing.
    ///
    /// Non-owner instances / bots render nothing.
    /// </summary>
    [RequireComponent(typeof(PartyPlayer))]
    public class PartyPlayerCrosshair : MonoBehaviour
    {
        [SerializeField] private float pixelSize = 48f;
        [SerializeField] private Color color = new Color(1f, 0.35f, 0.35f, 0.95f);

        private PartyPlayer player;
        private GameObject canvasGO;
        private RectTransform reticleRT;
        private RawImage reticleImage;
        private Texture2D ringTexture;

        private void Awake()
        {
            player = GetComponent<PartyPlayer>();
        }

        private void OnDestroy()
        {
            if (canvasGO != null) Destroy(canvasGO);
            if (ringTexture != null) Destroy(ringTexture);
        }

        private void LateUpdate()
        {
            if (player == null || !player.IsLocalController || player.IsBot)
            {
                SetActive(false);
                return;
            }
            if (PartyGameManager.Instance != null && !PartyGameManager.Instance.IsGamePlaying())
            {
                SetActive(false);
                return;
            }
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null)
            {
                SetActive(false);
                return;
            }
            EnsureCanvas();
            SetActive(true);
            Vector2 mp = mouse.position.ReadValue();
            reticleRT.position = new Vector3(mp.x, mp.y, 0f);
        }

        private void SetActive(bool v)
        {
            if (canvasGO != null && canvasGO.activeSelf != v) canvasGO.SetActive(v);
        }

        private void EnsureCanvas()
        {
            if (canvasGO != null) return;

            canvasGO = new GameObject($"WaterGunCrosshairCanvas_P{player.PlayerIndex}");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767; // above every gameplay HUD (PartyHUD is 0)
            canvasGO.AddComponent<CanvasScaler>();
            // No GraphicRaycaster — the crosshair should never eat UI clicks.

            var reticleGO = new GameObject("Reticle");
            reticleGO.transform.SetParent(canvasGO.transform, false);
            reticleRT = reticleGO.AddComponent<RectTransform>();
            reticleRT.sizeDelta = new Vector2(pixelSize, pixelSize);
            reticleImage = reticleGO.AddComponent<RawImage>();
            reticleImage.raycastTarget = false;
            ringTexture = BuildRingTexture(64, color);
            reticleImage.texture = ringTexture;
            reticleImage.color = Color.white;
        }

        private static Texture2D BuildRingTexture(int size, Color c)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            float outer = size * 0.48f;
            float inner = size * 0.32f;
            float tick = size * 0.06f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - size * 0.5f + 0.5f;
                    float dy = y - size * 0.5f + 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    bool inRing = d >= inner && d <= outer;
                    bool inCrosshair = (Mathf.Abs(dx) < tick && Mathf.Abs(dy) < outer)
                                     || (Mathf.Abs(dy) < tick && Mathf.Abs(dx) < outer);
                    Color px = (inRing || inCrosshair) ? c : new Color(0, 0, 0, 0);
                    tex.SetPixel(x, y, px);
                }
            }
            tex.Apply(false, true);
            return tex;
        }
    }
}
