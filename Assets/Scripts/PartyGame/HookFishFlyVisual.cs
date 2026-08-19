using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// One-shot visual for a fish being reeled in by the hook. Spawned client-side via ClientRpc;
    /// travels from the hook impact point to the caster's raft over `duration` seconds on a small
    /// arc, then destroys itself. The server independently applies the fish to the caster's raft
    /// when the visual's duration elapses, so the disc appears on the raft the moment this visual
    /// finishes.
    /// </summary>
    public class HookFishFlyVisual : MonoBehaviour
    {
        private Vector3 fromWorld;
        private Vector3 toWorld;
        private float duration;
        private float t;
        private Transform sprite;
        private Material mat;

        private static readonly Color CommonColor = new Color(0.35f, 0.65f, 1f, 1f);
        private static readonly Color GoldenColor = new Color(1f, 0.85f, 0.2f, 1f);

        public static void Spawn(Vector3 from, Vector3 to, FishType type, float duration)
        {
            var go = new GameObject("HookFishFly");
            var v = go.AddComponent<HookFishFlyVisual>();
            v.Configure(from, to, type, duration);
        }

        public void Configure(Vector3 from, Vector3 to, FishType type, float duration)
        {
            this.fromWorld = from;
            this.toWorld = to;
            this.duration = Mathf.Max(0.05f, duration);

            transform.position = from;

            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = q.GetComponent<Collider>(); if (col != null) Destroy(col);
            q.transform.SetParent(transform, false);
            q.transform.localScale = new Vector3(0.6f, 0.6f, 1f);
            var r = q.GetComponent<MeshRenderer>();
            mat = new Material(Shader.Find("Sprites/Default")) { color = type == FishType.Common ? CommonColor : GoldenColor };
            mat.mainTexture = BuildFishTexture(48);
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            sprite = q.transform;
        }

        private void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            // Ease-in-out: rope reels the fish in with a slight arc toward the raft.
            Vector3 pos = Vector3.Lerp(fromWorld, toWorld, k);
            pos.y += Mathf.Sin(k * Mathf.PI) * 0.8f;
            transform.position = pos;

            var cam = GameWorldCamera.Resolve();
            if (cam != null && sprite != null) sprite.rotation = cam.transform.rotation;

            if (t >= duration) Destroy(gameObject);
        }

        private static Texture2D BuildFishTexture(int size)
        {
            // Simple oval, so it reads as "a fish disc" without needing an art asset.
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float rx = size * 0.45f, ry = size * 0.30f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - size * 0.5f + 0.5f) / rx;
                    float dy = (y - size * 0.5f + 0.5f) / ry;
                    float d = dx * dx + dy * dy;
                    float a = Mathf.Clamp01(1f - d);
                    a = Mathf.Pow(a, 0.6f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply(false, true);
            return tex;
        }
    }
}
