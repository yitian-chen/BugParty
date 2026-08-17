using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace PartyGame.Net
{
    /// <summary>
    /// Sits in the game scene alongside NetworkManager. On Awake:
    ///   - If LanBootstrapData says Host/Client, apply Address+Port and start that role.
    ///   - Otherwise (Play-in-Editor with no menu), fall back to StartHost so solo testing
    ///     still works.
    /// Consumes the bootstrap data so a scene reload starts fresh.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class NetworkedPartyBootstrap : MonoBehaviour
    {
        [Tooltip("If no LAN menu handed us a mode, start a Host anyway so the game scene works when played directly.")]
        [SerializeField] private bool autoHostIfUnset = true;

        private void Start()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                Debug.LogError("[NetworkedPartyBootstrap] NetworkManager.Singleton is null.");
                return;
            }
            if (nm.IsHost || nm.IsClient || nm.IsServer)
            {
                Debug.Log("[NetworkedPartyBootstrap] Already networked; skipping bootstrap.");
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
        }
    }
}
