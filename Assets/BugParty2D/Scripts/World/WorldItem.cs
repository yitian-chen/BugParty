using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>掉落在地上的道具。可被任何人拾取。</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class WorldItem : MonoBehaviour
    {
        public ItemDefinition definition;

        [Header("表现")]
        public float spinSpeed = 100f;
        public float bobAmplitude = 0.07f;
        public float bobSpeed = 2.3f;
        public float pickupRadius = 0.95f;

        Rigidbody _rb;
        Transform _visual;
        float _visualBaseY;
        float _pickupTime;
        float _spawnTime;
        bool _settled;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _spawnTime = Time.time;
        }

        public static WorldItem SpawnDropped(
            ItemDefinition def, Vector3 origin, Vector3 popDir, RoomConfig cfg)
        {
            if (def == null) return null;

            GameObject go;
            if (def.worldPrefab != null)
            {
                go = Instantiate(def.worldPrefab, origin, Random.rotation);
            }
            else
            {
                go = new GameObject("Item_" + def.itemId);
                go.transform.position = origin;

                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = "Visual";
                visual.transform.SetParent(go.transform, false);
                visual.transform.localScale = def.placeholderSize;

                var vc = visual.GetComponent<Collider>();
                if (vc != null) Destroy(vc);

                var r = visual.GetComponent<Renderer>();
                if (r != null)
                {
                    var m = r.material;
                    var col = def.isRare
                        ? Color.Lerp(def.placeholderColor, new Color(1f, 0.85f, 0.2f), 0.5f)
                        : def.placeholderColor;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", col);
                }
            }

            go.name = "DroppedItem_" + def.itemId;

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            rb.drag = 0.45f;

            var sc = go.GetComponent<SphereCollider>();
            if (sc == null) sc = go.AddComponent<SphereCollider>();
            sc.radius = 0.26f;

            var wi = go.GetComponent<WorldItem>();
            if (wi == null) wi = go.AddComponent<WorldItem>();
            wi.definition = def;
            wi._pickupTime = Time.time + (cfg != null ? cfg.droppedItemPickupDelay : 0.4f);

            float force = cfg != null ? cfg.itemPopForce : 5f;
            var dir = popDir.sqrMagnitude > 0.0001f ? popDir.normalized : Vector3.up;
            rb.AddForce(dir * force, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * 3f, ForceMode.Impulse);

            return wi;
        }

        void Start()
        {
            _visual = transform.Find("Visual");
            if (_visual != null) _visualBaseY = _visual.localPosition.y;
        }

        void Update()
        {
            var mgr = RoomManager.Instance;

            // ★地板塌了之后掉下去的道具直接销毁，避免堆在虚空里
            if (mgr != null && transform.position.y < -12f)
            {
                Destroy(gameObject);
                return;
            }

            if (!_settled && Time.time - _spawnTime > 0.85f && _rb.velocity.magnitude < 0.38f)
            {
                _settled = true;
                _rb.velocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }

            if (!_settled) return;

            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

            if (_visual != null)
            {
                var lp = _visual.localPosition;
                lp.y = _visualBaseY + Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
                _visual.localPosition = lp;
            }

            TryPickup();
        }

        void TryPickup()
        {
            if (Time.time < _pickupTime) return;

            var mgr = RoomManager.Instance;
            if (mgr == null || !mgr.CanAct) return;

            float bestSqr = pickupRadius * pickupRadius;
            PlayerActor best = null;

            for (int i = 0; i < mgr.players.Count; i++)
            {
                var p = mgr.players[i];
                if (p == null || !p.IsAlive || p.IsInPitfall) continue;
                if (p.Inventory.IsFull) continue;

                // 3D 距离：站在桌上捡不到地上的东西
                float d = (p.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = p; }
            }

            if (best != null && best.Inventory.TryAdd(definition))
            {
                RoomEvents.RaiseItemCollected(best, definition);
                Destroy(gameObject);
            }
        }
    }
}
