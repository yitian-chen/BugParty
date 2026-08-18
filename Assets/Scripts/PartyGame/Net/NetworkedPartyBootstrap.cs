using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace PartyGame.Net
{
    /// <summary>
    /// Owns the persistent NetworkManager for the whole session.
    /// Placed in LobbyScene; DontDestroyOnLoad keeps it alive across the load into GameScene.
    ///
    /// On Start:
    ///   - If LanBootstrapData says Host/Client, apply Address+Port and start that role.
    ///   - Otherwise stay idle (menu will drive it later).
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class NetworkedPartyBootstrap : MonoBehaviour
    {
        [Tooltip("If no LAN menu handed us a mode, start a Host anyway so the lobby scene works standalone (Editor Play from lobby).")]
        [SerializeField] private bool autoHostIfUnset = true;

        private static bool bootstrapped;

        private void Awake()
        {
            // NOTE: Do NOT DontDestroyOnLoad the NetworkManager GameObject here. Moving it out of
            // LanLobbyScene BEFORE StartHost/StartClient runs breaks NGO's in-scene NetworkObject
            // matching for LanLobbyManager (which sits on this same GO). Symptom: client connects
            // successfully but its local LanLobbyManager.Entries never syncs — lobby UI shows
            // "真人 0". DDoL is deferred to LanLobbyManager.OnNetworkSpawn, which runs after both
            // host and client have completed in-scene NO matching on their side.
        }

        private void Start()
        {
            if (bootstrapped) return; // Only bootstrap once per session — if lobby is re-entered we skip.
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                Debug.LogError("[NetworkedPartyBootstrap] NetworkManager.Singleton is null.");
                return;
            }
            if (nm.IsHost || nm.IsClient || nm.IsServer)
            {
                bootstrapped = true;
                return;
            }

            var utp = nm.GetComponent<UnityTransport>();
            LanBootstrapData.StartMode mode = LanBootstrapData.Mode;

            if (utp != null && (mode == LanBootstrapData.StartMode.Host || mode == LanBootstrapData.StartMode.Client))
            {
                utp.SetConnectionData(LanBootstrapData.Address, LanBootstrapData.Port);
            }

            switch (mode)
            {
                case LanBootstrapData.StartMode.Host:
                    Debug.Log($"[NetworkedPartyBootstrap] StartHost on {LanBootstrapData.Address}:{LanBootstrapData.Port}");
                    nm.StartHost();
                    break;
                case LanBootstrapData.StartMode.Client:
                    Debug.Log($"[NetworkedPartyBootstrap] StartClient -> {LanBootstrapData.Address}:{LanBootstrapData.Port}");
                    nm.StartClient();
                    break;
                default:
                    if (autoHostIfUnset)
                    {
                        Debug.Log("[NetworkedPartyBootstrap] No LAN mode set — auto StartHost for solo Editor test.");
                        nm.StartHost();
                    }
                    break;
            }

            LanBootstrapData.Consume();
            bootstrapped = true;
        }
    }
}
