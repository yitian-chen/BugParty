using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 2D 俯视 HUD。OnGUI 实现，零美术依赖。
    /// ★包含警报红边框、穿越提示轮播、塌陷警告，对应紧迫感的第三层。
    /// </summary>
    public class RoomHUD : MonoBehaviour
    {
        [Header("显示开关")]
        public bool showTimer = true;
        public bool showInventories = true;
        public bool showAlarmBanner = true;
        public bool showRedVignette = true;
        public bool showEventLog = true;
        public bool showHelp = true;

        [Range(3, 10)] public int logLines = 5;

        readonly List<string> _log = new List<string>();
        readonly Dictionary<Color, Texture2D> _texCache = new Dictionary<Color, Texture2D>();

        GUIStyle _big, _mid, _small, _tiny;
        Texture2D _panel, _panelDark, _slotEmpty;

        float _flashUntil;
        string _flashText = "";

        // ══════════════════════════════════════════════

        void OnEnable()
        {
            RoomEvents.OnItemCollected += OnCollected;
            RoomEvents.OnItemKnockedOut += OnKnocked;
            RoomEvents.OnElbowHit += OnElbow;
            RoomEvents.OnPhaseChanged += OnPhase;
            RoomEvents.OnTileCracking += OnCracking;
            RoomEvents.OnPlayerPitfall += OnPitfall;
            RoomEvents.OnPlayerRecovered += OnRecovered;
        }

        void OnDisable()
        {
            RoomEvents.OnItemCollected -= OnCollected;
            RoomEvents.OnItemKnockedOut -= OnKnocked;
            RoomEvents.OnElbowHit -= OnElbow;
            RoomEvents.OnPhaseChanged -= OnPhase;
            RoomEvents.OnTileCracking -= OnCracking;
            RoomEvents.OnPlayerPitfall -= OnPitfall;
            RoomEvents.OnPlayerRecovered -= OnRecovered;
        }

        void OnDestroy()
        {
            foreach (var kv in _texCache) if (kv.Value != null) Destroy(kv.Value);
            _texCache.Clear();
            if (_panel != null) Destroy(_panel);
            if (_panelDark != null) Destroy(_panelDark);
            if (_slotEmpty != null) Destroy(_slotEmpty);
        }

        // ── 事件 ───────────────────────────────────────

        void OnCollected(PlayerActor a, ItemDefinition i)
        {
            Push($"{a.playerColor.ToLabel()}方 获得 {i.displayName}（{i.lootValue}分）");
            if (IsLocal(a)) Flash($"+ {i.displayName}");
        }

        void OnKnocked(PlayerActor a, ItemDefinition i)
        {
            Push($"{a.playerColor.ToLabel()}方 掉落 {i.displayName}！");
            if (IsLocal(a)) Flash($"− {i.displayName}");
        }

        void OnElbow(PlayerActor atk, PlayerActor vic)
            => Push($"{atk.playerColor.ToLabel()}方 肘击 {vic.playerColor.ToLabel()}方");

        void OnCracking(FloorTile t) => Push("⚠ 地板开裂，即将塌陷！");

        void OnPitfall(PlayerActor a)
        {
            Push($"{a.playerColor.ToLabel()}方 掉进洞里了！");
            if (IsLocal(a)) Flash("掉下去了！");
        }

        void OnRecovered(PlayerActor a)
        {
            if (IsLocal(a)) Flash("爬回来了…");
        }

        void OnPhase(RoundPhase p)
        {
            switch (p)
            {
                case RoundPhase.Intro:      Push("── 进入 Bug 会议室 ──"); break;
                case RoundPhase.Searching:  Push("── 门已锁！开始搜索 ──"); break;
                case RoundPhase.Collapse:   Push("── 地板全面塌陷！ ──"); break;
                case RoundPhase.Transition: Push("── 正在穿越 ──"); break;
            }
        }

        bool IsLocal(PlayerActor a)
        {
            var mgr = RoomManager.Instance;
            return mgr != null && mgr.LocalPlayer == a;
        }

        void Push(string s)
        {
            _log.Add(s);
            while (_log.Count > logLines) _log.RemoveAt(0);
        }

        void Flash(string s)
        {
            _flashText = s;
            _flashUntil = Time.time + 1.5f;
        }

        // ── 样式 ───────────────────────────────────────

        void EnsureStyles()
        {
            if (_big != null) return;

            _big = new GUIStyle(GUI.skin.label)
            { fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _mid = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            _tiny = new GUIStyle(GUI.skin.label) { fontSize = 11 };

            _panel = Tex(new Color(0f, 0f, 0f, 0.55f));
            _panelDark = Tex(new Color(0f, 0f, 0f, 0.82f));
            _slotEmpty = Tex(new Color(1f, 1f, 1f, 0.11f));
        }

        static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        Texture2D C(Color c)
        {
            if (_texCache.TryGetValue(c, out var t) && t != null) return t;
            t = Tex(c);
            _texCache[c] = t;
            return t;
        }

        // ══════════════════════════════════════════════

        void OnGUI()
        {
            var mgr = RoomManager.Instance;
            if (mgr == null || mgr.config == null) return;

            EnsureStyles();

            // 先画红边框，作为最底层
            if (showRedVignette) DrawRedVignette(mgr);

            if (showTimer) DrawTimer(mgr);
            if (showAlarmBanner) DrawAlarmBanner(mgr);
            if (showInventories) DrawInventories(mgr);
            if (showEventLog) DrawLog();
            if (showHelp) DrawHelp(mgr);

            DrawFlash();

            if (mgr.Phase == RoundPhase.Collapse || mgr.Phase == RoundPhase.Transition)
                DrawCollapseOverlay(mgr);

            if (mgr.Phase == RoundPhase.Finished) DrawSettlement(mgr);
        }

        // ── ★红色警报边框（画面级紧迫感）─────────────

        void DrawRedVignette(RoomManager mgr)
        {
            if (mgr.Phase != RoundPhase.Searching && mgr.Phase != RoundPhase.Collapse) return;

            float urgency = 0f;
            if (mgr.Phase == RoundPhase.Collapse) urgency = 1f;
            else if (mgr.config.urgentThreshold > 0f)
                urgency = 1f - Mathf.Clamp01(mgr.TimeLeft / mgr.config.urgentThreshold);

            if (urgency <= 0.01f) return;

            // 脉冲频率随紧张度上升
            float freq = Mathf.Lerp(1.6f, 6f, urgency);
            float pulse = (Mathf.Sin(Time.time * freq * Mathf.PI) + 1f) * 0.5f;
            float alpha = Mathf.Lerp(0.05f, 0.30f, urgency) * (0.45f + pulse * 0.55f);

            var col = new Color(0.95f, 0.1f, 0.08f, alpha);
            var tex = C(col);

            // 四条边框，中间留空不遮挡视野
            float thick = Mathf.Lerp(20f, 62f, urgency);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, thick), tex);
            GUI.DrawTexture(new Rect(0f, Screen.height - thick, Screen.width, thick), tex);
            GUI.DrawTexture(new Rect(0f, 0f, thick, Screen.height), tex);
            GUI.DrawTexture(new Rect(Screen.width - thick, 0f, thick, Screen.height), tex);
        }

        // ── 倒计时 ─────────────────────────────────────

        void DrawTimer(RoomManager mgr)
        {
            float w = 300f, h = 70f;
            var r = new Rect((Screen.width - w) * 0.5f, 12f, w, h);
            GUI.DrawTexture(r, _panel);

            string label, time;
            Color col = Color.white;

            switch (mgr.Phase)
            {
                case RoundPhase.Intro:
                    label = "准备"; time = "";
                    col = new Color(0.7f, 0.85f, 1f); break;
                case RoundPhase.Searching:
                    label = "搜索阶段";
                    time = Mathf.CeilToInt(mgr.TimeLeft).ToString();
                    col = mgr.TimeLeft <= mgr.config.urgentThreshold
                        ? Color.Lerp(new Color(1f, 0.3f, 0.2f), Color.white,
                                     Mathf.PingPong(Time.time * 4.5f, 1f))
                        : Color.white;
                    break;
                case RoundPhase.Collapse:
                    label = "★ 全面塌陷 ★"; time = "";
                    col = new Color(1f, 0.25f, 0.15f); break;
                case RoundPhase.Transition:
                    label = "穿越中…"; time = "";
                    col = new Color(0.4f, 0.8f, 1f); break;
                default:
                    label = "结算"; time = "";
                    col = new Color(1f, 0.85f, 0.3f); break;
            }

            var ms = new GUIStyle(_mid) { alignment = TextAnchor.MiddleCenter };
            ms.normal.textColor = col;
            GUI.Label(new Rect(r.x, r.y + 5f, r.width, 22f), label, ms);

            if (!string.IsNullOrEmpty(time))
            {
                var bs = new GUIStyle(_big);
                bs.normal.textColor = col;
                GUI.Label(new Rect(r.x, r.y + 24f, r.width, 44f), time, bs);
            }
        }

        // ── ★警报文案轮播 ─────────────────────────────

        void DrawAlarmBanner(RoomManager mgr)
        {
            if (string.IsNullOrEmpty(mgr.AlarmMessage)) return;
            if (mgr.Phase == RoundPhase.Finished) return;

            float w = 340f, h = 32f;
            var r = new Rect((Screen.width - w) * 0.5f, 88f, w, h);

            bool urgent = mgr.Phase == RoundPhase.Collapse
                          || mgr.TimeLeft <= mgr.config.urgentThreshold;

            // 紧张时底色变红
            var bg = urgent
                ? C(new Color(0.55f, 0.05f, 0.04f, 0.72f))
                : _panel;
            GUI.DrawTexture(r, bg);

            var st = new GUIStyle(_mid) { alignment = TextAnchor.MiddleCenter };
            float pulse = (Mathf.Sin(Time.time * (urgent ? 8f : 3f)) + 1f) * 0.5f;
            st.normal.textColor = Color.Lerp(
                new Color(1f, 0.55f, 0.45f), Color.white, pulse);

            GUI.Label(r, mgr.AlarmMessage, st);
        }

        // ── 背包 ───────────────────────────────────────

        void DrawInventories(RoomManager mgr)
        {
            float pw = 200f, ph = 96f, pad = 10f;

            var slots = new Rect[4]
            {
                new Rect(pad, Screen.height - ph - pad - 62f, pw, ph),
                new Rect(Screen.width - pw - pad, Screen.height - ph - pad - 62f, pw, ph),
                new Rect(pad, 100f, pw, ph),
                new Rect(Screen.width - pw - pad, 100f, pw, ph),
            };

            int idx = 0;
            for (int i = 0; i < mgr.players.Count && idx < 4; i++)
            {
                var p = mgr.players[i];
                if (p == null) continue;
                DrawOneInventory(slots[idx], p, mgr);
                idx++;
            }
        }

        void DrawOneInventory(Rect r, PlayerActor p, RoomManager mgr)
        {
            GUI.DrawTexture(r, _panel);

            var col = p.playerColor.ToColor();
            var ts = new GUIStyle(_mid);
            ts.normal.textColor = col;

            string tag = p.GetComponent<AIBrain>() != null ? " (AI)" : "";
            GUI.Label(new Rect(r.x + 10f, r.y + 5f, r.width - 20f, 22f),
                      p.playerColor.ToLabel() + "方" + tag + $"　{p.Inventory.TotalValue}分", ts);

            int cap = mgr.config.inventoryCapacity;
            float cell = 30f, gap = 6f;
            float y = r.y + 30f;

            for (int s = 0; s < cap; s++)
            {
                var sr = new Rect(r.x + 10f + s * (cell + gap), y, cell, cell);
                if (s < p.Inventory.Count)
                {
                    var item = p.Inventory.Items[s];
                    var ic = item == null ? Color.white
                        : (item.isRare
                            ? Color.Lerp(item.placeholderColor, new Color(1f, 0.85f, 0.2f), 0.5f)
                            : item.placeholderColor);
                    GUI.DrawTexture(sr, C(ic));

                    if (item != null)
                        GUI.Label(new Rect(sr.x + cell + 5f, sr.y + 5f, 120f, 20f),
                                  item.displayName, _small);
                }
                else GUI.DrawTexture(sr, _slotEmpty);
            }

            // ★状态行：加入高度与坠落状态，这是 2D 俯视版特有的信息
            string state = "待机";
            if (p.IsInPitfall) state = "坠落中！";
            else if (p.IsStaggered) state = "硬直";
            else if (p.Search != null && p.Search.IsSearching)
                state = $"搜索 {Mathf.RoundToInt(p.Search.Progress01 * 100f)}%";
            else if (p.IsAirborne) state = "空中";
            else if (p.HeightAboveGround > 0.5f) state = "站在高处";
            else if (p.Elbow != null && !p.Elbow.IsReady) state = "肘击冷却";

            GUI.Label(new Rect(r.x + 10f, r.y + 66f, r.width - 20f, 20f), state, _small);
        }

        // ── ★塌陷 / 穿越覆盖层 ───────────────────────

        void DrawCollapseOverlay(RoomManager mgr)
        {
            // 逐渐加深的黑幕，为场景切换做过渡
            float alpha = mgr.Phase == RoundPhase.Transition ? 0.72f : 0.20f;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height),
                            C(new Color(0f, 0f, 0f, alpha)));

            string text = mgr.Phase == RoundPhase.Transition
                ? "正在穿越到下一个 BUG…"
                : "地板塌陷！";

            var st = new GUIStyle(_big) { alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = mgr.Phase == RoundPhase.Transition
                ? new Color(0.45f, 0.82f, 1f)
                : new Color(1f, 0.3f, 0.2f);

            GUI.Label(new Rect(0f, Screen.height * 0.42f, Screen.width, 60f), text, st);
        }

        void DrawFlash()
        {
            if (Time.time > _flashUntil) return;

            float a = Mathf.Clamp01((_flashUntil - Time.time) / 1.5f);
            var st = new GUIStyle(_mid) { alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = new Color(1f, 1f, 1f, a);
            GUI.Label(new Rect(0f, Screen.height * 0.5f - 60f, Screen.width, 26f), _flashText, st);
        }

        // ── 日志与帮助 ─────────────────────────────────

        void DrawLog()
        {
            if (_log.Count == 0) return;

            float w = 320f;
            float h = _log.Count * 19f + 12f;
            var r = new Rect((Screen.width - w) * 0.5f, 128f, w, h);
            GUI.DrawTexture(r, _panel);

            var sb = new StringBuilder();
            for (int i = 0; i < _log.Count; i++) sb.AppendLine(_log[i]);
            GUI.Label(new Rect(r.x + 8f, r.y + 5f, r.width - 16f, r.height - 10f),
                      sb.ToString(), _small);
        }

        void DrawHelp(RoomManager mgr)
        {
            float w = 400f, h = 54f;
            var r = new Rect((Screen.width - w) * 0.5f, Screen.height - h - 8f, w, h);
            GUI.DrawTexture(r, _panel);

            var sb = new StringBuilder();
            sb.AppendLine("WASD 移动　Space 跳（可跳上桌子）　J 按住搜索　K 肘击");
            sb.Append($"R 重开　主题：{mgr.theme}　容量：{mgr.config.inventoryCapacity}");

            GUI.Label(new Rect(r.x + 10f, r.y + 5f, r.width - 20f, r.height - 10f),
                      sb.ToString(), new GUIStyle(_small) { alignment = TextAnchor.UpperCenter });
        }

        void DrawSettlement(RoomManager mgr)
        {
            float w = 400f, h = 56f + mgr.players.Count * 26f;
            var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.DrawTexture(r, _panelDark);

            GUI.Label(new Rect(r.x, r.y + 8f, r.width, 24f), "本环节结算",
                      new GUIStyle(_mid) { alignment = TextAnchor.MiddleCenter });

            var sorted = new List<PlayerActor>(mgr.players);
            sorted.RemoveAll(p => p == null);
            sorted.Sort((a, b) => b.Inventory.TotalValue.CompareTo(a.Inventory.TotalValue));

            float y = r.y + 36f;
            for (int i = 0; i < sorted.Count; i++)
            {
                var p = sorted[i];
                var st = new GUIStyle(_small);
                st.normal.textColor = p.playerColor.ToColor();
                GUI.Label(new Rect(r.x + 16f, y, r.width - 32f, 22f),
                          $"第{i + 1}名　{p.playerColor.ToLabel()}方　{p.Inventory.TotalValue} 分　" +
                          p.Inventory.Describe(), st);
                y += 26f;
            }
        }
    }
}
