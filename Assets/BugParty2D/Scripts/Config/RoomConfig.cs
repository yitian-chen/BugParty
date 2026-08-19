using System.Collections.Generic;
using UnityEngine;

namespace BugParty.TopDown2D
{
    [System.Serializable]
    public class ThemeItemPool
    {
        public RoomTheme theme = RoomTheme.Fishing;
        public List<ItemDefinition> items = new List<ItemDefinition>();
    }

    /// <summary>
    /// 2D 俯视密室搜刮的全部参数。
    /// Assets ▸ Create ▸ BugParty2D ▸ Room Config
    /// </summary>
    [CreateAssetMenu(fileName = "RoomConfig2D", menuName = "BugParty2D/Room Config", order = 0)]
    public class RoomConfig : ScriptableObject
    {
        // ═══════════════════════════════════════════════
        [Header("═══ 回合时间 ═══")]
        [Min(0f)] public float introDuration = 3f;

        [Tooltip("搜索阶段时长")]
        [Min(1f)] public float searchDuration = 45f;

        [Tooltip("★全塌陷动画时长，从第一块塌到最后一块")]
        [Min(0.5f)] public float collapseDuration = 3.2f;

        [Tooltip("★穿越过渡时长，用于衔接下一关")]
        [Min(0.2f)] public float transitionDuration = 1.6f;

        [Tooltip("剩余多少秒开始进入紧张状态（警报加剧）")]
        [Min(0f)] public float urgentThreshold = 12f;

        // ═══════════════════════════════════════════════
        [Header("═══ 移动 ═══")]
        [Min(0.1f)] public float moveSpeed = 5.5f;

        [Tooltip("转身速度（度/秒）")]
        [Min(1f)] public float turnSpeed = 900f;

        [Tooltip("加速平滑，越小越灵敏")]
        [Range(0.01f, 0.4f)] public float moveSmoothing = 0.07f;

        [Tooltip("被肘击后的硬直")]
        [Min(0f)] public float staggerDuration = 0.8f;

        // ═══════════════════════════════════════════════
        [Header("═══ ★跳跃与高度 ═══")]
        [Tooltip("跳跃初速度。要能跳上 0.9 米高的桌子")]
        [Min(1f)] public float jumpVelocity = 7.2f;

        [Min(1f)] public float gravity = 22f;

        [Tooltip("允许离地后仍可跳跃的宽容时间（郊狼时间），手感关键")]
        [Range(0f, 0.35f)] public float coyoteTime = 0.12f;

        [Tooltip("落地前提前按跳的缓冲时间")]
        [Range(0f, 0.35f)] public float jumpBuffer = 0.12f;

        [Tooltip("空中可以微调方向的程度，0=完全不能改向 1=和地面一样灵活")]
        [Range(0f, 1f)] public float airControl = 0.65f;

        [Tooltip("落地检测的射线长度")]
        [Min(0.05f)] public float groundCheckDistance = 0.25f;

        // ═══════════════════════════════════════════════
        [Header("═══ ★地板塌陷 ═══")]
        [Tooltip("搜索阶段随机塌陷的地板总数量。你要求「数量很少」，默认 5")]
        [Range(0, 20)] public int randomCollapseCount = 5;

        [Tooltip("第一块随机塌陷出现的时间（占搜索总时长的比例）")]
        [Range(0f, 0.8f)] public float firstCollapseAt = 0.25f;

        [Tooltip("最后一块随机塌陷出现的时间（占比）")]
        [Range(0.2f, 1f)] public float lastCollapseAt = 0.85f;

        [Tooltip("★开裂预警时长。这段时间内地板闪红光但仍可通行")]
        [Min(0.2f)] public float crackWarningTime = 1.8f;

        [Tooltip("塌陷的地板下沉多深")]
        [Min(0.2f)] public float collapseDropDepth = 6f;

        [Tooltip("不允许在这些位置附近塌陷（容器与出生点的保护半径）")]
        [Min(0f)] public float collapseSafeRadius = 2.2f;

        // ═══════════════════════════════════════════════
        [Header("═══ ★掉进洞里的惩罚 ═══")]
        [Tooltip("掉落多深后判定为坠落")]
        [Min(0.5f)] public float pitfallDepth = 2.5f;

        [Tooltip("被传送回安全地板前的下坠时长")]
        [Min(0.1f)] public float pitfallDuration = 0.9f;

        [Tooltip("坠落后的硬直时间")]
        [Min(0f)] public float pitfallStagger = 1.2f;

        [Tooltip("坠落时掉落几件道具。0 表示不惩罚道具")]
        [Range(0, 3)] public int pitfallItemLoss = 1;

        // ═══════════════════════════════════════════════
        [Header("═══ 背包 ═══")]
        [Range(1, 6)] public int inventoryCapacity = 2;

        // ═══════════════════════════════════════════════
        [Header("═══ 搜索 ═══")]
        [Min(0.1f)] public float searchTime = 2.4f;
        [Min(0.1f)] public float searchRange = 1.7f;
        [Min(0f)] public float containerCooldown = 1.3f;
        [Range(1, 5)] public int containerYield = 2;

        // ═══════════════════════════════════════════════
        [Header("═══ 肘击 ═══")]
        [Min(0.1f)] public float elbowRange = 1.6f;
        [Range(10f, 180f)] public float elbowAngle = 75f;
        [Min(0f)] public float elbowCooldown = 0.85f;
        [Min(0f)] public float elbowWindup = 0.12f;
        [Min(0f)] public float elbowKnockback = 7f;

        [Tooltip("是否打落对方道具")]
        public bool elbowKnocksOutItem = true;

        [Tooltip("★能否把对手从桌子上打下来")]
        public bool elbowCanKnockOffLedge = true;

        [Min(0f)] public float itemPopForce = 5f;
        [Min(0f)] public float droppedItemPickupDelay = 0.4f;

        // ═══════════════════════════════════════════════
        [Header("═══ ★警报与故障氛围 ═══")]
        [Tooltip("常态下红色警报灯的闪烁周期（秒）")]
        [Min(0.1f)] public float alarmPeriodNormal = 2.4f;

        [Tooltip("紧张状态下的闪烁周期，越小越急促")]
        [Min(0.05f)] public float alarmPeriodUrgent = 0.45f;

        [Tooltip("警报红光的最大强度")]
        [Min(0f)] public float alarmIntensity = 2.6f;

        [Tooltip("天花板碎片的掉落间隔（秒）")]
        [Min(0.1f)] public float debrisInterval = 1.4f;

        [Tooltip("紧张状态下的碎片掉落间隔")]
        [Min(0.05f)] public float debrisIntervalUrgent = 0.35f;

        [Tooltip("每次掉落几块碎片")]
        [Range(1, 6)] public int debrisPerBurst = 2;

        [Tooltip("画面抖动的触发间隔")]
        [Min(0.2f)] public float screenShakeInterval = 6f;

        [Tooltip("紧张状态下的画面抖动间隔")]
        [Min(0.1f)] public float screenShakeIntervalUrgent = 1.8f;

        [Tooltip("画面抖动强度")]
        [Range(0f, 1f)] public float screenShakeAmount = 0.28f;

        [Tooltip("画面抖动持续时间")]
        [Min(0.05f)] public float screenShakeDuration = 0.22f;

        // ═══════════════════════════════════════════════
        [Header("═══ ★2D 俯视相机 ═══")]
        [Tooltip("俯角。90=完全垂直（看不出高度），70=推荐（保留高度可读性）")]
        [Range(50f, 90f)] public float cameraPitch = 70f;

        [Tooltip("正交视野大小，越大看得越广")]
        [Min(3f)] public float orthographicSize = 11.5f;

        [Tooltip("是否自动取景，让四人都在画面内")]
        public bool autoFrame = true;

        [Min(3f)] public float minOrthoSize = 10f;
        [Min(3f)] public float maxOrthoSize = 16f;

        [Tooltip("相机跟随平滑")]
        [Min(0.01f)] public float cameraSmoothTime = 0.32f;

        // ═══════════════════════════════════════════════
        [Header("═══ AI ═══")]
        [Min(0.05f)] public float aiDecisionInterval = 0.35f;
        [Min(0.1f)] public float aiAggroRange = 2.4f;
        [Range(0f, 1f)] public float aiAggressiveness = 0.45f;

        [Tooltip("★AI 会不会主动跳上桌子（有更好的道具时）")]
        public bool aiCanJump = true;

        [Tooltip("AI 探测前方地板是否塌陷的距离，用来绕路")]
        [Min(0.3f)] public float aiPitAvoidDistance = 1.6f;

        // ═══════════════════════════════════════════════
        [Header("═══ 道具池 ═══")]
        public List<ThemeItemPool> itemPools = new List<ThemeItemPool>();

        // ── 查询 ──────────────────────────────────────

        public List<ItemDefinition> GetPool(RoomTheme theme)
        {
            for (int i = 0; i < itemPools.Count; i++)
                if (itemPools[i] != null && itemPools[i].theme == theme && itemPools[i].items.Count > 0)
                    return itemPools[i].items;

            for (int i = 0; i < itemPools.Count; i++)
                if (itemPools[i] != null && itemPools[i].items.Count > 0)
                    return itemPools[i].items;

            return new List<ItemDefinition>();
        }

        // ═══════════════════════════════════════════════
        [Header("═══ 美术资源替换（留空则用程序生成的占位体）═══")]

        [Tooltip("★角色模型。四个槽位按 红/蓝/黄/绿 顺序填。\n" +
                 "要求：Prefab 根节点朝 +Z，脚底在 y=0，身高约 1.5 米。\n" +
                 "填了之后建场工具会用它替换胶囊体，并自动挂 Animator（若有）。")]
        public GameObject[] characterPrefabs = new GameObject[4];

        [Tooltip("落地阴影贴图。留空则用纯黑半透明方片。")]
        public Material shadowMaterial;

        [Tooltip("搜索容器模型。留空用彩色方块。")]
        public GameObject containerPrefab;

        [Tooltip("天花板碎片模型。留空用小方块。")]
        public GameObject debrisPrefab;

        [Tooltip("地板砖模型。留空用扁平方块。\n注意：必须是 1×1 单位大小，建场时会按格子尺寸缩放。")]
        public GameObject floorTilePrefab;

        [Header("═══ 地板材质（按状态切换）═══")]
        public Material floorMatSolid;
        [Tooltip("开裂预警状态。留空则用代码染红。")]
        public Material floorMatCracking;

        public ItemDefinition RollItem(RoomTheme theme)
        {
            var pool = GetPool(theme);
            if (pool.Count == 0) return null;

            float total = 0f;
            for (int i = 0; i < pool.Count; i++)
                if (pool[i] != null) total += Mathf.Max(0.01f, pool[i].spawnWeight);

            if (total <= 0f) return pool[0];

            float roll = Random.Range(0f, total);
            float acc = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] == null) continue;
                acc += Mathf.Max(0.01f, pool[i].spawnWeight);
                if (roll <= acc) return pool[i];
            }
            return pool[pool.Count - 1];
        }
    }
}
