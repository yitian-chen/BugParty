using Unity.Netcode;
using UnityEngine;

namespace PartyGame.Net
{
    /// <summary>
    /// Server-authoritative player spawner for Party Fishing LAN.
    ///
    /// Flow:
    ///   OnServerStarted -> disable/destroy the four scene placeholder PartyPlayers
    ///                      (P1..P4) so we don't have duplicates.
    ///   OnClientConnected(clientId) -> instantiate PartyPlayer.prefab, assign a
    ///     PlayerIndex (0..3) matching a free slot, place at that island's spawn
    ///     point, and spawn as owned by that client.
    ///
    /// Attach to the NetworkManager GameObject alongside NetworkedPartyBootstrap.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class PartyPlayerSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject playerPrefab;
        [Tooltip("World spawn positions for slots 0..3 (P1..P4). If empty, positions of scene placeholders PartyPlayer_P1..P4 are used.")]
        [SerializeField] private Vector3[] spawnPositions;
        [Tooltip("Names of the placeholder scene GameObjects that should be removed when the server starts.")]
        [SerializeField] private string[] scenePlaceholderNames = new[] { "PartyPlayer_P1", "PartyPlayer_P2", "PartyPlayer_P3", "PartyPlayer_P4" };
        [SerializeField] private int maxPlayers = 4;

        private readonly bool[] slotTaken = new bool[4];

        private NetworkManager netMgr;

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
                netMgr.OnClientConnectedCallback += HandleClientConnected;
                netMgr.OnClientDisconnectCallback += HandleClientDisconnected;
            }
        }

        private void OnDisable()
        {
            if (netMgr != null)
            {
                netMgr.OnServerStarted -= HandleServerStarted;
                netMgr.OnClientConnectedCallback -= HandleClientConnected;
                netMgr.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        private void HandleServerStarted()
        {
            if (!netMgr.IsServer) return;

            // Capture placeholder positions as spawn points, then remove them.
            if (spawnPositions == null || spawnPositions.Length == 0)
            {
                var captured = new System.Collections.Generic.List<Vector3>(maxPlayers);
                foreach (var n in scenePlaceholderNames)
                {
                    var go = GameObject.Find(n);
                    if (go != null) captured.Add(go.transform.position);
                }
                spawnPositions = captured.ToArray();
            }
            foreach (var n in scenePlaceholderNames)
            {
                var go = GameObject.Find(n);
                if (go != null) Destroy(go);
            }
            Debug.Log($"[PartyPlayerSpawner] Server started; captured {spawnPositions?.Length ?? 0} spawn points, removed placeholders.");

            // Spawn one for the host itself.
            SpawnFor(netMgr.LocalClientId);
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (!netMgr.IsServer) return;
            // Host's own connect callback already handled by OnServerStarted.
            if (clientId == netMgr.LocalClientId) return;
            SpawnFor(clientId);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (!netMgr.IsServer) return;
            // Free the slot that this client held so a rejoin can pick it back up.
            foreach (var pair in netMgr.ConnectedClients)
            {
                if (pair.Key != clientId) continue;
                var po = pair.Value.PlayerObject;
                if (po == null) continue;
                var pp = po.GetComponent<PartyPlayer>();
                if (pp != null)
                {
                    int idx = pp.PlayerIndex;
                    if (idx >= 0 && idx < slotTaken.Length) slotTaken[idx] = false;
                }
            }
        }

        private void SpawnFor(ulong clientId)
        {
            if (playerPrefab == null) { Debug.LogError("[PartyPlayerSpawner] playerPrefab not set."); return; }
            int slot = ClaimSlot();
            if (slot < 0) { Debug.LogWarning("[PartyPlayerSpawner] No free slot for client " + clientId); return; }

            Vector3 pos = (spawnPositions != null && slot < spawnPositions.Length) ? spawnPositions[slot] : Vector3.zero;
            var go = Instantiate(playerPrefab, pos, Quaternion.identity);
            go.name = $"PartyPlayer_P{slot + 1}_Net_{clientId}";
            var pp = go.GetComponent<PartyPlayer>();
            if (pp != null)
            {
                // playerIndex is a private [SerializeField] on PartyPlayer — set via reflection.
                var field = typeof(PartyPlayer).GetField("playerIndex",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null) field.SetValue(pp, slot);
            }

            var netObj = go.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError("[PartyPlayerSpawner] PartyPlayer prefab missing NetworkObject.");
                Destroy(go);
                slotTaken[slot] = false;
                return;
            }
            netObj.SpawnAsPlayerObject(clientId, true);
            Debug.Log($"[PartyPlayerSpawner] Spawned slot={slot} for client={clientId} at {pos}");
        }

        private int ClaimSlot()
        {
            int max = Mathf.Min(maxPlayers, slotTaken.Length);
            for (int i = 0; i < max; i++)
            {
                if (!slotTaken[i]) { slotTaken[i] = true; return i; }
            }
            return -1;
        }
    }
}
