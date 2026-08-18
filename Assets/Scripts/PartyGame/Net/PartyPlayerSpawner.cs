using Unity.Netcode;
using UnityEngine;

namespace PartyGame.Net
{
    /// <summary>
    /// Server-authoritative player spawner. Waits until NetworkSceneManager finishes loading
    /// the game scene; then reads LanLobbyManager entries and spawns one PartyPlayer per
    /// connected client at its chosen slot's spawn point.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class PartyPlayerSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject playerPrefab;
        [Tooltip("If empty, positions of scene placeholders PartyPlayer_P1..P4 are used at spawn time.")]
        [SerializeField] private Vector3[] spawnPositions;
        [SerializeField] private string[] scenePlaceholderNames = new[] { "PartyPlayer_P1", "PartyPlayer_P2", "PartyPlayer_P3", "PartyPlayer_P4" };
        [SerializeField] private string gameSceneName = "GameScene_PartyFishing";

        private NetworkManager netMgr;
        private bool spawnedForMatch;

        private void Awake()
        {
            netMgr = GetComponent<NetworkManager>();
        }

        private void OnEnable()
        {
            if (netMgr == null) netMgr = GetComponent<NetworkManager>();
            if (netMgr != null)
            {
                netMgr.OnServerStarted += HandleServerStarted;
                netMgr.OnClientDisconnectCallback += HandleClientDisconnected;
            }
        }

        private void OnDisable()
        {
            if (netMgr != null)
            {
                netMgr.OnServerStarted -= HandleServerStarted;
                netMgr.OnClientDisconnectCallback -= HandleClientDisconnected;
                if (netMgr.SceneManager != null)
                    netMgr.SceneManager.OnLoadEventCompleted -= HandleSceneLoaded;
            }
        }

        private void HandleServerStarted()
        {
            if (!netMgr.IsServer) return;
            // Subscribe to scene load completion so we spawn only after the game scene is live.
            if (netMgr.SceneManager != null)
            {
                netMgr.SceneManager.OnLoadEventCompleted += HandleSceneLoaded;
            }
        }

        private void HandleSceneLoaded(string sceneName, UnityEngine.SceneManagement.LoadSceneMode mode, System.Collections.Generic.List<ulong> clientsCompleted, System.Collections.Generic.List<ulong> clientsTimedOut)
        {
            if (spawnedForMatch) return;
            if (sceneName != gameSceneName) return;
            if (!netMgr.IsServer) return;

            // Capture spawn points from placeholder objects in the game scene, then remove placeholders.
            if (spawnPositions == null || spawnPositions.Length == 0)
            {
                var list = new System.Collections.Generic.List<Vector3>(4);
                foreach (var n in scenePlaceholderNames)
                {
                    var go = GameObject.Find(n);
                    if (go != null) list.Add(go.transform.position);
                }
                spawnPositions = list.ToArray();
            }
            foreach (var n in scenePlaceholderNames)
            {
                var go = GameObject.Find(n);
                if (go != null) Destroy(go);
            }

            // Spawn one PartyPlayer per lobby entry.
            var lobby = LanLobbyManager.Instance;
            if (lobby == null) { Debug.LogError("[PartyPlayerSpawner] No LanLobbyManager on server."); return; }

            foreach (var entry in lobby.Entries)
            {
                SpawnFor(entry.clientId, entry.slotIndex, entry.isBot);
            }
            spawnedForMatch = true;
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            // Nothing to do for now — LanLobbyManager cleans its list; if the game has started, the
            // PartyPlayer for the departed client stays as-is until match end (host keeps game going).
        }

        private void SpawnFor(ulong clientId, int slot, bool isBot)
        {
            if (playerPrefab == null) { Debug.LogError("[PartyPlayerSpawner] playerPrefab not set."); return; }
            if (slot < 0 || slot >= spawnPositions.Length) { Debug.LogWarning($"[PartyPlayerSpawner] slot {slot} out of range."); return; }

            Vector3 pos = spawnPositions[slot];
            var go = Instantiate(playerPrefab, pos, Quaternion.identity);
            go.name = isBot
                ? $"PartyPlayer_P{slot + 1}_Bot_{clientId}"
                : $"PartyPlayer_P{slot + 1}_Net_{clientId}";

            var pp = go.GetComponent<PartyGame.PartyPlayer>();
            if (pp != null)
            {
                var field = typeof(PartyGame.PartyPlayer).GetField("playerIndex",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null) field.SetValue(pp, slot);
                if (isBot) pp.SetIsBot(true);
            }

            var netObj = go.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError("[PartyPlayerSpawner] PartyPlayer prefab missing NetworkObject.");
                Destroy(go);
                return;
            }

            if (isBot)
            {
                // Server owns the bot object. Do NOT SpawnAsPlayerObject — Netcode only allows one
                // player object per real clientId, and synthetic bot ids aren't in NM.ConnectedClients.
                netObj.Spawn(true);
                // Attach the AI brain after spawn so it can safely touch NetworkVariables.
                var ai = go.GetComponent<PartyGame.PartyPlayerAI>();
                if (ai == null) ai = go.AddComponent<PartyGame.PartyPlayerAI>();
            }
            else
            {
                netObj.SpawnAsPlayerObject(clientId, true);
            }

            // Equip default loadout right after spawn so item slots are set before the match starts.
            // Doing it here (rather than in PartyGameManager's state transition) avoids racing
            // FindObjectsOfType against player spawn timing.
            var mgr = PartyGame.PartyGameManager.Instance;
            if (mgr != null && pp != null) mgr.EquipDefaultLoadoutFor(pp);

            Debug.Log($"[PartyPlayerSpawner] Spawned {(isBot ? "BOT" : "PLAYER")} slot={slot} for id={clientId} at {pos}");
        }
    }
}
