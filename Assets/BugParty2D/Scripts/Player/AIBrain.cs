using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// AI 控制器。相比俯视基础版增加两个能力：
    ///   ★1. 探测前方地板是否塌陷，主动绕路
    ///   ★2. 会跳（跳上桌子、跳过小洞）
    /// </summary>
    public class AIBrain : PlayerBrain
    {
        enum AIState { SeekContainer, Searching, Chase, Wander }

        [Header("性格")]
        [Range(-0.5f, 0.5f)] public float aggressionBias = 0f;
        [Range(0f, 0.5f)] public float noise = 0.15f;

        AIState _state = AIState.SeekContainer;
        SearchContainer _targetContainer;
        PlayerActor _targetEnemy;
        Vector3 _wanderPoint;

        float _nextDecision;
        float _stateEnterTime;
        float _nextJumpAllowed;
        Vector3 _detourDir;
        float _detourUntil;

        protected override void Start()
        {
            base.Start();
            _nextDecision = Time.time + Random.Range(0f, 0.4f);
            PickWanderPoint();
        }

        protected override void Think()
        {
            if (Time.time >= _nextDecision)
            {
                Decide();
                float interval = Cfg != null ? Cfg.aiDecisionInterval : 0.35f;
                _nextDecision = Time.time + interval + Random.Range(0f, noise * 0.5f);
            }
            Act();
        }

        // ── 决策 ───────────────────────────────────────

        void Decide()
        {
            var mgr = RoomManager.Instance;
            if (mgr == null || Cfg == null) return;

            // 背包满了 → 专心搞事
            if (Actor.Inventory.IsFull)
            {
                var prey = mgr.FindNearestOpponent(Actor, Cfg.aiAggroRange * 2f);
                if (prey != null) { EnterChase(prey); return; }
                EnterWander();
                return;
            }

            if (Actor.Search.IsSearching)
            {
                _state = AIState.Searching;
                var threat = mgr.FindNearestOpponent(Actor, Cfg.elbowRange);
                if (threat != null && Roll(Cfg.aiAggressiveness * 0.5f)) EnterChase(threat);
                return;
            }

            var opponent = mgr.FindNearestOpponent(Actor, Cfg.aiAggroRange);
            if (opponent != null)
            {
                float weight = Cfg.aiAggressiveness + aggressionBias;
                if (opponent.Search != null && opponent.Search.IsSearching) weight += 0.35f;
                if (!opponent.Inventory.IsEmpty) weight += 0.15f;

                if (Roll(weight)) { EnterChase(opponent); return; }
            }

            var c = mgr.FindNearestAvailableContainer(transform.position, Actor);
            if (c != null) { EnterSeek(c); return; }

            EnterWander();
        }

        bool Roll(float chance)
            => Random.value < Mathf.Clamp01(chance + Random.Range(-noise, noise));

        void EnterSeek(SearchContainer c)
        {
            _state = AIState.SeekContainer;
            _targetContainer = c;
            _targetEnemy = null;
            _stateEnterTime = Time.time;
        }

        void EnterChase(PlayerActor p)
        {
            _state = AIState.Chase;
            _targetEnemy = p;
            _targetContainer = null;
            _stateEnterTime = Time.time;
        }

        void EnterWander()
        {
            if (_state != AIState.Wander) PickWanderPoint();
            _state = AIState.Wander;
            _targetContainer = null;
            _targetEnemy = null;
        }

        // ── 执行 ───────────────────────────────────────

        void Act()
        {
            switch (_state)
            {
                case AIState.SeekContainer: ActSeek(); break;
                case AIState.Searching:     ActSearching(); break;
                case AIState.Chase:         ActChase(); break;
                case AIState.Wander:        ActWander(); break;
            }
        }

        void ActSeek()
        {
            if (_targetContainer == null || !_targetContainer.IsAvailableFor(Actor))
            {
                EnterWander();
                return;
            }

            var goal = _targetContainer.InteractPoint;
            var to = goal - transform.position;
            to.y = 0f;

            float range = Cfg != null ? Cfg.searchRange : 1.7f;
            if (to.magnitude <= range * 0.8f)
            {
                Actor.MoveInput = Vector2.zero;

                // ★容器在桌子上（比自己高）→ 先跳上去
                float heightDiff = goal.y - transform.position.y;
                if (heightDiff > 0.5f && Actor.IsGrounded && CanJump())
                {
                    Actor.WantJump = true;
                    _nextJumpAllowed = Time.time + 0.8f;
                    return;
                }

                if (Actor.Search.TryBegin(_targetContainer)) _state = AIState.Searching;
                else EnterWander();
                return;
            }

            MoveWithPitAvoidance(goal);

            if (Time.time - _stateEnterTime > 5f) EnterWander();
        }

        void ActSearching()
        {
            Actor.MoveInput = Vector2.zero;
            if (!Actor.Search.IsSearching) EnterWander();
        }

        void ActChase()
        {
            if (_targetEnemy == null || !_targetEnemy.IsAlive || _targetEnemy.IsInPitfall)
            {
                EnterWander();
                return;
            }

            var to = _targetEnemy.transform.position - transform.position;
            float heightDiff = to.y;
            to.y = 0f;
            float dist = to.magnitude;

            float elbowRange = Cfg != null ? Cfg.elbowRange : 1.6f;

            if (dist <= elbowRange * 0.85f)
            {
                Actor.MoveInput = Vector2.zero;

                if (to.sqrMagnitude > 0.0001f)
                {
                    var look = Quaternion.LookRotation(to.normalized, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation, look, 900f * Time.deltaTime);
                }

                // ★对手在桌上 → 跳上去打
                if (heightDiff > 0.6f && Actor.IsGrounded && CanJump())
                {
                    Actor.WantJump = true;
                    _nextJumpAllowed = Time.time + 0.8f;
                    return;
                }

                float dot = Vector3.Dot(transform.forward, to.normalized);
                if (dot > 0.75f && Mathf.Abs(heightDiff) < 1.2f) Actor.Elbow.TryElbow();
            }
            else
            {
                MoveWithPitAvoidance(_targetEnemy.transform.position);
            }

            if (Time.time - _stateEnterTime > 3.5f) EnterWander();
        }

        void ActWander()
        {
            var to = _wanderPoint - transform.position;
            to.y = 0f;

            if (to.magnitude < 0.7f) { PickWanderPoint(); return; }
            MoveWithPitAvoidance(_wanderPoint);
        }

        // ══════════════════════════════════════════════
        //  ★绕路：探测前方地板是否塌陷
        // ══════════════════════════════════════════════

        void MoveWithPitAvoidance(Vector3 goal)
        {
            var mgr = RoomManager.Instance;
            var to = goal - transform.position;
            to.y = 0f;

            if (to.sqrMagnitude < 0.0001f)
            {
                Actor.MoveInput = Vector2.zero;
                return;
            }

            var desired = to.normalized;

            // 绕行状态中：沿着上次算出的绕行方向继续走一小段
            if (Time.time < _detourUntil)
            {
                Actor.MoveInput = new Vector2(_detourDir.x, _detourDir.z);
                return;
            }

            if (mgr != null && mgr.floorGrid != null && Cfg != null)
            {
                float probe = Cfg.aiPitAvoidDistance;
                var ahead = transform.position + desired * probe;

                if (mgr.floorGrid.IsHoleAt(ahead))
                {
                    // 前方是洞：试左右各 45° / 90°，挑第一个安全的方向
                    var candidates = new[] { 45f, -45f, 80f, -80f, 120f, -120f };
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        var dir = Quaternion.Euler(0f, candidates[i], 0f) * desired;
                        if (!mgr.floorGrid.IsHoleAt(transform.position + dir * probe))
                        {
                            _detourDir = dir;
                            _detourUntil = Time.time + 0.5f;
                            Actor.MoveInput = new Vector2(dir.x, dir.z);
                            return;
                        }
                    }

                    // 四面都是洞：原地不动，等终局
                    Actor.MoveInput = Vector2.zero;
                    return;
                }
            }

            Actor.MoveInput = new Vector2(desired.x, desired.z);
        }

        bool CanJump()
        {
            if (Cfg == null || !Cfg.aiCanJump) return false;
            return Time.time >= _nextJumpAllowed;
        }

        void PickWanderPoint()
        {
            var mgr = RoomManager.Instance;
            if (mgr != null && mgr.containers.Count > 0)
            {
                var c = mgr.containers[Random.Range(0, mgr.containers.Count)];
                if (c != null)
                {
                    _wanderPoint = c.InteractPoint + new Vector3(
                        Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1.5f, 1.5f));
                    return;
                }
            }
            _wanderPoint = transform.position + new Vector3(
                Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
        }
    }
}
