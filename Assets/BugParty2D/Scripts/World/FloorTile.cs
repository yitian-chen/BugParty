using System.Collections;
using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// ★可塌陷地板块。整个房间地面由若干这样的块拼成。
    ///
    /// 四态流程：
    ///   Solid（完好）→ Cracking（开裂预警，红光闪烁但仍可走）
    ///                → Collapsed（碰撞体移除，成为不可通行的洞）
    ///                → Falling（终局全塌时的下坠动画）
    ///
    /// 设计要点：Cracking 阶段必须存在。直接塌陷会让玩家觉得被阴，
    /// 有 1.8 秒预警才能形成「快跑离开」的正向操作。
    /// </summary>
    public class FloorTile : MonoBehaviour
    {
        [Header("标识")]
        [Tooltip("网格坐标，建场工具会填")]
        public Vector2Int gridPos;

        [Tooltip("勾选后本块永不随机塌陷（出生点、容器脚下）。终局仍会塌")]
        public bool isProtected = false;

        [Header("视觉")]
        public Renderer tileRenderer;

        [Tooltip("完好状态颜色")]
        public Color solidColor = new Color(0.24f, 0.25f, 0.30f);

        [Tooltip("开裂预警时闪烁的颜色")]
        public Color crackColor = new Color(0.95f, 0.25f, 0.20f);

        // ── 运行时 ─────────────────────────────────────
        TileState _state = TileState.Solid;
        Collider _collider;
        Vector3 _originPos;
        Coroutine _routine;
        RoomConfig _cfg;
        Material _mat;

        public TileState State => _state;
        public bool IsWalkable => _state == TileState.Solid || _state == TileState.Cracking;
        public bool IsHole => _state == TileState.Collapsed || _state == TileState.Falling;

        void Awake()
        {
            _collider = GetComponent<Collider>();
            _originPos = transform.localPosition;

            if (tileRenderer == null) tileRenderer = GetComponent<Renderer>();
            if (tileRenderer != null) _mat = tileRenderer.material;
        }

        void Start()
        {
            _cfg = RoomManager.Instance != null ? RoomManager.Instance.config : null;
            ApplyColor(solidColor);
        }

        void ApplyColor(Color c)
        {
            if (_mat == null) return;
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);
            if (_mat.HasProperty("_Color")) _mat.SetColor("_Color", c);
        }

        // ══════════════════════════════════════════════
        //  搜索阶段：单块随机塌陷
        // ══════════════════════════════════════════════

        /// <summary>
        /// 开始「预警 → 塌陷」流程。搜索阶段由 RoomManager 调度。
        /// </summary>
        public void BeginCollapseSequence()
        {
            if (_state != TileState.Solid) return;
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(CollapseSequence());
        }

        IEnumerator CollapseSequence()
        {
            // ── 阶段：开裂预警 ──
            _state = TileState.Cracking;
            RoomEvents.RaiseTileCracking(this);

            float warn = _cfg != null ? _cfg.crackWarningTime : 1.8f;
            float t = 0f;

            while (t < warn)
            {
                t += Time.deltaTime;

                // 闪烁频率随预警推进而加快，最后阶段几乎是急闪
                float progress = t / warn;
                float freq = Mathf.Lerp(4f, 16f, progress);
                float k = (Mathf.Sin(t * freq) + 1f) * 0.5f;
                ApplyColor(Color.Lerp(solidColor, crackColor, k * Mathf.Lerp(0.5f, 1f, progress)));

                // 轻微抖动，提示"这块要掉了"
                float shake = Mathf.Lerp(0.01f, 0.05f, progress);
                transform.localPosition = _originPos + new Vector3(
                    Random.Range(-shake, shake), 0f, Random.Range(-shake, shake));

                yield return null;
            }

            // ── 阶段：塌陷 ──
            Collapse();
        }

        /// <summary>立即塌陷（不经过预警）。</summary>
        public void Collapse()
        {
            if (_state == TileState.Collapsed || _state == TileState.Falling) return;

            _state = TileState.Collapsed;
            if (_collider != null) _collider.enabled = false;

            RoomEvents.RaiseTileCollapsed(this);

            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(DropAway(false));
        }

        // ══════════════════════════════════════════════
        //  终局：全塌陷
        // ══════════════════════════════════════════════

        /// <summary>
        /// 终局塌陷。delay 用于制造波浪式扩散效果。
        /// </summary>
        public void FinalCollapse(float delay)
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(FinalCollapseRoutine(delay));
        }

        IEnumerator FinalCollapseRoutine(float delay)
        {
            // 塌陷前先急速闪红，形成"整片地板一起亮起"的视觉冲击
            float t = 0f;
            while (t < delay)
            {
                t += Time.deltaTime;
                float k = (Mathf.Sin(t * 22f) + 1f) * 0.5f;
                ApplyColor(Color.Lerp(solidColor, crackColor, k * 0.85f));

                float shake = 0.045f;
                transform.localPosition = _originPos + new Vector3(
                    Random.Range(-shake, shake), 0f, Random.Range(-shake, shake));
                yield return null;
            }

            _state = TileState.Falling;
            if (_collider != null) _collider.enabled = false;
            RoomEvents.RaiseTileCollapsed(this);

            yield return DropAway(true);
        }

        /// <summary>地板下坠动画。isFinal 时坠得更快更远。</summary>
        IEnumerator DropAway(bool isFinal)
        {
            float depth = _cfg != null ? _cfg.collapseDropDepth : 6f;
            if (isFinal) depth *= 2.5f;

            float speed = isFinal ? 9f : 4.5f;
            float spin = isFinal ? Random.Range(40f, 120f) : Random.Range(10f, 40f);
            var axis = Random.insideUnitSphere.normalized;

            float fallen = 0f;
            var start = _originPos;

            while (fallen < depth)
            {
                float d = speed * Time.deltaTime;
                fallen += d;
                speed += 18f * Time.deltaTime;   // 加速下坠

                transform.localPosition = start + Vector3.down * fallen;
                transform.Rotate(axis, spin * Time.deltaTime, Space.Self);
                yield return null;
            }

            // 沉到底后隐藏，减少渲染开销
            if (tileRenderer != null) tileRenderer.enabled = false;
        }

        // ══════════════════════════════════════════════

        public void ResetTile()
        {
            if (_routine != null) { StopCoroutine(_routine); _routine = null; }

            _state = TileState.Solid;
            transform.localPosition = _originPos;
            transform.localRotation = Quaternion.identity;

            if (_collider != null) _collider.enabled = true;
            if (tileRenderer != null) tileRenderer.enabled = true;
            ApplyColor(solidColor);
        }

        void OnDrawGizmos()
        {
            if (_state == TileState.Solid) return;

            Gizmos.color = _state == TileState.Cracking
                ? new Color(1f, 0.6f, 0.1f, 0.5f)
                : new Color(1f, 0.15f, 0.1f, 0.35f);
            Gizmos.DrawWireCube(transform.position, transform.localScale);
        }
    }
}
