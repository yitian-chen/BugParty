using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PartyGame.Net
{
    /// <summary>
    /// Sits alongside NetworkManager. When this client loses connection (host closed,
    /// server disconnected them, transport fault), loads back to the LAN menu scene.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class DisconnectReturnToMenu : MonoBehaviour
    {
        [SerializeField] private string menuSceneName = "LanMenuScene";

        private NetworkManager nm;

        private void Awake() => nm = GetComponent<NetworkManager>();

        private void OnEnable()
        {
            if (nm != null) nm.OnClientDisconnectCallback += HandleClientDisconnect;
        }

        private void OnDisable()
        {
            if (nm != null) nm.OnClientDisconnectCallback -= HandleClientDisconnect;
        }

        private void HandleClientDisconnect(ulong clientId)
        {
            if (nm == null) return;
            // Only react to *our own* disconnect on the client side; server keeps running.
            if (nm.IsServer) return;
            if (clientId != nm.LocalClientId) return;

            Debug.Log("[DisconnectReturnToMenu] local client disconnected — returning to LAN menu.");
            if (!string.IsNullOrEmpty(menuSceneName)) SceneManager.LoadScene(menuSceneName);
        }
    }
}
