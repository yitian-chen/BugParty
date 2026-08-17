using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PartyGame.Net
{
    /// <summary>
    /// Minimal LAN menu:
    ///   - Host button  -> loads game scene as host
    ///   - Join button  -> reads IP + port fields, loads game scene as client
    /// Assign the two buttons and both InputFields (or TMP versions) in the Inspector.
    /// The scene assigned to <see cref="gameSceneName"/> must contain NetworkManager +
    /// NetworkedPartyBootstrap for the connection to actually start.
    /// </summary>
    public class LanMenuUI : MonoBehaviour
    {
        [Header("Scene to load after Host/Join is pressed")]
        [SerializeField] private string lobbySceneName = "LanLobbyScene";

        [Header("Buttons")]
        [SerializeField] private Button hostButton;
        [SerializeField] private Button joinButton;

        [Header("Address inputs (either legacy InputField or TMP_InputField works)")]
        [SerializeField] private TMP_InputField addressInputTmp;
        [SerializeField] private InputField addressInputLegacy;
        [SerializeField] private TMP_InputField portInputTmp;
        [SerializeField] private InputField portInputLegacy;

        [Header("Optional status label for connection errors")]
        [SerializeField] private TMP_Text statusLabel;

        [Header("Defaults")]
        [SerializeField] private string defaultAddress = "127.0.0.1";
        [SerializeField] private ushort defaultPort = 7777;

        private void Awake()
        {
            if (hostButton != null) hostButton.onClick.AddListener(OnHostClicked);
            if (joinButton != null) joinButton.onClick.AddListener(OnJoinClicked);
            SetAddressField(defaultAddress);
            SetPortField(defaultPort);
        }

        private void OnHostClicked()
        {
            LanBootstrapData.Mode = LanBootstrapData.StartMode.Host;
            LanBootstrapData.Address = "0.0.0.0"; // bind all interfaces so LAN clients can reach us
            LanBootstrapData.Port = ReadPort();
            LoadGame();
        }

        private void OnJoinClicked()
        {
            LanBootstrapData.Mode = LanBootstrapData.StartMode.Client;
            LanBootstrapData.Address = ReadAddress();
            LanBootstrapData.Port = ReadPort();
            LoadGame();
        }

        private void LoadGame()
        {
            if (string.IsNullOrWhiteSpace(lobbySceneName))
            {
                Log("Lobby scene name not set.");
                return;
            }
            SceneManager.LoadScene(lobbySceneName);
        }

        private string ReadAddress()
        {
            if (addressInputTmp != null && !string.IsNullOrWhiteSpace(addressInputTmp.text)) return addressInputTmp.text.Trim();
            if (addressInputLegacy != null && !string.IsNullOrWhiteSpace(addressInputLegacy.text)) return addressInputLegacy.text.Trim();
            return defaultAddress;
        }

        private ushort ReadPort()
        {
            string raw = null;
            if (portInputTmp != null && !string.IsNullOrWhiteSpace(portInputTmp.text)) raw = portInputTmp.text;
            else if (portInputLegacy != null && !string.IsNullOrWhiteSpace(portInputLegacy.text)) raw = portInputLegacy.text;
            if (!string.IsNullOrEmpty(raw) && ushort.TryParse(raw, out ushort p)) return p;
            return defaultPort;
        }

        private void SetAddressField(string v)
        {
            if (addressInputTmp != null) addressInputTmp.text = v;
            if (addressInputLegacy != null) addressInputLegacy.text = v;
        }

        private void SetPortField(ushort v)
        {
            if (portInputTmp != null) portInputTmp.text = v.ToString();
            if (portInputLegacy != null) portInputLegacy.text = v.ToString();
        }

        private void Log(string msg)
        {
            Debug.Log("[LanMenuUI] " + msg);
            if (statusLabel != null) statusLabel.text = msg;
        }
    }
}
