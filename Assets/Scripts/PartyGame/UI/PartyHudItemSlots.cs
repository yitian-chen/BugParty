using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PartyGame.UI
{
    /// <summary>Renders up to N item slots (icon + durability) for the local player.</summary>
    public class PartyHudItemSlots : MonoBehaviour
    {
        [System.Serializable]
        public class SlotView
        {
            public Image icon;
            public TextMeshProUGUI durabilityLabel;
            public TextMeshProUGUI nameLabel;
            public TextMeshProUGUI hotkeyLabel;
            public GameObject emptyIndicator;
            [Tooltip("Optional highlight image shown when this slot is the currently-equipped weapon.")]
            public GameObject equippedHighlight;
        }

        [SerializeField] private PartyPlayer localPlayer;
        [SerializeField] private SlotView[] slotViews;
        [SerializeField] private Color equippedNameColor = new Color(1f, 0.9f, 0.3f, 1f);
        [SerializeField] private Color idleNameColor = new Color(1f, 1f, 1f, 1f);

        private void OnEnable() => Subscribe();
        private void OnDisable()
        {
            if (localPlayer != null)
            {
                localPlayer.OnItemsChanged -= Refresh;
                localPlayer.OnEquippedWeaponChanged -= Refresh;
            }
        }

        public void SetLocalPlayer(PartyPlayer player)
        {
            if (localPlayer != null)
            {
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
                localPlayer.OnItemsChanged += Refresh;
                localPlayer.OnEquippedWeaponChanged += Refresh;
            }
            Refresh(null, null);
        }

        private void Update()
        {
            // Hook cooldown countdown ticks every frame; NetworkVariable OnValueChanged only fires
            // on discrete transitions, not on continuous decrements.
            if (localPlayer != null && localPlayer.HookOnCooldown) Refresh(null, null);
        }

        private void Refresh(object sender, System.EventArgs e)
        {
            if (localPlayer == null || slotViews == null) return;
            ItemInstance[] slots = localPlayer.ItemSlots;
            int equipped = localPlayer.EquippedSlot;
            for (int i = 0; i < slotViews.Length; i++)
            {
                SlotView v = slotViews[i];
                if (v == null) continue;
                ItemInstance inst = (slots != null && i < slots.Length) ? slots[i] : null;
                bool hasItem = inst != null && !inst.IsEmpty;
                bool isHook = hasItem && inst.data != null && inst.data.kind == ItemKind.Hook;
                bool isEquipped = hasItem && i == equipped;

                if (v.icon != null)
                {
                    v.icon.enabled = hasItem && inst.data != null && inst.data.icon != null;
                    if (v.icon.enabled) v.icon.sprite = inst.data.icon;
                }
                if (v.nameLabel != null)
                {
                    v.nameLabel.text = hasItem ? inst.data.displayName : "";
                    v.nameLabel.color = isEquipped ? equippedNameColor : idleNameColor;
                }
                if (v.durabilityLabel != null)
                {
                    // Special-case Hook: show remaining cooldown seconds instead of the "x{durability}" count
                    // whenever the hook is on cooldown. When ready, show the remaining shot count.
                    if (isHook && localPlayer.HookOnCooldown)
                    {
                        v.durabilityLabel.text = localPlayer.HookCooldownRemaining.ToString("0.0") + "s";
                    }
                    else
                    {
                        v.durabilityLabel.text = hasItem ? "x" + inst.durability : "";
                    }
                }
                if (v.hotkeyLabel != null)
                {
                    v.hotkeyLabel.text = (i + 1).ToString();
                }
                if (v.emptyIndicator != null) v.emptyIndicator.SetActive(!hasItem);
                if (v.equippedHighlight != null) v.equippedHighlight.SetActive(isEquipped);
            }
        }
    }
}
