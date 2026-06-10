using TMPro;
using UnityEngine;
using UnityEngine.UI;
// using Unity.Services.Lobbies.Models;

public class LobbyListSingleUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI lobbyNameText;

    private KitchenGameLobby.LocalLobbyInfo lobby;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(() =>
        {
            KitchenGameLobby.Instance.JoinWithLobbyId(lobby.Id);
        });
    }

    public void SetLobby(KitchenGameLobby.LocalLobbyInfo lobby)
    {
        this.lobby = lobby;
        lobbyNameText.text = lobby.Name;
    }
}
