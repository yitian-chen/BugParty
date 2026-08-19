using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Owner-side world-space crosshair for the water gun.
    /// Auto-spawns a small quad at the mouse-projected aim point every frame.
    /// Zero scene setup — a magenta ring (procedural texture) is created at runtime.
    ///
    /// Attach to the PartyPlayer prefab. Non-owner instances render nothing.
    /// </summary>
    [RequireComponent(typeof(PartyPlayer))]
    public class PartyPlayerCrosshair : MonoBehaviour
    {
        [SerializeField] private float size = 0.9f;
        [SerializeField] private Color color = new Color(1f, 0.4f, 0.4f, 0.9f);

        private PartyPlayer player;
        private GameObject reticle;
        private MeshRenderer reticleRenderer;

        private void Awake()
        {
            player = GetComponent<PartyPlayer>();
        }

        private void LateUpdate()
        {
            if (player == null || !player.IsLocalController || player.IsBot)
            {
                if (reticle != null) reticle.SetActive(false);
                return;
            }
            if (PartyGameManager.Instance != null && !PartyGameManager.Instance.IsGamePlaying())
            {
                if (reticle != null) reticle.SetActive(false);
                return;
            }
            EnsureReticle();
            if (!player.TryReadAimWorldPosition(out Vector3 world))
            {
                reticle.SetActive(false);
                return;
            }
            reticle.SetActive(true);
            reticle.transform.position = new Vector3(world.x, 0.05f, world.z);
        }

        private void EnsureReticle()
        {
            if (reticle != null) return;
            reticle = GameObject.CreatePrimitive(PrimitiveType.Quad);
            reticle.name = $"WaterGunReticle_P{player.PlayerIndex}";
            var col = reticle.GetComponent<Collider>();
            if (col != null) Destroy(col);
            reticle.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // lie flat on water
            reticle.transform.localScale = new Vector3(size, size, 1f);

            reticleRenderer = reticle.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = color;
            mat.mainTexture = BuildRingTexture(64, color);
            // Draw on top of everything so an enemy raft never covers the crosshair.
            // ZTest Always + a very high renderQueue puts it after all opaque + transparent geometry.
            mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 5000;
            reticleRenderer.sharedMaterial = mat;
            reticleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            reticleRenderer.receiveShadows = false;
            // Force this renderer to render after everything else in URP too.
            reticleRenderer.sortingOrder = 1000;
        }

        private static Texture2D BuildRingTexture(int size, Color c)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            float outer = size * 0.5f;
            float inner = size * 0.35f;
            float tick = size * 0.08f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - outer + 0.5f;
                    float dy = y - outer + 0.5f;
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

        private void OnDestroy()
        {
            if (reticle != null) Destroy(reticle);
        }
    }
}
