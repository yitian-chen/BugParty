using System.Collections.Generic;
using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 地板网格管理器。负责建立网格索引、调度随机塌陷、执行终局全塌陷。
    /// </summary>
    public class FloorGrid : MonoBehaviour
    {
        [Header("网格信息（建场工具填）")]
        public int columns = 12;
        public int rows = 9;
        public float tileSize = 2f;

        [Tooltip("网格原点（左下角地板中心的世界坐标）")]
        public Vector3 origin;

        readonly Dictionary<Vector2Int, FloorTile> _tiles = new Dictionary<Vector2Int, FloorTile>();
        readonly List<FloorTile> _all = new List<FloorTile>();

        public IReadOnlyList<FloorTile> AllTiles => _all;

        void Awake()
        {
            Rebuild();
        }

        /// <summary>扫描所有子物体建立索引。</summary>
        public void Rebuild()
        {
            _tiles.Clear();
            _all.Clear();

            var found = GetComponentsInChildren<FloorTile>(true);
            for (int i = 0; i < found.Length; i++)
            {
                var t = found[i];
                if (t == null) continue;
                _tiles[t.gridPos] = t;
                _all.Add(t);
            }
        }

        public FloorTile GetTile(Vector2Int g)
            => _tiles.TryGetValue(g, out var t) ? t : null;

        /// <summary>世界坐标 → 网格坐标。</summary>
        public Vector2Int WorldToGrid(Vector3 world)
        {
            float fx = (world.x - origin.x) / tileSize;
            float fz = (world.z - origin.z) / tileSize;
            return new Vector2Int(Mathf.RoundToInt(fx), Mathf.RoundToInt(fz));
        }

        /// <summary>网格坐标 → 世界坐标（地板中心）。</summary>
        public Vector3 GridToWorld(Vector2Int g)
            => new Vector3(origin.x + g.x * tileSize, origin.y, origin.z + g.y * tileSize);

        /// <summary>某个世界位置下方的地板。</summary>
        public FloorTile GetTileAt(Vector3 world) => GetTile(WorldToGrid(world));

        /// <summary>该位置是否是洞（不可通行）。AI 绕路用。</summary>
        public bool IsHoleAt(Vector3 world)
        {
            var t = GetTileAt(world);
            // 网格外也当作洞，防止 AI 走出地图
            return t == null || t.IsHole;
        }

        /// <summary>
        /// 找出距离指定位置最近的、还能站的地板中心。
        /// 玩家掉进洞里后用它来决定弹回哪里。
        /// </summary>
        public Vector3 FindNearestSafePosition(Vector3 from)
        {
            FloorTile best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < _all.Count; i++)
            {
                var t = _all[i];
                if (t == null || !t.IsWalkable) continue;

                float d = (t.transform.position - from).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = t; }
            }

            if (best == null) return from + Vector3.up * 2f;

            var p = best.transform.position;
            // 抬高一点避免生成时卡进地板
            p.y += 1.2f;
            return p;
        }

        /// <summary>
        /// ★挑选可以随机塌陷的候选地板。
        /// 排除受保护的、已塌的，以及离容器/出生点太近的。
        /// </summary>
        public List<FloorTile> PickRandomCollapseCandidates(
            int count, IReadOnlyList<Vector3> avoidPoints, float avoidRadius)
        {
            var pool = new List<FloorTile>();

            for (int i = 0; i < _all.Count; i++)
            {
                var t = _all[i];
                if (t == null || t.isProtected || t.State != TileState.Solid) continue;

                bool tooClose = false;
                if (avoidPoints != null)
                {
                    for (int k = 0; k < avoidPoints.Count; k++)
                    {
                        var flat = t.transform.position - avoidPoints[k];
                        flat.y = 0f;
                        if (flat.sqrMagnitude < avoidRadius * avoidRadius) { tooClose = true; break; }
                    }
                }
                if (tooClose) continue;

                pool.Add(t);
            }

            // Fisher-Yates 洗牌后取前 count 个
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            if (pool.Count > count) pool.RemoveRange(count, pool.Count - count);
            return pool;
        }

        /// <summary>
        /// ★终局全塌陷。以某点为中心波浪式扩散，视觉上比"全部同时塌"好看得多。
        /// </summary>
        public void TriggerFinalCollapse(Vector3 epicenter, float totalDuration)
        {
            // 找出最远距离用于归一化
            float maxDist = 0.01f;
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i] == null) continue;
                var flat = _all[i].transform.position - epicenter;
                flat.y = 0f;
                maxDist = Mathf.Max(maxDist, flat.magnitude);
            }

            for (int i = 0; i < _all.Count; i++)
            {
                var t = _all[i];
                if (t == null) continue;

                var flat = t.transform.position - epicenter;
                flat.y = 0f;

                // 距离震中越远，延迟越久 → 波浪扩散
                float delay = (flat.magnitude / maxDist) * totalDuration * 0.75f;
                delay += Random.Range(0f, totalDuration * 0.08f);   // 少量随机让边缘不整齐

                t.FinalCollapse(delay);
            }
        }

        public void ResetAll()
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i] != null) _all[i].ResetTile();
        }
    }
}
