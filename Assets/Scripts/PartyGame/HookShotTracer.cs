using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// One-shot grappling-hook visual. Spawned server-side via ClientRpc; drawn on every client.
    /// A LineRenderer rope grows from muzzle to the impact point at `castSpeed`, followed by a
    /// short "held" phase, then fades out. A small dark quad hangs at the far end to suggest the
    /// hook head.
    ///
    /// Server has already resolved the hit and applied the drag — this is pure feedback. Keep the
    /// visual cheap: two colors, procedural textures, no shader work.
    /// </summary>
    public class HookShotTracer : MonoBehaviour
    {
        [SerializeField] private float extendDuration = 0.12f;
        [SerializeField] private float holdDuration   = 0.18f;
        [SerializeField] private float fadeDuration   = 0.15f;
        [SerializeField] private float ropeWidth      = 0.18f;
        [SerializeField] private float hookHeadSize   = 0.55f;

        private LineRenderer rope;
        private Transform hookHeadTF;
        private Material hookMat;
        private Vector3 from;
        private Vector3 to;
        private bool hit;
        private float t;

        private static readonly Color RopeColorMiss = new Color(0.9f, 0.85f, 0.7f, 0.95f);
        private static readonly Color RopeColorHit  = new Color(1f,   1f,   0.4f, 1f);
        private static readonly Color HookColor     = new Color(0.2f, 0.2f, 0.22f, 1f);

        public static HookShotTracer Spawn(Vector3 from, Vector3 to, bool hit)
        {
            var go = new GameObject("HookTracer");
            var tr = go.AddComponent<HookShotTracer>();
            tr.Configure(from, to, hit);
            return tr;
        }

        /// <summary>
        /// Server-triggered cut: freeze the rope tip at `newTip` (where the hook actually caught
        /// something mid-flight) and skip any further extension. Used when the sweep resolves a
        /// moving player-hit shorter than the tracer's initial endpoint.
        /// </summary>
        public void CutShort(Vector3 newTip)
        {
            to = newTip;
            // Force phase 1 to end immediately by advancing t past extendDuration.
            if (t < extendDuration) t = extendDuration;
            if (rope != null) rope.SetPosition(1, newTip);
            if (hookHeadTF != null) hookHeadTF.position = newTip;
        }

        public void Configure(Vector3 from, Vector3 to, bool hit)
        {
            this.from = from;
            this.to   = to;
            this.hit  = hit;

            // Convert cast-speed (m/s from PartyGameConfig) into an extend duration so long shots
            // visibly take longer than short ones. Fall back to the serialized default when config
            // isn't reachable (e.g. spawned via ClientRpc before manager is up). No upper clamp: the
            // travel time must match the server's pull delay so the rope reaches the target exactly
            // when the pull begins.
            var cfg = PartyGameManager.Instance != null ? PartyGameManager.Instance.Config : null;
            float speed = cfg != null && cfg.hookCastSpeed > 0.01f ? cfg.hookCastSpeed : 45f;
            float dist = Vector3.Distance(from, to);
            extendDuration = Mathf.Max(0.05f, dist / speed);

            var lineGO = new GameObject("Rope");
            lineGO.transform.SetParent(transform, false);
            rope = lineGO.AddComponent<LineRenderer>();
            rope.positionCount = 2;
            rope.material = new Material(Shader.Find("Sprites/Default"));
            rope.useWorldSpace = true;
            rope.startWidth = ropeWidth;
            rope.endWidth = ropeWidth * 0.9f;
            rope.numCapVertices = 4;
            rope.startColor = hit ? RopeColorHit : RopeColorMiss;
            rope.endColor = rope.startColor;
            rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rope.receiveShadows = false;
            rope.SetPosition(0, from);
            rope.SetPosition(1, from); // extends toward `to` over `extendDuration`

            var headGO = new GameObject("Hook");
            headGO.transform.SetParent(transform, false);
            hookHeadTF = headGO.transform;
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = quad.GetComponent<Collider>(); if (col != null) Destroy(col);
            quad.transform.SetParent(hookHeadTF, false);
            quad.transform.localScale = new Vector3(hookHeadSize, hookHeadSize, 1f);
            var r = quad.GetComponent<MeshRenderer>();
            hookMat = new Material(Shader.Find("Sprites/Default")) { color = HookColor };
            hookMat.mainTexture = BuildHookTexture(32);
            r.sharedMaterial = hookMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            hookHeadTF.position = from;
        }

        private void Update()
        {
            t += Time.deltaTime;
            float total = extendDuration + holdDuration + fadeDuration;

            // Phase 1 — extend the rope out to `to` linearly.
            if (t <= extendDuration)
            {
                float k = extendDuration <= 0f ? 1f : Mathf.Clamp01(t / extendDuration);
                Vector3 tip = Vector3.Lerp(from, to, k);
                if (rope != null) rope.SetPosition(1, tip);
                if (hookHeadTF != null) hookHeadTF.position = tip;
            }
            else
            {
                if (rope != null) rope.SetPosition(1, to);
                if (hookHeadTF != null) hookHeadTF.position = to;
            }

            // Phase 3 — fade out.
            float fadeStart = extendDuration + holdDuration;
            if (t > fadeStart && rope != null)
            {
                float f = Mathf.Clamp01(1f - (t - fadeStart) / Mathf.Max(0.01f, fadeDuration));
                var c1 = rope.startColor; c1.a = f; rope.startColor = c1;
                var c2 = rope.endColor;   c2.a = f; rope.endColor = c2;
                if (hookMat != null) { var hc = hookMat.color; hc.a = f; hookMat.color = hc; }
            }

            // Face the camera so the hook-head quad reads at any angle.
            if (hookHeadTF != null)
            {
                var cam = GameWorldCamera.Resolve();
                if (cam != null) hookHeadTF.rotation = cam.transform.rotation;
            }

            if (t >= total) Destroy(gameObject);
        }

        private static Texture2D BuildHookTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // A simple hook shape: an outer arc (ring) + a downward barb.
                    float dx = x - size * 0.5f + 0.5f, dy = y - size * 0.5f + 0.5f;
                    float d  = Mathf.Sqrt(dx * dx + dy * dy);
                    float outer = size * 0.42f, inner = size * 0.28f;
                    bool inArc = d >= inner && d <= outer && dy > -1f;
                    bool inBarb = dy < -size * 0.15f && Mathf.Abs(dx) < size * 0.10f;
                    Color c = (inArc || inBarb) ? new Color(1f, 1f, 1f, 1f) : new Color(0, 0, 0, 0);
                    tex.SetPixel(x, y, c);
                }
            tex.Apply(false, true);
            return tex;
        }
    }
}
