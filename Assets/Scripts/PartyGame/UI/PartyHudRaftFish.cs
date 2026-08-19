using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PartyGame.UI
{
    /// <summary>
    /// HUD panel in the bottom-left showing the currently-equipped item's remaining uses.
    /// - WaterGun: "水枪 {ammo}/{clip}" (or "装填中… {ammo}/{clip}" during a reload)
    /// - Hook:     "钩爪 {durability}/{maxDurability}"
    /// - Other:    "{displayName} {durability}/{startingDurability}"
    /// - Nothing:  "未装备"
    ///
    /// The fill Image mirrors the ratio.
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
                    label.text = $"装填中… {ammo} / {cap}";
                    if (fill != null) fill.fillAmount = localPlayer.WaterReloadNormalized;
                }
                else
                {
                    label.text = $"水枪 {ammo} / {cap}";
                    if (fill != null) fill.fillAmount = cap > 0 ? (float)ammo / cap : 0f;
                }
                return;
            }

            // Generic path — durability out of max.
            int cur = equipped.durability;
            int max = data.startingDurability > 0 ? data.startingDurability : cur;
            string name = string.IsNullOrEmpty(data.displayName) ? data.kind.ToString() : data.displayName;
            label.text = $"{name} {cur} / {max}";
            if (fill != null) fill.fillAmount = max > 0 ? (float)cur / max : 0f;
        }
    }
}
