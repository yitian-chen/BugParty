using System.Collections;
using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 2D 俯视角玩家。虽然观感是 2D，但内部是完整的 3D 移动 + 垂直轴，
    /// 这样才能实现「跳上桌子」和「掉进洞里」。
    ///
    /// 用 CharacterController 而非 Rigidbody：垂直轴完全自己控制，手感可预测。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerInventory))]
    [RequireComponent(typeof(SearchAbility))]
    [RequireComponent(typeof(ElbowAbility))]
    public class PlayerActor : MonoBehaviour
    {
        [Header("身份")]
        public PlayerColor playerColor = PlayerColor.Red;
        public string displayName = "";

        [Header("引用（建场工具填）")]
        public Renderer bodyRenderer;
        public Transform visualRoot;
        public Transform handAnchor;
        public Transform elbowOrigin;

        [Tooltip("落在地上的阴影，用于在 2D 俯视下表现高度")]
        public Transform shadowQuad;

        // ── 组件 ───────────────────────────────────────
        public PlayerInventory Inventory { get; private set; }
        public SearchAbility Search { get; private set; }
        public ElbowAbility Elbow { get; private set; }
        public CharacterController Controller { get; private set; }

        RoomConfig _cfg;
        Vector3 _spawnPos;
        Quaternion _spawnRot;

        // ── 水平移动 ───────────────────────────────────
        Vector3 _velocity;
        Vector3 _velSmooth;

        // ── 垂直（跳跃/坠落）─────────────────────────
        float _vertical;
        VerticalState _vState = VerticalState.Grounded;
        float _lastGroundedTime = -99f;
        float _jumpPressedTime = -99f;
        float _highestY;
        bool _pitfallActive;

        // ── 状态 ───────────────────────────────────────
        float _staggerUntil;

        public bool IsStaggered => Time.time < _staggerUntil;
        public bool IsAlive { get; private set; } = true;
        public VerticalState Vertical => _vState;
        public bool IsGrounded => _vState == VerticalState.Grounded;
        public bool IsAirborne => _vState == VerticalState.Rising || _vState == VerticalState.Falling;
        public bool IsInPitfall => _vState == VerticalState.Pitfall;

        /// <summary>当前离地高度，用于阴影缩放与"是否站在桌上"的判断。</summary>
        public float HeightAboveGround { get; private set; }

        public float HorizontalSpeed => new Vector2(_velocity.x, _velocity.z).magnitude;

        /// <summary>垂直速度。正=上升，负=下落。供动画桥接层驱动跳跃/下落动画。</summary>
        public float VerticalVelocity => _vertical;

        /// <summary>是否正在搜索容器。转发 SearchAbility，方便动画与音效层直接读。</summary>
        public bool IsSearching => Search != null && Search.IsSearching;

        // ── 输入（由控制器写入）─────────────────────
        public Vector2 MoveInput { get; set; }
        public bool WantJump { get; set; }

        void Awake()
        {
            Controller = GetComponent<CharacterController>();
            Inventory = GetComponent<PlayerInventory>();
            Search = GetComponent<SearchAbility>();
            Elbow = GetComponent<ElbowAbility>();

            _spawnPos = transform.position;
            _spawnRot = transform.rotation;

            if (string.IsNullOrEmpty(displayName))
                displayName = playerColor.ToLabel() + "方";
        }

        void Start()
        {
            _cfg = RoomManager.Instance != null ? RoomManager.Instance.config : null;
            if (_cfg == null)
            {
                Debug.LogError($"[{displayName}] 找不到 RoomConfig。", this);
                enabled = false;
                return;
            }

            Inventory.Init(this, _cfg.inventoryCapacity);
            Search.Init(this, _cfg);
            Elbow.Init(this, _cfg);

            ApplyTeamColor();
        }

        public void ApplyTeamColor()
        {
            if (bodyRenderer == null) return;
            var c = playerColor.ToColor();
            var m = bodyRenderer.material;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        void Update()
        {
            if (!IsAlive || _cfg == null) return;

            UpdateGroundState();
            UpdateHorizontal();
            UpdateVertical();
            UpdateShadow();
            CheckPitfall();
        }

        // ══════════════════════════════════════════════
        //  地面检测
        // ══════════════════════════════════════════════

        void UpdateGroundState()
        {
            if (_pitfallActive) return;

            // ★用 SphereCast 而不是单线射线。
            // 单线从中心往下打，站在桌子边缘时会漏检导致"站着却在掉"的抖动。
            float radius = Controller != null ? Controller.radius * 0.9f : 0.34f;
            Vector3 origin = transform.position + Vector3.up * (radius + 0.05f);

            float probeLen = 3.5f;   // 探测得远一点，跳在空中也能算出离地高度
            float snapDist = _cfg.groundCheckDistance + radius + 0.1f;

            bool grounded = false;
            float surfaceY = float.MinValue;

            var hits = Physics.SphereCastAll(origin, radius, Vector3.down, probeLen);
            for (int i = 0; i < hits.Length; i++)
            {
                // 忽略自己、掉落物、以及触发器
                if (hits[i].collider.GetComponentInParent<PlayerActor>() == this) continue;
                if (hits[i].collider.GetComponentInParent<WorldItem>() != null) continue;
                if (hits[i].collider.isTrigger) continue;

                // SphereCast 起点若已重叠会返回 distance=0 且 point 为零向量，跳过
                if (hits[i].distance <= 0f) continue;

                float y = hits[i].point.y;
                // 只认脚底附近或更低的面，避免把头顶的桌子当地面
                if (y > transform.position.y + 0.2f) continue;

                if (y > surfaceY) surfaceY = y;
            }

            if (surfaceY > float.MinValue)
            {
                HeightAboveGround = Mathf.Max(0f, transform.position.y - surfaceY);
                // 距离够近 + 没有上升速度 → 算落地
                if (HeightAboveGround <= snapDist && _vertical <= 0.01f) grounded = true;
            }
            else
            {
                // 脚下什么都没有（洞里 / 地图外）
                HeightAboveGround = 99f;
            }

            if (grounded)
            {
                if (_vState != VerticalState.Grounded)
                {
                    float fallFrom = Mathf.Max(0f, _highestY - surfaceY);
                    RoomEvents.RaiseLand(this, fallFrom);
                    if (fallFrom > 1.2f) RoomEvents.RaiseScreenShake(0.08f, 0.12f);
                }

                _vState = VerticalState.Grounded;
                _vertical = 0f;
                _lastGroundedTime = Time.time;
                _highestY = transform.position.y;
            }
            else if (_vState == VerticalState.Grounded)
            {
                // 脚下没东西了（走到洞边缘或桌子边缘）→ 开始下落
                _vState = VerticalState.Falling;
            }
        }

        // ══════════════════════════════════════════════
        //  水平移动
        // ══════════════════════════════════════════════

        void UpdateHorizontal()
        {
            bool canMove = CanAct();

            Vector3 target = Vector3.zero;
            if (canMove)
            {
                var dir = new Vector3(MoveInput.x, 0f, MoveInput.y);
                if (dir.sqrMagnitude > 1f) dir.Normalize();

                float speedMul = 1f;
                // 空中操控受限，跳跃有"承诺感"
                if (IsAirborne) speedMul = _cfg.airControl;

                target = dir * _cfg.moveSpeed * speedMul;

                // 朝向：只在有输入时转，且地面上转得更快
                if (dir.sqrMagnitude > 0.0001f)
                {
                    var look = Quaternion.LookRotation(dir, Vector3.up);
                    float turn = _cfg.turnSpeed * (IsAirborne ? 0.5f : 1f);
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation, look, turn * Time.deltaTime);
                }
            }

            // 硬直和坠落时保留惯性，不强行归零
            float smooth = (IsStaggered || _pitfallActive) ? 0.3f : _cfg.moveSmoothing;
            _velocity = Vector3.SmoothDamp(_velocity, target, ref _velSmooth, smooth);
        }

        bool CanAct()
        {
            if (!IsAlive || IsStaggered || _pitfallActive) return false;

            var mgr = RoomManager.Instance;
            if (mgr == null || !mgr.CanAct) return false;

            if (Search != null && Search.IsSearching) return false;
            return true;
        }

        // ══════════════════════════════════════════════
        //  ★垂直（跳跃）
        // ══════════════════════════════════════════════

        void UpdateVertical()
        {
            if (_pitfallActive) return;

            // 记录跳跃按键，实现输入缓冲
            if (WantJump)
            {
                _jumpPressedTime = Time.time;
                WantJump = false;
            }

            bool wantJumpBuffered = Time.time - _jumpPressedTime <= _cfg.jumpBuffer;
            bool coyoteOk = Time.time - _lastGroundedTime <= _cfg.coyoteTime;

            // 郊狼时间 + 输入缓冲：走出桌沿的瞬间还能跳，手感宽容得多
            if (wantJumpBuffered && coyoteOk && !IsStaggered && CanJumpNow())
            {
                _vertical = _cfg.jumpVelocity;
                _vState = VerticalState.Rising;
                _jumpPressedTime = -99f;
                _lastGroundedTime = -99f;
                RoomEvents.RaiseJump(this);
            }

            // 重力
            if (_vState != VerticalState.Grounded)
            {
                _vertical -= _cfg.gravity * Time.deltaTime;
                if (_vertical < 0f && _vState == VerticalState.Rising)
                    _vState = VerticalState.Falling;
            }

            _highestY = Mathf.Max(_highestY, transform.position.y);

            var motion = _velocity;
            motion.y = _vertical;
            Controller.Move(motion * Time.deltaTime);
        }

        bool CanJumpNow()
        {
            var mgr = RoomManager.Instance;
            if (mgr == null || !mgr.CanAct) return false;
            if (Search != null && Search.IsSearching) return false;
            return true;
        }

        // ══════════════════════════════════════════════
        //  ★阴影（2D 俯视下表现高度的关键）
        // ══════════════════════════════════════════════

        void UpdateShadow()
        {
            if (shadowQuad == null) return;

            // 阴影是独立物体（不是子物体），需要手动同步 XZ
            float h = HeightAboveGround > 50f ? 3f : HeightAboveGround;

            shadowQuad.position = new Vector3(
                transform.position.x,
                transform.position.y - h + 0.04f,
                transform.position.z);

            // 保持朝上平铺，不跟随角色旋转
            shadowQuad.rotation = Quaternion.Euler(90f, 0f, 0f);

            // ★离地越高，阴影越小越淡 —— 这是 2D 俯视判断高度的核心线索
            float k = Mathf.Clamp01(h / 3f);
            float scale = Mathf.Lerp(1.05f, 0.42f, k);
            shadowQuad.localScale = new Vector3(scale, scale, 1f);

            var r = shadowQuad.GetComponent<Renderer>();
            if (r != null)
            {
                var m = r.material;
                var c = Color.black;
                c.a = Mathf.Lerp(0.45f, 0.10f, k);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            }

            // 掉进洞里时藏掉阴影
            bool visible = !_pitfallActive && HeightAboveGround < 50f;
            if (shadowQuad.gameObject.activeSelf != visible)
                shadowQuad.gameObject.SetActive(visible);
        }

        // ══════════════════════════════════════════════
        //  ★掉进洞里
        // ══════════════════════════════════════════════

        void CheckPitfall()
        {
            if (_pitfallActive || !IsAlive) return;

            var mgr = RoomManager.Instance;
            if (mgr == null) return;

            // 终局塌陷阶段不触发惩罚，那是剧情性掉落
            if (mgr.Phase == RoundPhase.Collapse || mgr.Phase == RoundPhase.Transition) return;

            // 掉得比地板基准面低于阈值 → 判定坠落
            float baseY = mgr.floorGrid != null ? mgr.floorGrid.origin.y : 0f;
            if (transform.position.y < baseY - _cfg.pitfallDepth)
                StartCoroutine(PitfallRoutine());
        }

        IEnumerator PitfallRoutine()
        {
            _pitfallActive = true;
            _vState = VerticalState.Pitfall;
            RoomEvents.RaisePlayerPitfall(this);

            // 中断搜索
            if (Search != null) Search.Cancel(true);

            // 掉落惩罚：丢道具
            if (_cfg.pitfallItemLoss > 0)
            {
                for (int i = 0; i < _cfg.pitfallItemLoss; i++)
                {
                    var item = Inventory.PopLatest();
                    if (item == null) break;
                    // 道具掉在坠落前的位置上方，方便别人捡
                    var mgr2 = RoomManager.Instance;
                    var safe = mgr2 != null && mgr2.floorGrid != null
                        ? mgr2.floorGrid.FindNearestSafePosition(transform.position)
                        : transform.position + Vector3.up * 2f;
                    WorldItem.SpawnDropped(item, safe, Vector3.up + Random.insideUnitSphere * 0.3f, _cfg);
                    RoomEvents.RaiseItemKnockedOut(this, item);
                }
            }

            // 继续下坠一段时间，给足"掉进去了"的反馈
            float t = 0f;
            while (t < _cfg.pitfallDuration)
            {
                t += Time.deltaTime;
                _vertical -= _cfg.gravity * Time.deltaTime;
                var motion = _velocity * 0.4f;
                motion.y = _vertical;
                Controller.Move(motion * Time.deltaTime);

                // 下坠时旋转视觉体，滑稽感
                if (visualRoot != null)
                    visualRoot.Rotate(Vector3.right, 320f * Time.deltaTime, Space.Self);

                yield return null;
            }

            // 弹回最近的安全地板
            var mgr3 = RoomManager.Instance;
            Vector3 respawn = mgr3 != null && mgr3.floorGrid != null
                ? mgr3.floorGrid.FindNearestSafePosition(transform.position)
                : _spawnPos;

            Controller.enabled = false;
            transform.position = respawn;
            Controller.enabled = true;

            if (visualRoot != null) visualRoot.localRotation = Quaternion.identity;

            _vertical = 0f;
            _velocity = Vector3.zero;
            _velSmooth = Vector3.zero;
            _vState = VerticalState.Falling;
            _pitfallActive = false;

            // 落地后还要硬直一会，这是坠落的真实代价
            _staggerUntil = Time.time + _cfg.pitfallStagger;
            RoomEvents.RaisePlayerRecovered(this);
            RoomEvents.RaiseScreenShake(0.15f, 0.2f);
        }

        // ══════════════════════════════════════════════
        //  受击
        // ══════════════════════════════════════════════

        public void ReceiveElbow(PlayerActor attacker, Vector3 knockDir, float force, float staggerTime)
        {
            if (!IsAlive) return;

            _staggerUntil = Time.time + staggerTime;
            if (Search != null) Search.Cancel(true);

            knockDir.y = 0f;
            if (knockDir.sqrMagnitude < 0.0001f) knockDir = -transform.forward;

            _velocity = knockDir.normalized * force;

            // ★把人从桌子上打下来：给一点向上的初速度，形成抛物线飞出去
            if (_cfg.elbowCanKnockOffLedge && HeightAboveGround > 0.4f)
            {
                _vertical = 3.2f;
                _vState = VerticalState.Rising;
            }
        }

        public void DropLatestItem(Vector3 popDir)
        {
            if (_cfg == null) return;

            var item = Inventory.PopLatest();
            if (item == null) return;

            var origin = handAnchor != null ? handAnchor.position : transform.position + Vector3.up;
            WorldItem.SpawnDropped(item, origin, popDir, _cfg);
            RoomEvents.RaiseItemKnockedOut(this, item);
        }

        // ══════════════════════════════════════════════
        //  ★终局：掉落到下一关
        // ══════════════════════════════════════════════

        /// <summary>
        /// 终局全塌陷时调用。玩家自由下坠，不触发惩罚，播完就衔接下一关。
        /// </summary>
        public void BeginFallToNextLevel()
        {
            if (!IsAlive) return;
            StartCoroutine(FallToNextLevelRoutine());
        }

        IEnumerator FallToNextLevelRoutine()
        {
            RoomEvents.RaisePlayerFallingToNextLevel(this);

            // 中断一切操作
            if (Search != null) Search.Cancel(false);
            MoveInput = Vector2.zero;

            // ★关掉 CharacterController：直接改 transform 会和它冲突，
            // 而且残留的碰撞体会挡住下坠
            if (Controller != null) Controller.enabled = false;

            // 阴影跟着一起消失
            if (shadowQuad != null) shadowQuad.gameObject.SetActive(false);

            _vertical = 1.8f;   // 先微微向上，像被塌陷的地板弹了一下
            float t = 0f;

            while (t < 3.5f)
            {
                t += Time.deltaTime;
                _vertical -= _cfg.gravity * Time.deltaTime;

                var motion = _velocity * 0.35f;
                motion.y = _vertical;
                transform.position += motion * Time.deltaTime;

                // 边掉边翻滚，喜剧感
                if (visualRoot != null)
                {
                    visualRoot.Rotate(Vector3.right, 420f * Time.deltaTime, Space.Self);
                    visualRoot.Rotate(Vector3.up, 180f * Time.deltaTime, Space.Self);
                }
                yield return null;
            }
        }

        // ══════════════════════════════════════════════

        public void ResetForNewRound()
        {
            IsAlive = true;
            _staggerUntil = 0f;
            _velocity = Vector3.zero;
            _velSmooth = Vector3.zero;
            _vertical = 0f;
            _vState = VerticalState.Grounded;
            _pitfallActive = false;
            _lastGroundedTime = -99f;
            _jumpPressedTime = -99f;
            MoveInput = Vector2.zero;
            WantJump = false;
            HeightAboveGround = 0f;

            if (Controller != null)
            {
                Controller.enabled = false;
                transform.SetPositionAndRotation(_spawnPos, _spawnRot);
                Controller.enabled = true;
            }

            if (shadowQuad != null && !shadowQuad.gameObject.activeSelf)
                shadowQuad.gameObject.SetActive(true);

            if (visualRoot != null)
            {
                visualRoot.localRotation = Quaternion.identity;
                if (!visualRoot.gameObject.activeSelf) visualRoot.gameObject.SetActive(true);
            }

            _highestY = _spawnPos.y;

            Inventory.Clear();
            if (Search != null) Search.Cancel(false);
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }
    }
}
