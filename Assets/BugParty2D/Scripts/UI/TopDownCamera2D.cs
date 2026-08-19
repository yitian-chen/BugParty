using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// ★2D 俯视角相机。关键在于「正交投影 + 可调俯角」：
    ///
    ///   · 正交投影 → 无透视变形、远近同尺寸，这是「2D 感」的真正来源
    ///   · 俯角 70°（而非 90°）→ 保留高度可读性，玩家能看出谁站在桌子上
    ///
    /// 参考《哈迪斯》《元气骑士》，观感是 2D 但角色能跳、有高低差。
    /// 想要更「平」就把 cameraPitch 调到 85°，想要更立体就调到 55°。
    ///
    /// 同时负责画面抖动（订阅 RoomEvents.OnScreenShakeRequested）。
    /// </summary>
    public class TopDownCamera2D : MonoBehaviour
    {
        [Header("注视目标")]
        [Tooltip("房间中心。留空则用世界原点")]
        public Transform lookTarget;

        [Header("覆写（留空则用 RoomConfig 的值）")]
        [Tooltip("勾选后使用下面的本地参数而不是 Config")]
        public bool overrideConfig = false;

        [Range(50f, 90f)] public float pitch = 70f;
        public float yaw = 0f;
        [Min(3f)] public float orthoSize = 11.5f;

        RoomConfig _cfg;
        Camera _cam;

        Vector3 _pivot;
        Vector3 _pivotVel;
        float _size;
        float _sizeVel;

        // 画面抖动
        float _shakeAmount;
        float _shakeUntil;
        Vector3 _shakeOffset;

        // 故障撕裂（Bug 感）
        float _glitchUntil;
        Vector3 _glitchOffset;

        void Awake()
        {
            _cam = GetComponent<Camera>();
        }

        void OnEnable()
        {
            RoomEvents.OnScreenShakeRequested += HandleShake;
            RoomEvents.OnTileCollapsed += HandleTileCollapsed;
            RoomEvents.OnFinalCollapseStarted += HandleFinalCollapse;
        }

        void OnDisable()
        {
            RoomEvents.OnScreenShakeRequested -= HandleShake;
            RoomEvents.OnTileCollapsed -= HandleTileCollapsed;
            RoomEvents.OnFinalCollapseStarted -= HandleFinalCollapse;
        }

        void Start()
        {
            _cfg = RoomManager.Instance != null ? RoomManager.Instance.config : null;

            _pivot = lookTarget != null ? lookTarget.position : Vector3.zero;
            _size = GetTargetOrthoSize();

            if (_cam != null)
            {
                // ★正交投影是「2D 感」的核心
                _cam.orthographic = true;
                _cam.orthographicSize = _size;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0.04f, 0.05f, 0.09f);
                // 正交下近裁剪面可以给负值，避免贴近物体被裁掉
                _cam.nearClipPlane = -30f;
                _cam.farClipPlane = 120f;
            }

            Apply();
        }

        float GetPitch() => overrideConfig || _cfg == null ? pitch : _cfg.cameraPitch;
        float GetTargetOrthoSize() => overrideConfig || _cfg == null ? orthoSize : _cfg.orthographicSize;

        // ── 事件响应 ───────────────────────────────────

        void HandleShake(float amount, float duration)
        {
            _shakeAmount = Mathf.Max(_shakeAmount, amount);
            _shakeUntil = Mathf.Max(_shakeUntil, Time.time + duration);
        }

        void HandleTileCollapsed(FloorTile t)
        {
            // 每次地板塌陷都抖一下，让塌陷有重量感
            HandleShake(0.18f, 0.22f);
            _glitchUntil = Time.time + 0.12f;
        }

        void HandleFinalCollapse()
        {
            // 终局：长时间剧烈抖动
            HandleShake(0.55f, 3f);
            _glitchUntil = Time.time + 2.5f;
        }

        // ── 主循环 ─────────────────────────────────────

        void LateUpdate()
        {
            UpdateFraming();
            UpdateShake();
            UpdateGlitch();
            Apply();
        }

        void UpdateFraming()
        {
            Vector3 targetPivot = lookTarget != null ? lookTarget.position : Vector3.zero;
            float targetSize = GetTargetOrthoSize();

            bool auto = _cfg != null && _cfg.autoFrame;
            if (auto)
            {
                var mgr = RoomManager.Instance;
                if (mgr != null && mgr.players.Count > 0)
                {
                    var min = new Vector3(float.MaxValue, 0f, float.MaxValue);
                    var max = new Vector3(float.MinValue, 0f, float.MinValue);
                    int n = 0;

                    for (int i = 0; i < mgr.players.Count; i++)
                    {
                        var p = mgr.players[i];
                        if (p == null || !p.IsAlive) continue;
                        var pos = p.transform.position;
                        min.x = Mathf.Min(min.x, pos.x); min.z = Mathf.Min(min.z, pos.z);
                        max.x = Mathf.Max(max.x, pos.x); max.z = Mathf.Max(max.z, pos.z);
                        n++;
                    }

                    if (n > 0)
                    {
                        var crowd = new Vector3((min.x + max.x) * 0.5f, 0f, (min.z + max.z) * 0.5f);
                        // 与房间中心混合，避免被单个跑远的人拽走
                        targetPivot = Vector3.Lerp(targetPivot, crowd, 0.5f);

                        float spread = Mathf.Max(max.x - min.x, max.z - min.z);
                        targetSize = Mathf.Clamp(
                            spread * 0.62f + 3.5f, _cfg.minOrthoSize, _cfg.maxOrthoSize);
                    }
                }
            }

            float smooth = _cfg != null ? _cfg.cameraSmoothTime : 0.32f;
            _pivot = Vector3.SmoothDamp(_pivot, targetPivot, ref _pivotVel, smooth);
            _size = Mathf.SmoothDamp(_size, targetSize, ref _sizeVel, smooth);
        }

        void UpdateShake()
        {
            if (Time.time > _shakeUntil)
            {
                _shakeAmount = Mathf.Lerp(_shakeAmount, 0f, Time.deltaTime * 8f);
                if (_shakeAmount < 0.005f) _shakeAmount = 0f;
            }

            if (_shakeAmount <= 0f)
            {
                _shakeOffset = Vector3.Lerp(_shakeOffset, Vector3.zero, Time.deltaTime * 12f);
                return;
            }

            _shakeOffset = new Vector3(
                Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-0.4f, 0.4f)
            ) * _shakeAmount;
        }

        void UpdateGlitch()
        {
            // ★故障撕裂：短促的横向位移，模拟画面错位
            if (Time.time > _glitchUntil)
            {
                _glitchOffset = Vector3.Lerp(_glitchOffset, Vector3.zero, Time.deltaTime * 14f);
                return;
            }

            if (Random.value < 0.35f)
                _glitchOffset = new Vector3(Random.Range(-0.35f, 0.35f), 0f, 0f);
            else
                _glitchOffset = Vector3.zero;
        }

        void Apply()
        {
            float p = GetPitch();
            var rot = Quaternion.Euler(p, yaw, 0f);

            // 正交相机的"距离"只影响裁剪，不影响画面大小，取固定值即可
            const float dist = 40f;
            transform.position = _pivot - rot * Vector3.forward * dist + _shakeOffset + _glitchOffset;
            transform.rotation = rot;

            if (_cam != null) _cam.orthographicSize = _size;
        }

        /// <summary>外部调用：立即请求一次抖动。</summary>
        public void Shake(float amount, float duration) => HandleShake(amount, duration);
    }
}
