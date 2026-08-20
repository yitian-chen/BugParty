using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PartyGame.UI
{
    /// <summary>
    /// HUD panel in the bottom-left showing the currently-equipped weapon's ammo/reserves.
    /// Format is always "{current} / {reserve}":
    /// - WaterGun:  current = clip ammo, reserve = ∞ (infinite spare)
    /// - Hook:      current = remaining shots (durability), reserve = 0 (no reload)
    /// - Other:     current = durability, reserve = 0 (single-use consumables)
    /// - Nothing:   "未装备"
    ///
    /// The fill Image mirrors the current/max-current ratio.
    /// </summary>
    public class PartyHudRaftFish : MonoBehaviour
    {
        private const string InfinitySymbol = "∞";

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
                localPlayer.OnCarriedFishChanged -= Refresh;
                localPlayer.OnWaterGunChanged -= Refresh;
                localPlayer.OnItemsChanged -= Refresh;
                localPlayer.OnEquippedWeaponChanged -= Refresh;
            }
        }

        public void SetLocalPlayer(PartyPlayer player)
        {
            if (localPlayer != null)
            {
                localPlayer.OnCarriedFishChanged -= Refresh;
                localPlayer.OnWaterGunChanged -= Refresh;
                localPlayer.OnItemsChanged -= Refresh;
                localPlayer.OnEquippedWeaponChanged -= Refresh;
            }
            localPlayer = player;
            Subscribe();
            Refresh(null, null);
        }

        private void Subscribe()
        {
            if (localPlayer != null)
            {
                localPlayer.OnWaterGunChanged += Refresh;
                localPlayer.OnItemsChanged += Refresh;
                localPlayer.OnEquippedWeaponChanged += Refresh;
            }
            Refresh(null, null);
        }

        private void Update()
        {
            // Reload progress must animate every frame; NetworkVariable OnValueChanged only fires on
            // discrete state transitions (start/end), not on the continuous countdown.
            if (localPlayer != null && localPlayer.WaterReloading && localPlayer.IsEquippedKind(ItemKind.WaterGun))
                Refresh(null, null);
        }

        private void Refresh(object sender, System.EventArgs e)
        {
            if (localPlayer == null || label == null) return;

            ItemInstance equipped = localPlayer.EquippedItem;
            if (equipped == null || equipped.data == null)
            {
                label.text = "未装备";
                if (fill != null) fill.fillAmount = 0f;
                return;
            }

            ItemDataSO data = equipped.data;
            if (data.kind == ItemKind.WaterGun)
            {
                int ammo = localPlayer.WaterAmmo;
                int cap = localPlayer.WaterClipSize;
                if (localPlayer.WaterReloading)
                {
                    label.text = $"装填中… {ammo} / {InfinitySymbol}";
                    if (fill != null) fill.fillAmount = localPlayer.WaterReloadNormalized;
                }
                else
                {
                    label.text = $"{ammo} / {InfinitySymbol}";
                    if (fill != null) fill.fillAmount = cap > 0 ? (float)ammo / cap : 0f;
                }
                return;
            }
            if (data.kind == ItemKind.Booster)
            {
                // Booster: durability = seconds of sprint remaining. Show as time, not shot count.
                int cur = equipped.durability;
                int max = data.startingDurability > 0 ? data.startingDurability : cur;
                label.text = $"{cur}s / {max}s";
                if (fill != null) fill.fillAmount = max > 0 ? (float)cur / max : 0f;
                return;
            }

            // Non-refilling items (hook, mines, nets…) show remaining uses over 0 reserve.
            int nCur = equipped.durability;
            int nMax = data.startingDurability > 0 ? data.startingDurability : nCur;
            label.text = $"{nCur} / 0";
            if (fill != null) fill.fillAmount = nMax > 0 ? (float)nCur / nMax : 0f;
        }
    }
}
