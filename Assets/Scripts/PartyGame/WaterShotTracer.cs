using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// One-shot water tracer. Spawned server-side (via ClientRpc broadcast) and drawn on every client.
    /// Uses a LineRenderer with a fade-out over `lifetime` seconds; auto-destroys.
    /// </summary>
    public class WaterShotTracer : MonoBehaviour
    {
        [SerializeField] private float lifetime = 0.15f;
        [SerializeField] private float startWidth = 0.18f;
        [SerializeField] private float endWidth = 0.05f;
        [SerializeField] private Color color = new Color(0.35f, 0.7f, 1f, 1f);

        private LineRenderer line;
        private float t;

        public static void Spawn(Vector3 from, Vector3 to, bool hit)
        {
            var go = new GameObject("WaterTracer");
            var t = go.AddComponent<WaterShotTracer>();
            t.Configure(from, to, hit);
        }

        private void Awake()
        {
            line = gameObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.useWorldSpace = true;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
        }

        public void Configure(Vector3 from, Vector3 to, bool hit)
        {
            if (line == null) Awake();
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = startWidth;
            line.endWidth = endWidth;
            var c = hit ? new Color(0.5f, 0.9f, 1f, 1f) : color;
            line.startColor = c;
            line.endColor = new Color(c.r, c.g, c.b, 0.5f);
        }

        private void Update()
        {
            t += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(t / lifetime);
            if (line != null)
            {
                var sc = line.startColor; sc.a = k; line.startColor = sc;
                var ec = line.endColor;   ec.a = k * 0.6f; line.endColor = ec;
            }
            if (t >= lifetime) Destroy(gameObject);
        }
    }
}
