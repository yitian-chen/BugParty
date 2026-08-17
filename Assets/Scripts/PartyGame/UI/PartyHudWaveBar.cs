using UnityEngine;
using UnityEngine.UI;

namespace PartyGame.UI
{
    /// <summary>Top-of-screen wave bar (30s countdown) and wave index label.</summary>
    public class PartyHudWaveBar : MonoBehaviour
    {
        [SerializeField] private Slider bar;
        [SerializeField] private TMPro.TextMeshProUGUI waveLabel;

        private void Update()
        {
            if (PartyGameManager.Instance == null) return;
            PartyGameConfig cfg = PartyGameManager.Instance.Config;
            if (cfg == null) return;

            float elapsed = PartyGameManager.Instance.MatchTimeElapsed;
            if (elapsed < 0f) elapsed = 0f;
            int currentWave = Mathf.Clamp(Mathf.FloorToInt(elapsed / cfg.waveInterval), 0, cfg.totalWaves - 1);
            float inWaveElapsed = elapsed - currentWave * cfg.waveInterval;
            float waveRemaining = Mathf.Clamp(cfg.waveInterval - inWaveElapsed, 0f, cfg.waveInterval);

            if (bar != null) bar.value = waveRemaining / cfg.waveInterval;
            if (waveLabel != null)
            {
                bool isFinal = currentWave == cfg.totalWaves - 1;
                waveLabel.text = isFinal ? $"Final Wave  {Mathf.CeilToInt(waveRemaining)}s" : $"Wave {currentWave + 1}  {Mathf.CeilToInt(waveRemaining)}s";
            }
        }
    }
}
