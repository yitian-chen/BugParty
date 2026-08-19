using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PartyGame.UI
{
    /// <summary>
    /// HUD panel showing the local player's water gun ammo (repurposed from the old raft-fish label
    /// slot in the bottom-left corner). Text: "X / N". Fill: X/N normalized.
    /// When the gun is reloading, the label switches to "装填中…" and the fill drives from the reload
    /// progress (empty → full).
    /// The world-space head bar (WaterReloadBar) covers the "everyone can see it" case; this only
    /// serves the local player and stays where the original raft-fish HUD used to live.
    /// </summary>
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
            if (localPlayer != null)
            {
                localPlayer.OnCarriedFishChanged -= Refresh; // legacy — kept for defensive unsubscribe
                localPlayer.OnWaterGunChanged -= Refresh;
            }
        }

        public void SetLocalPlayer(PartyPlayer player)
        {
            if (localPlayer != null)
            {
                localPlayer.OnCarriedFishChanged -= Refresh;
                localPlayer.OnWaterGunChanged -= Refresh;
            }
            localPlayer = player;
            Subscribe();
            Refresh(null, null);
        }

        private void Subscribe()
        {
            if (localPlayer != null) localPlayer.OnWaterGunChanged += Refresh;
            Refresh(null, null);
        }

        private void Update()
        {
            // Reload progress must animate every frame; NetworkVariable OnValueChanged only fires on
            // discrete state transitions (start/end), not on the continuous countdown.
            if (localPlayer != null && localPlayer.WaterReloading) Refresh(null, null);
        }

        private void Refresh(object sender, System.EventArgs e)
        {
            if (localPlayer == null || label == null) return;
            int ammo = localPlayer.WaterAmmo;
            int cap = localPlayer.WaterClipSize;
            if (localPlayer.WaterReloading)
            {
                label.text = $"装填中… {ammo} / {cap}";
                if (fill != null) fill.fillAmount = localPlayer.WaterReloadNormalized;
            }
            else
            {
                label.text = $"水枪 {ammo} / {cap}";
                if (fill != null) fill.fillAmount = cap > 0 ? (float)ammo / cap : 0f;
            }
        }
    }
}
