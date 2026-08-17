using TMPro;
using UnityEngine;

namespace PartyGame.UI
{
    /// <summary>
    /// Displays the match countdown "M:SS" at the top-left.
    /// Also shows the "3/2/1/GO" pre-match countdown by piggybacking on the same label.
    /// </summary>
    public class PartyHudTimer : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;

        private void Update()
        {
            if (PartyGameManager.Instance == null || label == null) return;

            switch (PartyGameManager.Instance.CurrentState)
            {
                case PartyGameManager.State.WaitingToStart:
                    label.text = "";
                    break;
                case PartyGameManager.State.CountdownToStart:
                    float c = PartyGameManager.Instance.CountdownTimer;
                    label.text = c > 0f ? Mathf.CeilToInt(c).ToString() : "GO!";
                    break;
                case PartyGameManager.State.GamePlaying:
                    float t = PartyGameManager.Instance.MatchTimeRemaining;
                    int mm = (int)(t / 60f);
                    int ss = (int)(t % 60f);
                    label.text = $"{mm}:{ss:D2}";
                    break;
                case PartyGameManager.State.GameOver:
                    label.text = "0:00";
                    break;
            }
        }
    }
}
