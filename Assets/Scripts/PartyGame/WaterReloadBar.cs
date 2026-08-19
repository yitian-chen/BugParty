using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// World-space reload progress bar rendered above the player's head. Reuses the
    /// FishingProgressBar prefab's visual so the water-gun reload matches the fishing bar style —
    /// same Border / BG / FillPivot / Fill hierarchy driven the same way, minus the FishingProgressBar
    /// component itself (this class drives the FillPivot child).
    ///
    /// Poll-based (reads netWaterReloading + WaterReloadNormalized) so all clients see everyone's
    /// reload state — server drives the source of truth, clients just visualize.
    ///
    /// Attached automatically by PartyPlayer.OnNetworkSpawn on every peer's copy. Falls back to a
    /// procedurally generated quad if PartyGameConfig.reloadBarPrefab is unset.
    /// </summary>
    [RequireComponent(typeof(PartyPlayer))]
    public class WaterReloadBar : MonoBehaviour
    {
        [SerializeField] private Vector3 offset = new Vector3(0f, 2.3f, 0f);
        [SerializeField] private float maxFillWidth = 1.5f;

        private PartyPlayer player;
        private GameObject instance;
        private Transform barRoot;
        private Transform fillTransform;

        // Fallback procedural pieces (only used when the prefab is unavailable).
        private Transform fallbackBg;
        private Transform fallbackFill;

        private void Awake()
        {
            player = GetComponent<PartyPlayer>();
        }

        private void OnDestroy()
        {
            if (instance != null) Destroy(instance);
        }

        private void LateUpdate()
        {
            if (player == null) return;
            bool showing = player.WaterReloading;
            EnsureBar();
            if (barRoot == null) return;
            barRoot.gameObject.SetActive(showing);
            if (!showing) return;

            barRoot.position = transform.position + offset;
            var cam = Camera.main;
            if (cam != null) barRoot.rotation = cam.transform.rotation;

            ApplyFill(Mathf.Clamp01(player.WaterReloadNormalized));
        }

        private void ApplyFill(float n)
        {
            if (fillTransform == null) return;
            if (fillTransform.childCount > 0)
            {
                // Prefab layout: FillPivot has a child "Fill" quad that grows from the pivot's origin.
                float w = n * maxFillWidth;
                Transform quad = fillTransform.GetChild(0);
                Vector3 s = quad.localScale;
                s.x = w;
                quad.localScale = s;
                Vector3 lp = quad.localPosition;
                lp.x = w * 0.5f;
                quad.localPosition = lp;
            }
            else
            {
                // Fallback: fill is the transform itself, centered.
                Vector3 s = fillTransform.localScale;
                s.x = maxFillWidth * n;
                fillTransform.localScale = s;
                Vector3 lp = fillTransform.localPosition;
                lp.x = -maxFillWidth * 0.5f + s.x * 0.5f;
                fillTransform.localPosition = lp;
            }
        }

        private void EnsureBar()
        {
            if (instance != null) return;
            var cfg = player != null ? player.Config : null;

            if (cfg != null && cfg.reloadBarPrefab != null)
            {
                instance = Instantiate(cfg.reloadBarPrefab);
                instance.name = $"WaterReloadBar_P{player.PlayerIndex}";
                // Kill the FishingProgressBar driver so we don't fight it for control of the fill.
                var fpb = instance.GetComponent<FishingProgressBar>();
                if (fpb != null) Destroy(fpb);
                // Grab the FillPivot child by name to keep the same driving semantics as FishingProgressBar.
                var root = instance.transform.Find("BarRoot");
                barRoot = root != null ? root : instance.transform;
                fillTransform = FindDeep(instance.transform, "FillPivot");
                if (fillTransform == null) fillTransform = FindDeep(instance.transform, "Fill");
                return;
            }

            // ---- Procedural fallback ----
            instance = new GameObject($"WaterReloadBar_P{player.PlayerIndex}");
            barRoot = instance.transform;
            fallbackBg = MakeQuad(barRoot, new Color(0f, 0f, 0f, 0.55f));
            fallbackBg.localScale = new Vector3(maxFillWidth + 0.1f, 0.24f, 1f);
            fallbackFill = MakeQuad(barRoot, new Color(0.35f, 0.7f, 1f, 1f));
            fallbackFill.localScale = new Vector3(maxFillWidth, 0.18f, 1f);
            fallbackFill.localPosition = new Vector3(-maxFillWidth * 0.5f, 0f, -0.01f);
            fillTransform = fallbackFill; // in the fallback path FillPivot IS the fill.
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
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
