using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Wake trail behind the raft while the booster is active. Auto-attached to every PartyPlayer
    /// (via PartyPlayer's OnNetworkSpawn), driven by the server-authoritative BoosterActive flag
    /// so every peer sees the same effect at the same time.
    ///
    /// Keep it visually cheap: two symmetric TrailRenderers offset from the raft's rear so the
    /// effect reads as a wake, not a laser. No shader work — Sprites/Default material with a
    /// procedural white texture and a hard-fade gradient.
    /// </summary>
    public class BoosterWakeVisual : MonoBehaviour
    {
        [SerializeField] private float wakeWidth = 0.6f;
        [SerializeField] private float wakeTime  = 0.45f;
        [SerializeField] private Vector3 rearOffset = new Vector3(0f, 0.05f, -0.7f);
        [SerializeField] private float sideSpacing = 0.55f;

        private PartyPlayer player;
        private TrailRenderer leftTrail;
        private TrailRenderer rightTrail;
        private Transform leftAnchor;
        private Transform rightAnchor;

        private void Awake()
        {
            player = GetComponent<PartyPlayer>();
            BuildTrails();
            SetVisible(false);
        }

        private void BuildTrails()
        {
            leftAnchor  = CreateAnchor("BoosterWake_LeftAnchor",  rearOffset + new Vector3(-sideSpacing, 0f, 0f));
            rightAnchor = CreateAnchor("BoosterWake_RightAnchor", rearOffset + new Vector3(+sideSpacing, 0f, 0f));
            leftTrail  = CreateTrail(leftAnchor,  "BoosterWake_Left");
            rightTrail = CreateTrail(rightAnchor, "BoosterWake_Right");
        }

        private Transform CreateAnchor(string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        private TrailRenderer CreateTrail(Transform anchor, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(anchor, false);
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = wakeTime;
            tr.startWidth = wakeWidth;
            tr.endWidth = wakeWidth * 0.1f;
            tr.minVertexDistance = 0.05f;
            tr.numCapVertices = 2;
            tr.material = new Material(Shader.Find("Sprites/Default"));
            tr.material.mainTexture = BuildWhiteTexture();
            tr.startColor = new Color(0.8f, 0.95f, 1f, 0.85f);
            tr.endColor = new Color(0.8f, 0.95f, 1f, 0f);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.emitting = false;
            return tr;
        }

        private static Texture2D BuildWhiteTexture()
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++) t.SetPixel(x, y, Color.white);
            t.Apply(false, true);
            return t;
        }

        private void LateUpdate()
        {
            if (player == null) return;
            SetVisible(player.BoosterActive);
        }

        private void SetVisible(bool active)
        {
            if (leftTrail  != null) leftTrail.emitting  = active;
            if (rightTrail != null) rightTrail.emitting = active;
            // Also clear the trail geometry when disabling so the tail doesn't linger frozen in
            // place after the sprint ends.
            if (!active)
            {
                if (leftTrail  != null) leftTrail.Clear();
                if (rightTrail != null) rightTrail.Clear();
            }
        }
    }
}
