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

        // Sentinel clientId offset for AI bots: real Netcode clientIds are small (0..N),
        // 10000+ safely avoids collisions and stays inside ulong.
        public const ulong BotClientIdBase = 10000UL;

        [System.Serializable]
        public struct LobbyEntry : INetworkSerializable, System.IEquatable<LobbyEntry>
        {
            public ulong clientId;
            public int slotIndex; // 0..3, -1 = unassigned
            public FixedString32Bytes displayName;
            public bool isBot;

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref clientId);
                s.SerializeValue(ref slotIndex);
                s.SerializeValue(ref displayName);
                s.SerializeValue(ref isBot);
            }
            public bool Equals(LobbyEntry o) => clientId == o.clientId && slotIndex == o.slotIndex && displayName.Equals(o.displayName) && isBot == o.isBot;
        }

        public NetworkList<LobbyEntry> Entries;
        public NetworkVariable<bool> Started = new NetworkVariable<bool>(false);

        [SerializeField] private string gameSceneName = "GameScene_PartyFishing";
        [SerializeField] private int minPlayersToStart = 1;
        [SerializeField] private int targetPlayerCount = 4;
        [SerializeField] private bool fillWithBots = true;

        public int MinPlayersToStart => minPlayersToStart;
        public int TargetPlayerCount => targetPlayerCount;

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
                isBot = false,
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

            // Count real players (bots not yet added at this point).
            int realCount = 0;
            foreach (var e in Entries) if (!e.isBot) realCount++;
            if (realCount < minPlayersToStart) return;
            // Ensure every real player has picked a slot.
            foreach (var e in Entries) if (!e.isBot && e.slotIndex < 0) return;

            if (fillWithBots) FillBotsToTarget();

            // Post-fill sanity: everyone must have a valid unique slot.
            foreach (var e in Entries) if (e.slotIndex < 0) return;

            Started.Value = true;
            nm.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }

        /// <summary>
        /// Add AI bot entries so total player count reaches targetPlayerCount.
        /// Each bot gets a synthetic clientId (BotClientIdBase + i) and a free slot.
        /// </summary>
        private void FillBotsToTarget()
        {
            int botIndex = 0;
            while (Entries.Count < targetPlayerCount)
            {
                int slot = PickFreeSlot();
                if (slot < 0) break; // no more slots (shouldn't happen with 4-slot cap)
                ulong syntheticId = BotClientIdBase + (ulong)botIndex;
                // Guarantee uniqueness even if AddClientEntry ran multiple times.
                while (ContainsClientId(syntheticId)) syntheticId++;
                var entry = new LobbyEntry
                {
                    clientId = syntheticId,
                    slotIndex = slot,
                    displayName = new FixedString32Bytes($"Bot {botIndex + 1}"),
                    isBot = true,
                };
                Entries.Add(entry);
                botIndex++;
            }
        }

        private bool ContainsClientId(ulong id)
        {
            foreach (var e in Entries) if (e.clientId == id) return true;
            return false;
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
