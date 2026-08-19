using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// ★房间警报系统。红色灯光呼吸闪烁，剩余时间越少越急促、越红。
    /// 这是「请尽快修复BUG」紧迫感的第二层。
    /// </summary>
    public class AlarmSystem : MonoBehaviour
    {
        [Header("引用（建场工具填）")]
        [Tooltip("警报红光。建议用多个点光源分布在房间四周")]
        public Light[] alarmLights;

        [Tooltip("警报灯的实体（墙上的红灯罩），会同步变亮")]
        public Renderer[] alarmLampRenderers;

        [Header("颜色")]
        public Color alarmColor = new Color(1f, 0.15f, 0.12f);

        [Tooltip("灯罩熄灭时的颜色")]
        public Color lampOffColor = new Color(0.28f, 0.10f, 0.10f);

        RoomConfig _cfg;
        float _phase;
        bool _active;

        void Start()
        {
            _cfg = RoomManager.Instance != null ? RoomManager.Instance.config : null;
            SetIntensity(0f);
        }

        void OnEnable()
        {
            RoomEvents.OnPhaseChanged += HandlePhase;
        }

        void OnDisable()
        {
            RoomEvents.OnPhaseChanged -= HandlePhase;
        }

        void HandlePhase(RoundPhase p)
        {
            // 搜索阶段开始亮警报，终局塌陷时全亮
            _active = p == RoundPhase.Searching || p == RoundPhase.Collapse;

            if (p == RoundPhase.Collapse) SetIntensity(1f);
            else if (!_active) SetIntensity(0f);
        }

        void Update()
        {
            if (!_active || _cfg == null) return;

            var mgr = RoomManager.Instance;
            if (mgr == null) return;

            // 终局塌陷：常亮不闪，最大强度
            if (mgr.Phase == RoundPhase.Collapse)
            {
                SetIntensity(1f);
                return;
            }

            // 搜索阶段：按剩余时间决定闪烁频率
            float urgency = 0f;
            if (mgr.TimeLeft <= _cfg.urgentThreshold && _cfg.urgentThreshold > 0f)
                urgency = 1f - Mathf.Clamp01(mgr.TimeLeft / _cfg.urgentThreshold);

            float period = Mathf.Lerp(_cfg.alarmPeriodNormal, _cfg.alarmPeriodUrgent, urgency);
            _phase += Time.deltaTime / Mathf.Max(0.05f, period);
            if (_phase > 1f) _phase -= 1f;

            // 呼吸曲线：不是简单开关，而是有起落的脉冲
            float k = Mathf.Pow(Mathf.Sin(_phase * Mathf.PI), 2f);

            // 紧张时基础亮度也抬高，形成"整个房间都在红"的压迫感
            float baseLevel = Mathf.Lerp(0f, 0.35f, urgency);
            SetIntensity(baseLevel + k * (1f - baseLevel));
        }

        void SetIntensity(float t01)
        {
            if (_cfg == null) return;
            t01 = Mathf.Clamp01(t01);

            if (alarmLights != null)
            {
                for (int i = 0; i < alarmLights.Length; i++)
                {
                    if (alarmLights[i] == null) continue;
                    alarmLights[i].color = alarmColor;
                    alarmLights[i].intensity = t01 * _cfg.alarmIntensity;
                    alarmLights[i].enabled = t01 > 0.01f;
                }
            }

            if (alarmLampRenderers != null)
            {
                var c = Color.Lerp(lampOffColor, alarmColor, t01);
                for (int i = 0; i < alarmLampRenderers.Length; i++)
                {
                    if (alarmLampRenderers[i] == null) continue;
                    var m = alarmLampRenderers[i].material;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", c);

                    // 若材质支持自发光，同步提亮，视觉上更像真的亮了。
                    // 注意：Standard/Lit 材质需要在材质面板手动勾选 Emission 才生效
                    if (m.HasProperty("_EmissionColor"))
                        m.SetColor("_EmissionColor", alarmColor * t01 * 2.2f);
                }
            }
        }
    }
}
