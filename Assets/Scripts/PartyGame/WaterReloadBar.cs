using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// World-space reload progress bar rendered above the player's head.
    /// Poll-based (reads netWaterReloading + WaterReloadNormalized) so all clients see everyone's
    /// reload state — server drives the source of truth, clients just visualize.
    ///
    /// Zero prefab setup: procedurally generates its own quads at runtime like PartyPlayerCrosshair.
    /// Attached automatically by PartyPlayer.OnNetworkSpawn on every peer's copy.
    /// </summary>
    [RequireComponent(typeof(PartyPlayer))]
    public class WaterReloadBar : MonoBehaviour
    {
        [SerializeField] private Vector3 offset = new Vector3(0f, 2.3f, 0f);
        [SerializeField] private float width = 1.5f;
        [SerializeField] private float height = 0.18f;

        private PartyPlayer player;
        private GameObject barRoot;
        private Transform bgQuad;
        private Transform fillQuad;

        private static readonly Color BgColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color FillColor = new Color(0.35f, 0.7f, 1f, 1f);

        private void Awake()
        {
            player = GetComponent<PartyPlayer>();
        }

        private void OnDestroy()
        {
            if (barRoot != null) Destroy(barRoot);
        }

        private void LateUpdate()
        {
            if (player == null) return;
            bool showing = player.WaterReloading;
            EnsureBar();
            if (barRoot == null) return;
            barRoot.SetActive(showing);
            if (!showing) return;

            barRoot.transform.position = transform.position + offset;
            var cam = Camera.main;
            if (cam != null) barRoot.transform.rotation = cam.transform.rotation;

            float n = Mathf.Clamp01(player.WaterReloadNormalized);
            Vector3 s = fillQuad.localScale;
            s.x = width * n;
            fillQuad.localScale = s;
            Vector3 lp = fillQuad.localPosition;
            lp.x = -width * 0.5f + s.x * 0.5f;
            fillQuad.localPosition = lp;
        }

        private void EnsureBar()
        {
            if (barRoot != null) return;
            barRoot = new GameObject($"WaterReloadBar_P{player.PlayerIndex}");
            barRoot.transform.SetParent(null, true);

            bgQuad = MakeQuad(barRoot.transform, BgColor);
            bgQuad.localScale = new Vector3(width + 0.1f, height + 0.06f, 1f);
            bgQuad.localPosition = Vector3.zero;

            fillQuad = MakeQuad(barRoot.transform, FillColor);
            fillQuad.localScale = new Vector3(width, height, 1f);
            fillQuad.localPosition = new Vector3(-width * 0.5f, 0f, -0.01f);
        }

        private static Transform MakeQuad(Transform parent, Color color)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = q.GetComponent<Collider>();
            if (col != null) Destroy(col);
            q.transform.SetParent(parent, false);
            var r = q.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Sprites/Default")) { color = color };
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return q.transform;
        }
    }
}
