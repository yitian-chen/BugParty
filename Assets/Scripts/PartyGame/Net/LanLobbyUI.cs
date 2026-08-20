using System.Collections.Generic;
using PartyGame;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace PartyGame.Net
{
    /// <summary>
    /// uGUI lobby UI — builds a Canvas at runtime (no scene setup).
    ///
    /// Uses Legacy uGUI Text with a dynamic OS font (Microsoft YaHei / SimHei) instead of TMP.
    /// Reason: TMP's default LiberationSans FontAsset has no CJK glyphs, so all Chinese labels
    /// rendered as tofu boxes. Rather than ship + wire an ICE-SDF-with-fallback FontAsset for
    /// UGUI at runtime, we take the same path StunLabel takes — Font.CreateDynamicFontFromOSFont
    /// rasterizes any glyph the OS has on demand. This keeps the lobby self-contained and
    /// dependency-free.
    ///
    /// Behaviour is unchanged from the IMGUI version: reads LanLobbyManager state, lets each
    /// client pick a slot, shows a Start button to the host once conditions are met.
    /// </summary>
    public class LanLobbyUI : MonoBehaviour
    {
        [SerializeField] private int minPlayersToStart = 1;
        [SerializeField] private int targetPlayerCount = 4;

        private static readonly Color[] SlotColors =
        {
            new Color(0.85f, 0.20f, 0.20f),
            new Color(0.20f, 0.40f, 0.85f),
            new Color(0.25f, 0.75f, 0.30f),
            new Color(0.95f, 0.85f, 0.20f),
        };
        private static readonly string[] SlotNames = { "红队 P1", "蓝队 P2", "绿队 P3", "黄队 P4" };

        // Built-once UI references.
        private Canvas rootCanvas;
        private Text headerLabel;
        private RectTransform entriesParent;
        private Text statusLabel;
        private Button startButton;
        private Text startButtonLabel;
        private Button leaveButton;
        private readonly Button[] slotButtons = new Button[4];

        // Row pooling: reuse Image+Label rows across rebuilds instead of destroying/creating each frame.
        private readonly List<RectTransform> rowPool = new List<RectTransform>();

        private static Font cachedCjkFont;

        private void Awake()
        {
            EnsureEventSystem();
            BuildCanvas();
        }

        /// <summary>
        /// Buttons on this Canvas need an EventSystem to receive clicks. The IMGUI predecessor
        /// didn't need one, so LanLobbyScene ships without it — we create one on the fly if the
        /// active scene doesn't already have it. Uses the Input System module because the rest of
        /// the project has switched to it.
        /// </summary>
        private static void EnsureEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null) return;
            var existing = FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
            if (existing != null) return;
            var go = new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            DontDestroyOnLoad(go);
        }

        private void Update()
        {
            RefreshUI();
        }

        // ---------- Font loading ----------

        private static Font CjkFont()
        {
            if (cachedCjkFont != null) return cachedCjkFont;
            // Windows CJK families first; TryDynamicFont rasterizes any glyph present in the OS
            // font. Fall back to Unity's built-in Arial (which on Windows also uses OS CJK fonts).
            cachedCjkFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun", "Arial Unicode MS", "Arial" },
                24);
            if (cachedCjkFont == null) cachedCjkFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return cachedCjkFont;
        }

        // ---------- Canvas construction ----------

        private void BuildCanvas()
        {
            var canvasGO = new GameObject("LanLobbyUICanvas", typeof(RectTransform));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            canvasGO.AddComponent<GraphicRaycaster>();
            rootCanvas = canvas;

            // Full-screen dark backdrop.
            var bg = CreateRow(canvasGO.transform, new Vector2(0, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            bg.name = "Backdrop";
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.05f, 0.06f, 0.08f, 0.8f);
            bgImg.raycastTarget = false;

            headerLabel = CreateLabel(canvasGO.transform, "Header",
                new Vector2(0.05f, 0.85f), new Vector2(0.95f, 0.98f),
                34, TextAnchor.MiddleCenter);

            // Container that holds one row per lobby entry.
            entriesParent = CreateRow(canvasGO.transform,
                new Vector2(0.08f, 0.34f), new Vector2(0.92f, 0.82f),
                Vector2.zero, Vector2.zero);
            entriesParent.name = "Entries";
            var vlg = entriesParent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(0, 0, 0, 0);

            var chooseLabel = CreateLabel(canvasGO.transform, "ChooseTeamLabel",
                new Vector2(0.05f, 0.27f), new Vector2(0.5f, 0.32f), 22, TextAnchor.MiddleLeft);
            chooseLabel.text = "选择队伍：";

            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                float x0 = 0.05f + slot * 0.14f;
                float x1 = x0 + 0.13f;
                var btn = CreateButton(canvasGO.transform,
                    $"SlotBtn{slot}",
                    new Vector2(x0, 0.20f), new Vector2(x1, 0.26f),
                    SlotNames[slot], SlotColors[slot], 20);
                btn.onClick.AddListener(() =>
                {
                    PlayUiClick();
                    var lobby = LanLobbyManager.Instance;
                    if (lobby != null) lobby.RequestSlotServerRpc(slot);
                });
                slotButtons[slot] = btn;
            }

            startButton = CreateButton(canvasGO.transform,
                "StartBtn",
                new Vector2(0.05f, 0.09f), new Vector2(0.30f, 0.17f),
                "开始对局", new Color(0.15f, 0.55f, 0.20f), 24);
            startButtonLabel = startButton.GetComponentInChildren<Text>();
            startButton.onClick.AddListener(() =>
            {
                PlayUiClick();
                var lobby = LanLobbyManager.Instance;
                if (lobby != null) lobby.RequestStartMatchServerRpc();
            });

            statusLabel = CreateLabel(canvasGO.transform, "Status",
                new Vector2(0.32f, 0.09f), new Vector2(0.9f, 0.17f), 20, TextAnchor.MiddleLeft);

            leaveButton = CreateButton(canvasGO.transform,
                "LeaveBtn",
                new Vector2(0.83f, 0.90f), new Vector2(0.97f, 0.97f),
                "离开房间", new Color(0.35f, 0.35f, 0.4f), 18);
            leaveButton.onClick.AddListener(() =>
            {
                PlayUiClick();
                var nm = NetworkManager.Singleton;
                if (nm != null) nm.Shutdown();
                UnityEngine.SceneManagement.SceneManager.LoadScene("LanMenuScene");
            });
        }

        private static void PlayUiClick()
        {
            var sm = SoundManager.Instance;
            if (sm != null && sm.Library != null) sm.PlaySfx(sm.Library.sfxUiClick);
        }

        // ---------- Small factory helpers ----------

        private static RectTransform CreateRow(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offMin, Vector2 offMax)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
            return rt;
        }

        private static Text CreateLabel(Transform parent, string name, Vector2 aMin, Vector2 aMax, int fontSize, TextAnchor align)
        {
            var rt = CreateRow(parent, aMin, aMax, Vector2.zero, Vector2.zero);
            rt.gameObject.name = name;
            var t = rt.gameObject.AddComponent<Text>();
            t.font = CjkFont();
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            return t;
        }

        private static Button CreateButton(Transform parent, string name, Vector2 aMin, Vector2 aMax, string label, Color color, int fontSize)
        {
            var rt = CreateRow(parent, aMin, aMax, Vector2.zero, Vector2.zero);
            rt.gameObject.name = name;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var t = CreateLabel(rt, "Label", Vector2.zero, Vector2.one, fontSize, TextAnchor.MiddleCenter);
            t.text = label;
            t.color = Color.white;
            return btn;
        }

        // ---------- Per-frame state sync ----------

        private void RefreshUI()
        {
            var lobby = LanLobbyManager.Instance;
            var nm = NetworkManager.Singleton;

            if (nm == null)
            {
                headerLabel.text = "NetworkManager 未启动";
                HideEntriesAndSlots();
                startButton.gameObject.SetActive(false);
                return;
            }
            if (lobby == null || !nm.IsListening)
            {
                headerLabel.text = "等待连接...";
                HideEntriesAndSlots();
                startButton.gameObject.SetActive(false);
                return;
            }

            int realCount = 0;
            foreach (var e in lobby.Entries) if (!e.isBot) realCount++;
            int botsToFill = Mathf.Max(0, targetPlayerCount - realCount);

            headerLabel.text = $"局域网房间  ({(nm.IsHost ? "主机" : "客户端")}  ID={nm.LocalClientId})   真人: {realCount} / {targetPlayerCount}   将补 {botsToFill} 个 AI";

            int rowIdx = 0;
            for (int i = 0; i < lobby.Entries.Count; i++)
            {
                var e = lobby.Entries[i];
                string me = e.clientId == nm.LocalClientId ? "  ← 你" : "";
                string slotLabel = e.slotIndex >= 0 && e.slotIndex < SlotNames.Length ? SlotNames[e.slotIndex] : "未选";
                var color = e.slotIndex >= 0 && e.slotIndex < SlotColors.Length ? SlotColors[e.slotIndex] : Color.gray;
                string text = $"[{slotLabel}]  {(e.isBot ? "AI 机器人" : $"玩家 {e.clientId}")}  {e.displayName}{me}";
                SetEntryRow(rowIdx++, text, color, 1f);
            }
            if (botsToFill > 0)
            {
                var taken = new HashSet<int>();
                foreach (var e in lobby.Entries) if (e.slotIndex >= 0) taken.Add(e.slotIndex);
                int filled = 0;
                for (int s = 0; s < 4 && filled < botsToFill; s++)
                {
                    if (taken.Contains(s)) continue;
                    SetEntryRow(rowIdx++, $"[{SlotNames[s]}]  (开始后自动填 AI)", SlotColors[s], 0.55f);
                    filled++;
                }
            }
            for (int i = rowIdx; i < rowPool.Count; i++) rowPool[i].gameObject.SetActive(false);

            bool canStart = realCount >= minPlayersToStart && AllRealSlotsAssigned(lobby);
            if (nm.IsHost)
            {
                startButton.gameObject.SetActive(true);
                startButton.interactable = canStart;
                startButtonLabel.text = canStart
                    ? (botsToFill > 0 ? $"开始对局（补 {botsToFill} AI）" : "开始对局")
                    : $"需要 ≥{minPlayersToStart} 真人且都选队";
                statusLabel.text = "";
            }
            else
            {
                startButton.gameObject.SetActive(false);
                statusLabel.text = "等待主机开始...";
            }
        }

        private void HideEntriesAndSlots()
        {
            foreach (var r in rowPool) if (r != null) r.gameObject.SetActive(false);
        }

        private void SetEntryRow(int i, string text, Color color, float alpha)
        {
            RectTransform row;
            if (i < rowPool.Count)
            {
                row = rowPool[i];
                row.gameObject.SetActive(true);
            }
            else
            {
                row = new GameObject($"Entry{i}", typeof(RectTransform)).GetComponent<RectTransform>();
                row.SetParent(entriesParent, false);
                var img = row.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                var le = row.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 44f;

                var labelRT = CreateRow(row, new Vector2(0, 0), new Vector2(1, 1), new Vector2(12, 2), new Vector2(-12, -2));
                labelRT.gameObject.name = "Label";
                var t = labelRT.gameObject.AddComponent<Text>();
                t.font = CjkFont();
                t.fontSize = 22;
                t.alignment = TextAnchor.MiddleLeft;
                t.color = Color.white;
                t.raycastTarget = false;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.supportRichText = false;
                rowPool.Add(row);
            }
            var bgImg = row.GetComponent<Image>();
            if (bgImg != null) bgImg.color = new Color(color.r, color.g, color.b, alpha);
            var labelText = row.GetComponentInChildren<Text>();
            if (labelText != null) { labelText.text = text; labelText.color = Color.white; }
        }

        private static bool AllRealSlotsAssigned(LanLobbyManager lobby)
        {
            foreach (var e in lobby.Entries) if (!e.isBot && e.slotIndex < 0) return false;
            return true;
        }
    }
}
