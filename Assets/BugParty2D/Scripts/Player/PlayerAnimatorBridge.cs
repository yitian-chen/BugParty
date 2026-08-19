using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 动画桥接层。把 PlayerActor 的状态翻译成 Animator 参数。
    ///
    /// 【设计意图】
    /// 玩法脚本（PlayerActor / SearchAbility / ElbowAbility）完全不知道 Animator 的存在。
    /// 所有动画驱动都集中在这一个文件里，这样做的好处：
    ///   · 换美术资源不需要碰任何玩法代码
    ///   · 美术那边改了参数名，只改这里的字符串常量
    ///   · 不挂这个组件游戏照常能跑（占位胶囊体模式）
    ///
    /// 【用法】
    /// 1. 把这个组件挂在玩家根节点（和 PlayerActor 同级）
    /// 2. animator 字段拖入角色模型上的 Animator
    /// 3. 在 Animator Controller 里建下面这些参数（名字必须一致）
    ///
    /// 【需要的 Animator 参数】
    ///   Float  Speed          水平移动速度，0 → 待机，越大跑越快
    ///   Float  VerticalSpeed  垂直速度，正=上升 负=下落
    ///   Bool   Grounded       是否在地面
    ///   Bool   Searching      是否正在搜索（翻箱动作）
    ///   Bool   Staggered      是否处于被击退硬直
    ///   Trigger Jump          起跳瞬间
    ///   Trigger Land          落地瞬间
    ///   Trigger Elbow         挥肘瞬间
    ///   Trigger GetHit        被肘击命中
    ///   Trigger Pitfall       踩空掉洞
    ///
    /// 【最小可用配置】
    /// 只做 Speed + Grounded + Jump 三个参数就已经能看出效果了，
    /// 其余可以后续逐个补。缺少的参数不会报错——本脚本用 HasParameter 做了保护。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerAnimatorBridge : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("角色模型上的 Animator。留空会自动在子物体里找。")]
        public Animator animator;

        [Header("调参")]
        [Tooltip("跑步动画对应的速度上限，用于把速度归一化到 0~1。\n" +
                 "填 0 则直接输出原始速度（适合 Blend Tree 用真实速度做阈值）。")]
        public float speedNormalizeMax = 6f;

        [Tooltip("Speed 参数的平滑时间，避免松开摇杆时动画瞬间切断")]
        [Min(0f)] public float speedDamping = 0.08f;

        // ── Animator 参数名。美术改了名字只改这里 ──
        const string P_Speed = "Speed";
        const string P_VerticalSpeed = "VerticalSpeed";
        const string P_Grounded = "Grounded";
        const string P_Searching = "Searching";
        const string P_Staggered = "Staggered";
        const string T_Jump = "Jump";
        const string T_Land = "Land";
        const string T_Elbow = "Elbow";
        const string T_GetHit = "GetHit";
        const string T_Pitfall = "Pitfall";

        PlayerActor _actor;
        float _speedSmoothed;
        float _speedVel;

        // 缓存参数是否存在，避免每帧字符串查找 + 缺参数时的报错
        bool _hasSpeed, _hasVertical, _hasGrounded, _hasSearching, _hasStaggered;
        bool _hasJump, _hasLand, _hasElbow, _hasGetHit, _hasPitfall;

        // 上一帧状态，用于检测跳变
        bool _prevSearching;

        void Awake()
        {
            _actor = GetComponent<PlayerActor>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            if (animator != null) CacheParameters();
        }

        void CacheParameters()
        {
            _hasSpeed = HasParam(P_Speed);
            _hasVertical = HasParam(P_VerticalSpeed);
            _hasGrounded = HasParam(P_Grounded);
            _hasSearching = HasParam(P_Searching);
            _hasStaggered = HasParam(P_Staggered);
            _hasJump = HasParam(T_Jump);
            _hasLand = HasParam(T_Land);
            _hasElbow = HasParam(T_Elbow);
            _hasGetHit = HasParam(T_GetHit);
            _hasPitfall = HasParam(T_Pitfall);
        }

        bool HasParam(string name)
        {
            if (animator == null) return false;
            var ps = animator.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].name == name) return true;
            return false;
        }

        void OnEnable()
        {
            RoomEvents.OnJump += HandleJump;
            RoomEvents.OnLand += HandleLand;
            RoomEvents.OnElbowHit += HandleElbowHit;
            RoomEvents.OnPlayerPitfall += HandlePitfall;
        }

        void OnDisable()
        {
            RoomEvents.OnJump -= HandleJump;
            RoomEvents.OnLand -= HandleLand;
            RoomEvents.OnElbowHit -= HandleElbowHit;
            RoomEvents.OnPlayerPitfall -= HandlePitfall;
        }

        void Update()
        {
            if (animator == null || _actor == null) return;

            // ── 移动速度 ──
            if (_hasSpeed)
            {
                float raw = _actor.HorizontalSpeed;
                if (speedNormalizeMax > 0.01f)
                    raw = Mathf.Clamp01(raw / speedNormalizeMax);

                _speedSmoothed = Mathf.SmoothDamp(
                    _speedSmoothed, raw, ref _speedVel, speedDamping);
                animator.SetFloat(P_Speed, _speedSmoothed);
            }

            // ── 垂直状态 ──
            if (_hasVertical)
                animator.SetFloat(P_VerticalSpeed, _actor.VerticalVelocity);

            if (_hasGrounded)
                animator.SetBool(P_Grounded, _actor.IsGrounded);

            // ── 搜索：用 Bool 而非 Trigger，因为它是持续状态 ──
            if (_hasSearching)
            {
                bool searching = _actor.IsSearching;
                if (searching != _prevSearching)
                {
                    animator.SetBool(P_Searching, searching);
                    _prevSearching = searching;
                }
            }

            if (_hasStaggered)
                animator.SetBool(P_Staggered, _actor.IsStaggered);
        }

        // ══════════════════════════════════════════════
        //  事件回调
        // ══════════════════════════════════════════════

        void HandleJump(PlayerActor who)
        {
            if (who != _actor || animator == null) return;
            if (_hasJump) animator.SetTrigger(T_Jump);
        }

        void HandleLand(PlayerActor who, float fallHeight)
        {
            if (who != _actor || animator == null) return;
            if (_hasLand) animator.SetTrigger(T_Land);
        }

        void HandleElbowHit(PlayerActor attacker, PlayerActor victim)
        {
            if (animator == null) return;

            // 同一个事件里分别处理「我打人」和「我被打」
            if (attacker == _actor && _hasElbow) animator.SetTrigger(T_Elbow);
            if (victim == _actor && _hasGetHit) animator.SetTrigger(T_GetHit);
        }

        void HandlePitfall(PlayerActor who)
        {
            if (who != _actor || animator == null) return;
            if (_hasPitfall) animator.SetTrigger(T_Pitfall);
        }
    }
}
