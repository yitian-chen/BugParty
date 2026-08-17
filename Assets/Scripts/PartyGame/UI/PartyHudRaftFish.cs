using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PartyGame.UI
{
    /// <summary>Simple normalized bar + label showing carried fish over capacity, e.g. "1 / 2".</summary>
    public class PartyHudRaftFish : MonoBehaviour
    {
        [SerializeField] private PartyPlayer localPlayer;
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private Image fill;

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            if (localPlayer != null) localPlayer.OnCarriedFishChanged -= Refresh;
        }

        public void SetLocalPlayer(PartyPlayer player)
        {
            if (localPlayer != null) localPlayer.OnCarriedFishChanged -= Refresh;
            localPlayer = player;
            Subscribe();
            Refresh(null, null);
        }

        private void Subscribe()
        {
            if (localPlayer != null) localPlayer.OnCarriedFishChanged += Refresh;
            Refresh(null, null);
        }

        private void Refresh(object sender, System.EventArgs e)
        {
            if (localPlayer == null || label == null) return;
            int total = localPlayer.CarriedFishTotal;
            int cap = localPlayer.RaftFishCapacity;
            label.text = $"{total} / {cap}";
            if (fill != null) fill.fillAmount = cap > 0 ? (float)total / cap : 0f;
        }
    }
}
