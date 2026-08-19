using System.Collections.Generic;
using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// ★天花板碎片掉落系统。搜索阶段持续掉落，越接近结束越密集。
    /// 这是「房间正在崩坏」最直观的表现。
    ///
    /// 碎片是纯装饰：不带碰撞体、不伤害玩家，落地后淡出销毁。
    /// 用对象池避免频繁 Instantiate 造成 GC。
    /// </summary>
    public class CeilingDebrisSpawner : MonoBehaviour
    {
        [Header("范围")]
        [Tooltip("碎片生成的水平范围（房间尺寸）")]
        public Vector2 area = new Vector2(24f, 18f);

        [Tooltip("生成高度")]
        public float spawnHeight = 4.2f;

        [Header("外观")]
        public Color debrisColor = new Color(0.42f, 0.44f, 0.50f);

        [Tooltip("碎片尺寸范围")]
        public Vector2 sizeRange = new Vector2(0.08f, 0.26f);

        [Tooltip("蓝色像素碎片的比例（Bug 世界特征）")]
        [Range(0f, 1f)] public float pixelChance = 0.35f;

        public Color pixelColor = new Color(0.3f, 0.68f, 1f);

        [Header("对象池")]
        [Range(8, 80)] public int poolSize = 36;

        RoomConfig _cfg;
        float _nextBurst;
        bool _active;

        readonly List<DebrisPiece> _pool = new List<DebrisPiece>();

        class DebrisPiece
        {
            public GameObject go;
            public Transform tr;
            public Renderer rend;
            public Material mat;
            public float vy;
            public Vector3 spin;
            public float life;
            public float maxLife;
            public bool busy;
        }

        void Start()
        {
            _cfg = RoomManager.Instance != null ? RoomManager.Instance.config : null;
            BuildPool();
            _nextBurst = 1f;
        }

        void OnEnable() => RoomEvents.OnPhaseChanged += HandlePhase;
        void OnDisable() => RoomEvents.OnPhaseChanged -= HandlePhase;

        void HandlePhase(RoundPhase p)
        {
            _active = p == RoundPhase.Searching || p == RoundPhase.Collapse;
        }

        void BuildPool()
        {
            var root = new GameObject("DebrisPool");
            root.transform.SetParent(transform, false);

            for (int i = 0; i < poolSize; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Debris_" + i;
                go.transform.SetParent(root.transform, false);

                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);

                var rend = go.GetComponent<Renderer>();
                var piece = new DebrisPiece
                {
                    go = go,
                    tr = go.transform,
                    rend = rend,
                    mat = rend != null ? rend.material : null,
                    busy = false
                };
                go.SetActive(false);
                _pool.Add(piece);
            }
        }

        void Update()
        {
            UpdateActivePieces();

            if (!_active || _cfg == null) return;

            var mgr = RoomManager.Instance;
            if (mgr == null) return;

            // 终局塌陷时碎片如暴雨
            bool finalPhase = mgr.Phase == RoundPhase.Collapse;

            float urgency = 0f;
            if (mgr.Phase == RoundPhase.Searching && _cfg.urgentThreshold > 0f)
                urgency = 1f - Mathf.Clamp01(mgr.TimeLeft / _cfg.urgentThreshold);
            if (finalPhase) urgency = 1f;

            float interval = Mathf.Lerp(_cfg.debrisInterval, _cfg.debrisIntervalUrgent, urgency);
            if (finalPhase) interval *= 0.35f;

            _nextBurst -= Time.deltaTime;
            if (_nextBurst <= 0f)
            {
                int count = _cfg.debrisPerBurst + (finalPhase ? 3 : 0);
                for (int i = 0; i < count; i++) SpawnOne();
                _nextBurst = interval * Random.Range(0.7f, 1.3f);
            }
        }

        void SpawnOne()
        {
            DebrisPiece piece = null;
            for (int i = 0; i < _pool.Count; i++)
                if (!_pool[i].busy) { piece = _pool[i]; break; }

            if (piece == null) return;   // 池满了，本次跳过

            float size = Random.Range(sizeRange.x, sizeRange.y);
            bool isPixel = Random.value < pixelChance;

            piece.tr.position = new Vector3(
                Random.Range(-area.x * 0.5f, area.x * 0.5f),
                spawnHeight + Random.Range(-0.3f, 0.3f),
                Random.Range(-area.y * 0.5f, area.y * 0.5f));

            piece.tr.localScale = isPixel
                ? Vector3.one * size * 0.8f
                : new Vector3(size, size * Random.Range(0.4f, 1f), size);
            piece.tr.rotation = Random.rotation;

            piece.vy = 0f;
            piece.spin = Random.insideUnitSphere * Random.Range(90f, 320f);
            piece.maxLife = Random.Range(2.2f, 3.4f);
            piece.life = 0f;
            piece.busy = true;

            if (piece.mat != null)
            {
                var c = isPixel ? pixelColor : debrisColor;
                if (piece.mat.HasProperty("_BaseColor")) piece.mat.SetColor("_BaseColor", c);
                if (piece.mat.HasProperty("_Color")) piece.mat.SetColor("_Color", c);
            }

            piece.go.SetActive(true);
        }

        void UpdateActivePieces()
        {
            float g = _cfg != null ? _cfg.gravity : 22f;

            for (int i = 0; i < _pool.Count; i++)
            {
                var p = _pool[i];
                if (!p.busy) continue;

                p.life += Time.deltaTime;
                p.vy -= g * 0.55f * Time.deltaTime;

                var pos = p.tr.position;
                pos.y += p.vy * Time.deltaTime;
                p.tr.position = pos;
                p.tr.Rotate(p.spin * Time.deltaTime, Space.Self);

                // 生命结束或掉得太低 → 回收
                if (p.life >= p.maxLife || pos.y < -4f)
                {
                    p.busy = false;
                    p.go.SetActive(false);
                }
            }
        }

        /// <summary>回合重开时清空所有碎片。</summary>
        public void ClearAll()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                _pool[i].busy = false;
                if (_pool[i].go != null) _pool[i].go.SetActive(false);
            }
            _nextBurst = 1f;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.25f);
            Gizmos.DrawWireCube(
                transform.position + Vector3.up * spawnHeight,
                new Vector3(area.x, 0.2f, area.y));
        }
    }
}
