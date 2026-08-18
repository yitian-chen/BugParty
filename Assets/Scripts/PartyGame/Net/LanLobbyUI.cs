using Unity.Netcode;
using UnityEngine;

namespace PartyGame.Net
{
    /// <summary>
    /// IMGUI lobby UI — quick and dirty, but zero scene setup required. Reads LanLobbyManager
    /// state and lets each connected client pick a slot color; only the host sees the Start button.
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

        private void OnGUI()
        {
            var lobby = LanLobbyManager.Instance;
            var nm = NetworkManager.Singleton;
            if (nm == null) { GUI.Label(new Rect(20, 20, 500, 40), "NetworkManager 未启动"); return; }
            if (lobby == null || !nm.IsListening) { GUI.Label(new Rect(20, 20, 500, 40), "等待连接..."); return; }

            GUI.skin.label.fontSize = 22;
            GUI.skin.button.fontSize = 20;
            GUI.skin.box.fontSize = 20;

            int realCount = CountReal(lobby);
            int botsToFill = Mathf.Max(0, targetPlayerCount - realCount);
            GUI.Label(new Rect(40, 30, 900, 50), $"局域网房间  ({(nm.IsHost ? "主机" : "客户端")}  ID={nm.LocalClientId})  真人: {realCount} / {targetPlayerCount}   将补 {botsToFill} 个 AI");

            float y = 100;
            for (int i = 0; i < lobby.Entries.Count; i++)
            {
                var e = lobby.Entries[i];
                string me = e.clientId == nm.LocalClientId ? "  ← 你" : "";
                string slotLabel = e.slotIndex >= 0 && e.slotIndex < SlotNames.Length ? SlotNames[e.slotIndex] : "未选";
                GUI.color = e.slotIndex >= 0 && e.slotIndex < SlotColors.Length ? SlotColors[e.slotIndex] : Color.gray;
                GUI.Box(new Rect(40, y, 600, 44), $"[{slotLabel}]  {(e.isBot ? "AI 机器人" : $"玩家 {e.clientId}")}  {e.displayName}{me}");
                GUI.color = Color.white;
                y += 54;
            }

            // AI placeholder rows: show slots that WILL be filled by bots on Start.
            if (botsToFill > 0)
            {
                var taken = new System.Collections.Generic.HashSet<int>();
                foreach (var e in lobby.Entries) if (e.slotIndex >= 0) taken.Add(e.slotIndex);
                int filled = 0;
                for (int s = 0; s < 4 && filled < botsToFill; s++)
                {
                    if (taken.Contains(s)) continue;
                    GUI.color = new Color(SlotColors[s].r, SlotColors[s].g, SlotColors[s].b, 0.55f);
                    GUI.Box(new Rect(40, y, 600, 44), $"[{SlotNames[s]}]  (开始后自动填 AI)");
                    GUI.color = Color.white;
                    y += 54;
                    filled++;
                }
            }

            // Slot selection for me
            y += 20;
            GUI.color = Color.white;
            GUI.Label(new Rect(40, y, 600, 30), "选择队伍：");
            y += 40;
            for (int s = 0; s < 4; s++)
            {
                GUI.color = SlotColors[s];
                if (GUI.Button(new Rect(40 + s * 140, y, 130, 60), SlotNames[s]))
                {
                    lobby.RequestSlotServerRpc(s);
                }
            }
            GUI.color = Color.white;
            y += 90;

            if (nm.IsHost)
            {
                bool canStart = realCount >= minPlayersToStart && AllRealSlotsAssigned(lobby);
                GUI.enabled = canStart;
                string label = canStart
                    ? (botsToFill > 0 ? $"开始对局（补 {botsToFill} AI）" : "开始对局")
                    : $"需要 ≥{minPlayersToStart} 真人且都选队";
                if (GUI.Button(new Rect(40, y, 320, 70), label))
                {
                    lobby.RequestStartMatchServerRpc();
                }
                GUI.enabled = true;
            }
            else
            {
                GUI.Label(new Rect(40, y, 600, 40), "等待主机开始...");
            }

            if (GUI.Button(new Rect(Screen.width - 200, 30, 160, 50), "离开房间"))
            {
                nm.Shutdown();
                UnityEngine.SceneManagement.SceneManager.LoadScene("LanMenuScene");
            }
        }

        private static int CountReal(LanLobbyManager lobby)
        {
            int n = 0;
            foreach (var e in lobby.Entries) if (!e.isBot) n++;
            return n;
        }

        private static bool AllRealSlotsAssigned(LanLobbyManager lobby)
        {
            foreach (var e in lobby.Entries) if (!e.isBot && e.slotIndex < 0) return false;
            return true;
        }
    }
}
