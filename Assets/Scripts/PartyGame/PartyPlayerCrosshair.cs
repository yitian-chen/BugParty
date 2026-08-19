using UnityEngine;
using UnityEngine.UI;

namespace PartyGame
{
    /// <summary>
    /// Owner-side crosshair + transient head banner for the water gun. Renders on a dedicated
    /// ScreenSpace-Overlay canvas so it draws on top of the entire 3D scene, including other
    /// players' rafts. The crosshair follows the mouse; the banner floats above the player's
    /// head and is used for messages like "弹药耗尽". Both hidden while not playing.
    ///
    /// Non-owner instances / bots render nothing.
    /// </summary>
    [RequireComponent(typeof(PartyPlayer))]
    public class PartyPlayerCrosshair : MonoBehaviour
    {
        [SerializeField] private float pixelSize = 48f;
        [SerializeField] private Color color = new Color(1f, 0.35f, 0.35f, 0.95f);
        [SerializeField] private Vector3 headWorldOffset = new Vector3(0f, 2.5f, 0f);
        [SerializeField] private int bannerFontSize = 32;

        private PartyPlayer player;
        private GameObject canvasGO;
        private RectTransform reticleRT;
        private RawImage reticleImage;
        private Texture2D ringTexture;

        // Head-banner floating message (e.g. "弹药耗尽 请装填").
        private RectTransform bannerRT;
        private Text bannerText;
        private float bannerRemaining;
        private float bannerTotal;

        private void Awake()
        {
            player = GetComponent<PartyPlayer>();
        }

        private void OnDestroy()
        {
            if (canvasGO != null) Destroy(canvasGO);
            if (ringTexture != null) Destroy(ringTexture);
        }

        /// <summary>Show a transient banner over this player's head for `seconds`. Owner-only.</summary>
        public void ShowHeadBanner(string message, float seconds = 1.2f)
        {
            if (player == null || !player.IsLocalController || player.IsBot) return;
            EnsureCanvas();
            if (bannerText == null) return;
            bannerText.text = message;
            bannerRemaining = seconds;
            bannerTotal = seconds;
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

            UpdateBanner();
        }

        private void UpdateBanner()
        {
            if (bannerRT == null) return;
            if (bannerRemaining <= 0f)
            {
                if (bannerRT.gameObject.activeSelf) bannerRT.gameObject.SetActive(false);
                return;
            }

            if (!bannerRT.gameObject.activeSelf) bannerRT.gameObject.SetActive(true);
            bannerRemaining -= Time.deltaTime;
            float alpha = Mathf.Clamp01(bannerRemaining / Mathf.Max(0.01f, bannerTotal));
            var c = bannerText.color; c.a = Mathf.Clamp01(alpha * 1.5f); bannerText.color = c;

            var cam = GameWorldCamera.Resolve();
            if (cam == null) return;
            Vector3 headWorld = transform.position + headWorldOffset;
            // WorldToScreenPoint returns coordinates in the camera's target-texture space, which for the
            // pixel camera is 640x360 rather than the real screen. Convert via viewport (0..1) so the
            // banner lands correctly on the ScreenSpaceOverlay canvas at any real screen size.
            Vector3 vp = cam.WorldToViewportPoint(headWorld);
            if (vp.z <= 0f) { bannerRT.gameObject.SetActive(false); return; }
            bannerRT.position = new Vector3(vp.x * Screen.width, vp.y * Screen.height, 0f);
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

            var reticleGO = new GameObject("Reticle");
            reticleGO.transform.SetParent(canvasGO.transform, false);
            reticleRT = reticleGO.AddComponent<RectTransform>();
            reticleRT.sizeDelta = new Vector2(pixelSize, pixelSize);
            reticleImage = reticleGO.AddComponent<RawImage>();
            reticleImage.raycastTarget = false;
            ringTexture = BuildRingTexture(64, color);
            reticleImage.texture = ringTexture;
            reticleImage.color = Color.white;

            var bannerGO = new GameObject("HeadBanner");
            bannerGO.transform.SetParent(canvasGO.transform, false);
            bannerRT = bannerGO.AddComponent<RectTransform>();
            bannerRT.sizeDelta = new Vector2(420f, 60f);
            bannerText = bannerGO.AddComponent<Text>();
            bannerText.raycastTarget = false;
            bannerText.alignment = TextAnchor.MiddleCenter;
            bannerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            bannerText.fontSize = bannerFontSize;
            bannerText.fontStyle = FontStyle.Bold;
            bannerText.color = new Color(1f, 0.9f, 0.3f, 1f);
            // Outline for readability against varied backgrounds.
            var outline = bannerGO.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            bannerRT.gameObject.SetActive(false);
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

