using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace PartyGame.Net
{
    /// <summary>
    /// Server-authoritative lobby state. Tracks connected clients, their chosen slot (island color 0..3),
    /// and the "match started" flag. Broadcasts changes through NetworkVariables/lists so the
    /// LanLobbyUI can render live.
    ///
    /// Sits on the NetworkManager GameObject alongside NetworkedPartyBootstrap. Survives scene reloads.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class LanLobbyManager : NetworkBehaviour
    {
        public static LanLobbyManager Instance { get; private set; }

        [System.Serializable]
        public struct LobbyEntry : INetworkSerializable, System.IEquatable<LobbyEntry>
        {
            public ulong clientId;
            public int slotIndex; // 0..3, -1 = unassigned
            public FixedString32Bytes displayName;

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref clientId);
                s.SerializeValue(ref slotIndex);
                s.SerializeValue(ref displayName);
            }
            public bool Equals(LobbyEntry o) => clientId == o.clientId && slotIndex == o.slotIndex && displayName.Equals(o.displayName);
        }

        public NetworkList<LobbyEntry> Entries;
        public NetworkVariable<bool> Started = new NetworkVariable<bool>(false);

        [SerializeField] private string gameSceneName = "GameScene_PartyFishing";
        [SerializeField] private int minPlayersToStart = 2;

        private NetworkManager nm;

        private void Awake()
        {
            Instance = this;
            Entries = new NetworkList<LobbyEntry>();
            nm = GetComponent<NetworkManager>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                nm.OnClientConnectedCallback += HandleClientConnected;
                nm.OnClientDisconnectCallback += HandleClientDisconnected;
                // Server includes itself as first entry (host is also a player).
                if (nm.IsHost) AddClientEntry(nm.LocalClientId);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (nm != null)
            {
                nm.OnClientConnectedCallback -= HandleClientConnected;
                nm.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (!IsServer) return;
            if (clientId == nm.LocalClientId) return; // host already added
            AddClientEntry(clientId);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (Entries[i].clientId == clientId) Entries.RemoveAt(i);
            }
        }

        private void AddClientEntry(ulong clientId)
        {
            int slot = PickFreeSlot();
            var entry = new LobbyEntry
            {
                clientId = clientId,
                slotIndex = slot,
                displayName = new FixedString32Bytes($"Player {clientId}"),
            };
            Entries.Add(entry);
        }

        private int PickFreeSlot()
        {
            var taken = new HashSet<int>();
            foreach (var e in Entries) if (e.slotIndex >= 0) taken.Add(e.slotIndex);
            for (int i = 0; i < 4; i++) if (!taken.Contains(i)) return i;
            return -1;
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestSlotServerRpc(int desiredSlot, ServerRpcParams p = default)
        {
            if (Started.Value) return;
            if (desiredSlot < 0 || desiredSlot > 3) return;
            ulong reqId = p.Receive.SenderClientId;
            // Reject if slot taken.
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].slotIndex == desiredSlot && Entries[i].clientId != reqId) return;

            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (e.clientId == reqId) { e.slotIndex = desiredSlot; Entries[i] = e; return; }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestStartMatchServerRpc()
        {
            if (!IsServer) return;
            if (Started.Value) return;
            if (Entries.Count < minPlayersToStart) return;
            // Ensure everyone has a valid unique slot.
            foreach (var e in Entries) if (e.slotIndex < 0) return;

            Started.Value = true;
            nm.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }

        /// <summary>Looks up a lobby entry by clientId. Returns default if unknown.</summary>
        public bool TryGetEntry(ulong clientId, out LobbyEntry entry)
        {
            foreach (var e in Entries)
            {
                if (e.clientId == clientId) { entry = e; return true; }
            }
            entry = default; return false;
        }
    }
}
