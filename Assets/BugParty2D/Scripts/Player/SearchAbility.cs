using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>搜索能力。读条 + 容器独占 + 打断回滚。</summary>
    public class SearchAbility : MonoBehaviour
    {
        PlayerActor _actor;
        RoomConfig _cfg;

        SearchContainer _target;
        float _progress;

        public bool IsSearching => _target != null;
        public SearchContainer CurrentTarget => _target;

        public float Progress01 => _cfg != null && _cfg.searchTime > 0f
            ? Mathf.Clamp01(_progress / _cfg.searchTime) : 0f;

        public void Init(PlayerActor actor, RoomConfig cfg)
        {
            _actor = actor;
            _cfg = cfg;
        }

        /// <summary>找出范围内可搜的容器。</summary>
        public SearchContainer FindTargetInRange()
        {
            if (_cfg == null || _actor.Inventory.IsFull) return null;

            var mgr = RoomManager.Instance;
            if (mgr == null) return null;

            SearchContainer best = null;
            float bestSqr = _cfg.searchRange * _cfg.searchRange;

            for (int i = 0; i < mgr.containers.Count; i++)
            {
                var c = mgr.containers[i];
                if (c == null || !c.IsAvailableFor(_actor)) continue;

                // 3D 距离：站在桌上搜不到地上的抽屉，这是高度差带来的额外规则
                float d = (c.InteractPoint - transform.position).sqrMagnitude;
                if (d <= bestSqr) { bestSqr = d; best = c; }
            }
            return best;
        }

        public bool TryBegin(SearchContainer container = null)
        {
            if (IsSearching) return false;
            if (_actor.IsStaggered) return false;
            if (_actor.Inventory.IsFull) return false;

            // 空中不能搜索
            if (_actor.IsAirborne || _actor.IsInPitfall) return false;

            var mgr = RoomManager.Instance;
            if (mgr == null || !mgr.CanAct) return false;

            var c = container != null ? container : FindTargetInRange();
            if (c == null || !c.TryClaim(_actor)) return false;

            _target = c;
            _progress = 0f;
            RoomEvents.RaiseSearchStarted(_actor, c);
            return true;
        }

        public void Cancel(bool interrupted)
        {
            if (_target == null) return;

            var c = _target;
            _target = null;
            _progress = 0f;

            c.Release(_actor, interrupted);
            if (interrupted) RoomEvents.RaiseSearchInterrupted(_actor, c);
        }

        void Update()
        {
            if (!IsSearching) return;

            var mgr = RoomManager.Instance;
            if (mgr == null || !mgr.CanAct) { Cancel(false); return; }

            // 脚下地板塌了 → 搜索自动中断
            if (mgr.floorGrid != null && mgr.floorGrid.IsHoleAt(transform.position))
            {
                Cancel(true);
                return;
            }

            float distSqr = (_target.InteractPoint - transform.position).sqrMagnitude;
            float maxSqr = (_cfg.searchRange * 1.4f) * (_cfg.searchRange * 1.4f);
            if (distSqr > maxSqr) { Cancel(true); return; }

            _progress += Time.deltaTime;
            if (_progress >= _cfg.searchTime) Complete();
        }

        void Complete()
        {
            var c = _target;
            _target = null;
            _progress = 0f;

            var item = c.ExtractItem();
            c.Release(_actor, false);

            if (item != null && _actor.Inventory.TryAdd(item))
                RoomEvents.RaiseItemCollected(_actor, item);
        }
    }
}
