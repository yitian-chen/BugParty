using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 2D 俯视密室搜刮总控。
    /// 流程：Intro → Searching（含随机塌陷）→ ★Collapse（全塌陷）→ ★Transition（穿越）→ Finished
    /// </summary>
    public class RoomManager : MonoBehaviour
    {
        public static RoomManager Instance { get; private set; }

        [Header("配置")]
        public RoomConfig config;
        public RoomTheme theme = RoomTheme.Fishing;

        [Header("场景引用（建场工具自动填）")]
        public List<PlayerActor> players = new List<PlayerActor>();
        public List<SearchContainer> containers = new List<SearchContainer>();

        [Tooltip("★地板网格，塌陷系统的核心")]
        public FloorGrid floorGrid;

        public Transform doorPivot;
        public CeilingDebrisSpawner debrisSpawner;

        [Header("★衔接下一关")]
        [Tooltip("穿越完成后要加载的场景名。留空则只打印日志，方便单独测试本环节")]
        public string nextSceneName = "";

        [Tooltip("勾选后穿越结束自动重开本环节，方便反复测试")]
        public bool loopForTesting = true;

        [Header("调试")]
        public bool allowRestartKey = true;
        public bool verboseLog = true;

        // ── 状态 ───────────────────────────────────────
        public RoundPhase Phase { get; private set; } = RoundPhase.Intro;
        public float TimeLeft { get; private set; }

        /// <summary>玩家能否自由行动。</summary>
        public bool CanAct => Phase == RoundPhase.Searching;

        /// <summary>本地真人玩家，HUD 用。</summary>
        public PlayerActor LocalPlayer { get; private set; }

        /// <summary>★当前的警报提示文案，HUD 轮播用。</summary>
        public string AlarmMessage { get; private set; } = "";

        Coroutine _flow;
        Quaternion _doorOpen;
        Quaternion _doorClosed;
        readonly List<Vector3> _avoidPoints = new List<Vector3>();

        // 警报文案轮播
        static readonly string[] AlarmTexts =
        {
            "⚠ 正在穿越中…",
            "⚠ 请尽快修复 BUG",
            "⚠ 地板结构不稳定",
            "⚠ 数据完整性下降",
        };
        int _alarmTextIndex;
        float _nextAlarmTextTime;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Room2D] 场景中存在多个 RoomManager，已销毁多余的。", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (config == null)
            {
                Debug.LogError("[Room2D] 未指定 RoomConfig。", this);
                enabled = false;
                return;
            }

            if (doorPivot != null)
            {
                _doorClosed = Quaternion.identity;
                _doorOpen = Quaternion.Euler(0f, 105f, 0f);
                doorPivot.localRotation = _doorOpen;
            }
        }

        void Start()
        {
            CollectRefs();
            StartRound();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                RoomEvents.ClearAll();
            }
        }

        void CollectRefs()
        {
            if (players == null) players = new List<PlayerActor>();
            players.RemoveAll(p => p == null);
            if (players.Count == 0) players.AddRange(FindObjectsOfType<PlayerActor>());

            if (containers == null) containers = new List<SearchContainer>();
            containers.RemoveAll(c => c == null);
            if (containers.Count == 0) containers.AddRange(FindObjectsOfType<SearchContainer>());

            if (floorGrid == null) floorGrid = FindObjectOfType<FloorGrid>();
            if (debrisSpawner == null) debrisSpawner = FindObjectOfType<CeilingDebrisSpawner>();

            // 找本地玩家：挂了 HumanBrain 的那个
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] == null) continue;
                if (players[i].GetComponent<HumanBrain>() != null) { LocalPlayer = players[i]; break; }
            }
            if (LocalPlayer == null && players.Count > 0) LocalPlayer = players[0];

            // 建立塌陷保护点：容器与出生点附近不塌，避免玩法被破坏
            _avoidPoints.Clear();
            for (int i = 0; i < containers.Count; i++)
                if (containers[i] != null) _avoidPoints.Add(containers[i].transform.position);
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null) _avoidPoints.Add(players[i].transform.position);

            if (verboseLog)
                Debug.Log($"[Room2D] 就绪：{players.Count} 玩家，{containers.Count} 容器，" +
                          $"{(floorGrid != null ? floorGrid.AllTiles.Count : 0)} 块地板，主题 {theme}");
        }

        public void StartRound()
        {
            if (_flow != null) StopCoroutine(_flow);
            _flow = StartCoroutine(Flow());
        }

        // ══════════════════════════════════════════════
        //  主流程
        // ══════════════════════════════════════════════

        IEnumerator Flow()
        {
            yield return null;   // 等一帧，让所有 Start 执行完

            // ═══ Intro ═══
            SetPhase(RoundPhase.Intro);
            ResetAll();

            float t = 0f;
            while (t < config.introDuration)
            {
                t += Time.deltaTime;
                if (doorPivot != null)
                {
                    float k = Mathf.InverseLerp(config.introDuration * 0.4f, config.introDuration, t);
                    doorPivot.localRotation = Quaternion.Slerp(
                        _doorOpen, _doorClosed, Mathf.SmoothStep(0f, 1f, k));
                }
                yield return null;
            }
            if (doorPivot != null) doorPivot.localRotation = _doorClosed;

            // ═══ Searching（含随机塌陷调度）═══
            SetPhase(RoundPhase.Searching);
            TimeLeft = config.searchDuration;

            var collapseSchedule = BuildCollapseSchedule();
            int nextCollapse = 0;
            float elapsed = 0f;

            while (TimeLeft > 0f)
            {
                float dt = Time.deltaTime;
                TimeLeft = Mathf.Max(0f, TimeLeft - dt);
                elapsed += dt;

                RoomEvents.RaiseTimerTick(TimeLeft);

                // 到点触发一块地板塌陷
                while (nextCollapse < collapseSchedule.Count
                       && elapsed >= collapseSchedule[nextCollapse].time)
                {
                    var tile = collapseSchedule[nextCollapse].tile;
                    if (tile != null) tile.BeginCollapseSequence();
                    nextCollapse++;
                }

                UpdateAlarmMessage();
                UpdateAmbientShake();
                yield return null;
            }

            // ═══ ★Collapse：全塌陷 ═══
            yield return StartCoroutine(FinalCollapseRoutine());

            // ═══ ★Transition：穿越 ═══
            SetPhase(RoundPhase.Transition);
            AlarmMessage = "穿越中…";
            yield return new WaitForSeconds(config.transitionDuration);

            // ═══ Finished ═══
            SetPhase(RoundPhase.Finished);
            LogSettlement();
            HandoffToNextLevel();

            if (loopForTesting)
            {
                yield return new WaitForSeconds(1.5f);
                StartRound();
            }
        }

        // ══════════════════════════════════════════════
        //  ★随机塌陷调度
        // ══════════════════════════════════════════════

        struct CollapseEvent
        {
            public float time;
            public FloorTile tile;
        }

        /// <summary>
        /// 预先排好搜索阶段的塌陷时间表。
        /// 数量少（默认 5 块）、位置避开容器与出生点、时间均匀分布。
        /// </summary>
        List<CollapseEvent> BuildCollapseSchedule()
        {
            var list = new List<CollapseEvent>();
            if (floorGrid == null || config.randomCollapseCount <= 0) return list;

            var tiles = floorGrid.PickRandomCollapseCandidates(
                config.randomCollapseCount, _avoidPoints, config.collapseSafeRadius);

            if (tiles.Count == 0) return list;

            float startT = config.searchDuration * config.firstCollapseAt;
            float endT = config.searchDuration * config.lastCollapseAt;
            // 预留预警时间，保证最后一块塌陷能在搜索结束前完成
            endT = Mathf.Min(endT, config.searchDuration - config.crackWarningTime - 0.5f);

            for (int i = 0; i < tiles.Count; i++)
            {
                float k = tiles.Count > 1 ? i / (float)(tiles.Count - 1) : 0f;
                float time = Mathf.Lerp(startT, endT, k);
                // 加少量随机，避免节奏机械
                time += Random.Range(-0.6f, 0.6f);
                time = Mathf.Clamp(time, 0.5f, config.searchDuration - 0.2f);

                list.Add(new CollapseEvent { time = time, tile = tiles[i] });
            }

            list.Sort((a, b) => a.time.CompareTo(b.time));

            if (verboseLog)
                Debug.Log($"[Room2D] 已排定 {list.Count} 处随机塌陷");

            return list;
        }

        // ══════════════════════════════════════════════
        //  ★终局全塌陷 + 掉落
        // ══════════════════════════════════════════════

        IEnumerator FinalCollapseRoutine()
        {
            SetPhase(RoundPhase.Collapse);
            AlarmMessage = "⚠ 地板全面塌陷！";
            RoomEvents.RaiseFinalCollapseStarted();

            // 中断所有玩家的搜索
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null && players[i].Search != null)
                    players[i].Search.Cancel(false);

            // 震中取房间中心，波浪从中间向四周扩散
            Vector3 epicenter = floorGrid != null
                ? floorGrid.GridToWorld(new Vector2Int(floorGrid.columns / 2, floorGrid.rows / 2))
                : Vector3.zero;

            if (floorGrid != null)
                floorGrid.TriggerFinalCollapse(epicenter, config.collapseDuration);

            // 门也打开，形成"出口出现"的暗示
            if (doorPivot != null) doorPivot.localRotation = _doorOpen;

            // 等一小段让地板先塌，玩家再掉下去，顺序才对
            yield return new WaitForSeconds(config.collapseDuration * 0.45f);

            for (int i = 0; i < players.Count; i++)
                if (players[i] != null) players[i].BeginFallToNextLevel();

            yield return new WaitForSeconds(config.collapseDuration * 0.55f);
        }

        // ══════════════════════════════════════════════
        //  警报文案与环境抖动
        // ══════════════════════════════════════════════

        void UpdateAlarmMessage()
        {
            if (Time.time < _nextAlarmTextTime) return;

            bool urgent = TimeLeft <= config.urgentThreshold;

            AlarmMessage = AlarmTexts[_alarmTextIndex % AlarmTexts.Length];
            _alarmTextIndex++;

            // 紧张时文案切换更快，制造焦躁感
            _nextAlarmTextTime = Time.time + (urgent ? 1.4f : 3.2f);
        }

        float _nextAmbientShake;

        void UpdateAmbientShake()
        {
            if (Time.time < _nextAmbientShake) return;

            bool urgent = TimeLeft <= config.urgentThreshold;
            float interval = urgent
                ? config.screenShakeIntervalUrgent
                : config.screenShakeInterval;

            // 紧张时抖得更狠
            float amount = config.screenShakeAmount * (urgent ? 1.5f : 1f);
            RoomEvents.RaiseScreenShake(amount, config.screenShakeDuration);

            _nextAmbientShake = Time.time + interval * Random.Range(0.75f, 1.25f);
        }

        // ══════════════════════════════════════════════

        void ResetAll()
        {
            AlarmMessage = "";
            _alarmTextIndex = 0;
            _nextAlarmTextTime = 0f;
            _nextAmbientShake = 0f;

            for (int i = 0; i < players.Count; i++)
                if (players[i] != null) players[i].ResetForNewRound();

            for (int i = 0; i < containers.Count; i++)
                if (containers[i] != null) containers[i].ResetForNewRound();

            if (floorGrid != null) floorGrid.ResetAll();
            if (debrisSpawner != null) debrisSpawner.ClearAll();

            var loose = FindObjectsOfType<WorldItem>();
            for (int i = 0; i < loose.Length; i++)
                if (loose[i] != null) Destroy(loose[i].gameObject);
        }

        void SetPhase(RoundPhase p)
        {
            Phase = p;
            RoomEvents.RaisePhaseChanged(p);
            if (verboseLog) Debug.Log($"[Room2D] 阶段 → {p}");
        }

        void LogSettlement()
        {
            if (!verboseLog) return;

            var sb = new System.Text.StringBuilder("[Room2D] ═══ 本环节结算 ═══\n");
            var sorted = new List<PlayerActor>(players);
            sorted.RemoveAll(p => p == null);
            sorted.Sort((a, b) => b.Inventory.TotalValue.CompareTo(a.Inventory.TotalValue));

            for (int i = 0; i < sorted.Count; i++)
            {
                var p = sorted[i];
                sb.Append($"  第{i + 1}名 {p.playerColor.ToLabel()}方 | " +
                          $"{p.Inventory.Count}/{config.inventoryCapacity} 件 | " +
                          $"{p.Inventory.TotalValue} 分 | {p.Inventory.Describe()}\n");
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// ★衔接下一关。把各玩家带走的道具导出，供下一个玩法场景读取。
        /// </summary>
        void HandoffToNextLevel()
        {
            // 导出携带数据。正式版应写入一个跨场景的静态类或存档
            var sb = new System.Text.StringBuilder("[Room2D] 携带进入下一关：\n");
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null) continue;

                var ids = p.Inventory.ExportIds();
                sb.Append($"  {p.playerColor.ToLabel()}方 → [{string.Join(", ", ids)}]\n");

                CarryOverData.Set(p.playerColor, ids);
            }
            if (verboseLog) Debug.Log(sb.ToString());

            if (!string.IsNullOrEmpty(nextSceneName))
            {
                if (verboseLog) Debug.Log($"[Room2D] 加载下一关场景：{nextSceneName}");
                UnityEngine.SceneManagement.SceneManager.LoadScene(nextSceneName);
            }
            else if (verboseLog)
            {
                Debug.Log("[Room2D] nextSceneName 为空，停留在本场景（单独测试模式）");
            }
        }

        void Update()
        {
            if (allowRestartKey && Input.GetKeyDown(KeyCode.R)) StartRound();
        }

        // ── 查询接口（AI 用）───────────────────────────

        public SearchContainer FindNearestAvailableContainer(Vector3 from, PlayerActor asker)
        {
            SearchContainer best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < containers.Count; i++)
            {
                var c = containers[i];
                if (c == null || !c.IsAvailableFor(asker)) continue;
                float d = (c.InteractPoint - from).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = c; }
            }
            return best;
        }

        public PlayerActor FindNearestOpponent(PlayerActor self, float maxRange)
        {
            PlayerActor best = null;
            float bestSqr = maxRange * maxRange;

            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null || p == self || !p.IsAlive || p.IsInPitfall) continue;
                float d = (p.transform.position - self.transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = p; }
            }
            return best;
        }
    }

    /// <summary>
    /// ★跨关卡携带数据。搜刮结束后把各人道具存这里，下一个玩法场景直接读。
    /// 静态类，跨场景不丢失。
    /// </summary>
    public static class CarryOverData
    {
        static readonly Dictionary<PlayerColor, List<string>> _carried
            = new Dictionary<PlayerColor, List<string>>();

        public static void Set(PlayerColor color, List<string> itemIds)
            => _carried[color] = new List<string>(itemIds);

        public static List<string> Get(PlayerColor color)
            => _carried.TryGetValue(color, out var v) ? v : new List<string>();

        public static void Clear() => _carried.Clear();

        public static bool HasData => _carried.Count > 0;
    }
}
