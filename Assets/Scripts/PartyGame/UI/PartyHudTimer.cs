using TMPro;
using UnityEngine;

namespace PartyGame.UI
{
    /// <summary>
    /// Two labels driven by PartyGameManager:
    ///   - top-left `label`: shows the match clock "M:SS" once GamePlaying begins
    ///   - center `bigCountdown`: shows 3/2/1/GO! during pre-match countdown only
    /// </summary>
    public class PartyHudTimer : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private TextMeshProUGUI bigCountdown;

        private void Update()
        {
            if (PartyGameManager.Instance == null) return;

            switch (PartyGameManager.Instance.CurrentState)
            {
                case PartyGameManager.State.WaitingToStart:
                    if (label != null) label.text = "";
                    if (bigCountdown != null) bigCountdown.text = "";
                    break;
                case PartyGameManager.State.CountdownToStart:
                    if (label != null) label.text = "";
                    if (bigCountdown != null)
                    {
                        float c = PartyGameManager.Instance.CountdownTimer;
                        bigCountdown.text = c > 0f ? Mathf.CeilToInt(c).ToString() : "GO!";
                    }
                    break;
                case PartyGameManager.State.GamePlaying:
                    if (bigCountdown != null) bigCountdown.text = "";
                    if (label != null)
                    {
                        float t = PartyGameManager.Instance.MatchTimeRemaining;
                        int mm = (int)(t / 60f);
                        int ss = (int)(t % 60f);
                        label.text = $"{mm}:{ss:D2}";
                    }
                    break;
                case PartyGameManager.State.GameOver:
                    if (label != null) label.text = "0:00";
                    if (bigCountdown != null) bigCountdown.text = "";
                    break;
            }
        }
    }
}

