using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// One-shot water tracer. Spawned server-side via ClientRpc; drawn on every client.
    /// Two stacked LineRenderers (a wide translucent outer bolt + a thin bright core)
    /// plus a splash sprite at the impact point make the shot easy to spot in motion.
    /// Fades out over `lifetime` seconds and auto-destroys.
    /// </summary>
    public class WaterShotTracer : MonoBehaviour
    {
        [SerializeField] private float lifetime = 0.3f;
        [SerializeField] private float outerWidth = 0.55f;
        [SerializeField] private float coreWidth = 0.18f;
        [SerializeField] private float splashSize = 0.9f;

        private LineRenderer outerLine;
        private LineRenderer coreLine;
        private GameObject splashGO;
        private Transform splashTF;
        private float t;

        private static readonly Color OuterColor = new Color(0.35f, 0.75f, 1f, 0.85f);
        private static readonly Color CoreColor  = new Color(1f, 1f, 1f, 1f);
        private static readonly Color HitTintOuter = new Color(0.6f, 0.95f, 1f, 0.95f);

        public static void Spawn(Vector3 from, Vector3 to, bool hit)
        {
            var go = new GameObject("WaterTracer");
            var t = go.AddComponent<WaterShotTracer>();
            t.Configure(from, to, hit);
        }

        public void Configure(Vector3 from, Vector3 to, bool hit)
        {
            outerLine = BuildLine("Outer", hit ? HitTintOuter : OuterColor, outerWidth, 0);
            coreLine  = BuildLine("Core",  CoreColor,                      coreWidth, 1);
            outerLine.SetPosition(0, from);
            outerLine.SetPosition(1, to);
            coreLine.SetPosition(0, from);
            coreLine.SetPosition(1, to);

            splashGO = new GameObject("Splash");
            splashGO.transform.SetParent(transform, false);
            splashGO.transform.position = to;
            splashTF = splashGO.transform;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = q.GetComponent<Collider>();
            if (col != null) Destroy(col);
            q.transform.SetParent(splashTF, false);
            q.transform.localScale = new Vector3(splashSize, splashSize, 1f);
            var r = q.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Sprites/Default")) { color = hit ? Color.white : new Color(1f, 1f, 1f, 0.85f) };
            mat.mainTexture = BuildSplashTexture(48);
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private LineRenderer BuildLine(string name, Color c, float width, int sortingOrder)
        {
            var lineGO = new GameObject(name);
            lineGO.transform.SetParent(transform, false);
            var lr = lineGO.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.useWorldSpace = true;
            lr.startWidth = width;
            lr.endWidth = width * 0.6f;
            lr.numCapVertices = 6;
            lr.startColor = c;
            lr.endColor = new Color(c.r, c.g, c.b, c.a * 0.6f);
            lr.sortingOrder = sortingOrder;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        private void Update()
        {
            t += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(t / lifetime);
            // Outer widens slightly and fades; core stays sharp and fades faster.
            if (outerLine != null)
            {
                float w = outerWidth * (0.9f + (1f - k) * 0.6f);
                outerLine.startWidth = w;
                outerLine.endWidth = w * 0.6f;
                var sc = outerLine.startColor; sc.a = k * 0.9f; outerLine.startColor = sc;
                var ec = outerLine.endColor;   ec.a = k * 0.5f; outerLine.endColor = ec;
            }
            if (coreLine != null)
            {
                var sc = coreLine.startColor; sc.a = k * k; coreLine.startColor = sc;
                var ec = coreLine.endColor;   ec.a = k * 0.7f; coreLine.endColor = ec;
            }
            if (splashTF != null)
            {
                // Grow the splash while fading it, so it looks like a bloom.
                float s = splashSize * (1f + (1f - k) * 1.2f);
                splashTF.localScale = new Vector3(s, s, 1f);
                var cam = GameWorldCamera.Resolve();
                if (cam != null) splashTF.rotation = cam.transform.rotation;
                var r = splashGO.GetComponentInChildren<Renderer>();
                if (r != null && r.sharedMaterial != null)
                {
                    var c = r.sharedMaterial.color; c.a = k; r.sharedMaterial.color = c;
                }
            }
            if (t >= lifetime) Destroy(gameObject);
        }

        private static Texture2D BuildSplashTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - r + 0.5f, dy = y - r + 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                    // Bright center, soft falloff.
                    float a = Mathf.Clamp01(1f - d);
                    a = Mathf.Pow(a, 1.8f);
                    tex.SetPixel(x, y, new Color(0.7f, 0.9f, 1f, a));
                }
            tex.Apply(false, true);
            return tex;
        }
    }
}
