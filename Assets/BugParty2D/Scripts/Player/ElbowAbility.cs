using System.Collections.Generic;
using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 肘击。锥形范围判定。
    /// 2D 俯视版新增：可以把站在桌子上的对手打下来（附带高度差惩罚）。
    /// </summary>
    public class ElbowAbility : MonoBehaviour
    {
        PlayerActor _actor;
        RoomConfig _cfg;

        float _cooldownUntil;
        float _windupUntil;
        bool _pending;

        public bool IsReady => Time.time >= _cooldownUntil;
        public float CooldownRemain => Mathf.Max(0f, _cooldownUntil - Time.time);

        public float CooldownFill01
        {
            get
            {
                if (_cfg == null || _cfg.elbowCooldown <= 0f) return 1f;
                return Mathf.Clamp01(1f - CooldownRemain / _cfg.elbowCooldown);
            }
        }

        public void Init(PlayerActor actor, RoomConfig cfg)
        {
            _actor = actor;
            _cfg = cfg;
        }

        public bool TryElbow()
        {
            if (!IsReady || _pending) return false;
            if (_actor.IsStaggered || _actor.IsInPitfall) return false;

            var mgr = RoomManager.Instance;
            if (mgr == null || !mgr.CanAct) return false;

            // 挥肘会中断自己的搜索
            if (_actor.Search != null) _actor.Search.Cancel(true);

            _cooldownUntil = Time.time + _cfg.elbowCooldown;
            _windupUntil = Time.time + _cfg.elbowWindup;
            _pending = true;
            return true;
        }

        void Update()
        {
            if (!_pending) return;
            if (Time.time < _windupUntil) return;

            _pending = false;
            Resolve();
        }

        void Resolve()
        {
            var victims = FindVictimsInCone();
            for (int i = 0; i < victims.Count; i++)
            {
                var v = victims[i];
                var dir = v.transform.position - transform.position;
                dir.y = 0f;

                v.ReceiveElbow(_actor, dir, _cfg.elbowKnockback, _cfg.staggerDuration);
                RoomEvents.RaiseElbowHit(_actor, v);

                if (_cfg.elbowKnocksOutItem && !v.Inventory.IsEmpty)
                {
                    var popDir = dir.normalized + Vector3.up * 1.4f;
                    v.DropLatestItem(popDir);
                }
            }

            if (victims.Count > 0)
                RoomEvents.RaiseScreenShake(0.1f, 0.1f);
        }

        /// <summary>锥形范围内的对手。会考虑高度差。</summary>
        public List<PlayerActor> FindVictimsInCone()
        {
            var result = new List<PlayerActor>();
            var mgr = RoomManager.Instance;
            if (mgr == null || _cfg == null) return result;

            Vector3 origin = _actor.elbowOrigin != null
                ? _actor.elbowOrigin.position
                : transform.position + Vector3.up * 0.8f;

            float rangeSqr = _cfg.elbowRange * _cfg.elbowRange;
            float cosLimit = Mathf.Cos(_cfg.elbowAngle * Mathf.Deg2Rad);

            for (int i = 0; i < mgr.players.Count; i++)
            {
                var p = mgr.players[i];
                if (p == null || p == _actor || !p.IsAlive) continue;
                if (p.IsInPitfall) continue;

                var to = p.transform.position - origin;

                // ★高度差限制：不能打到比自己高 1.2 米以上的人（他在桌上你在地下）
                if (Mathf.Abs(to.y) > 1.2f) continue;

                var flat = to;
                flat.y = 0f;
                if (flat.sqrMagnitude > rangeSqr) continue;

                var fwd = transform.forward;
                fwd.y = 0f;
                if (flat.sqrMagnitude > 0.0001f && fwd.sqrMagnitude > 0.0001f)
                {
                    if (Vector3.Dot(fwd.normalized, flat.normalized) < cosLimit) continue;
                }
                result.Add(p);
            }
            return result;
        }

        void OnDrawGizmosSelected()
        {
            if (_cfg == null || _actor == null) return;

            Vector3 origin = _actor.elbowOrigin != null
                ? _actor.elbowOrigin.position
                : transform.position + Vector3.up * 0.8f;

            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.4f);
            var fwd = transform.forward;
            var l = Quaternion.Euler(0f, -_cfg.elbowAngle, 0f) * fwd;
            var r = Quaternion.Euler(0f, _cfg.elbowAngle, 0f) * fwd;
            Gizmos.DrawLine(origin, origin + l * _cfg.elbowRange);
            Gizmos.DrawLine(origin, origin + r * _cfg.elbowRange);
        }
    }
}
