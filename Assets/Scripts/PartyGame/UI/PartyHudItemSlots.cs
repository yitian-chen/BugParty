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
        }

        [SerializeField] private PartyPlayer localPlayer;
        [SerializeField] private SlotView[] slotViews;

        private void OnEnable() => Subscribe();
        private void OnDisable() { if (localPlayer != null) localPlayer.OnItemsChanged -= Refresh; }

        public void SetLocalPlayer(PartyPlayer player)
        {
            if (localPlayer != null) localPlayer.OnItemsChanged -= Refresh;
            localPlayer = player;
            Subscribe();
            Refresh(null, null);
        }

        private void Subscribe()
        {
            if (localPlayer != null) localPlayer.OnItemsChanged += Refresh;
            Refresh(null, null);
        }

        private void Refresh(object sender, System.EventArgs e)
        {
            if (localPlayer == null || slotViews == null) return;
            ItemInstance[] slots = localPlayer.ItemSlots;
            for (int i = 0; i < slotViews.Length; i++)
            {
                SlotView v = slotViews[i];
                if (v == null) continue;
                ItemInstance inst = (slots != null && i < slots.Length) ? slots[i] : null;
                bool hasItem = inst != null && !inst.IsEmpty;

                if (v.icon != null)
                {
                    v.icon.enabled = hasItem && inst.data != null && inst.data.icon != null;
                    if (v.icon.enabled) v.icon.sprite = inst.data.icon;
                }
                if (v.nameLabel != null)
                {
                    v.nameLabel.text = hasItem ? inst.data.displayName : "";
                }
                if (v.durabilityLabel != null)
                {
                    v.durabilityLabel.text = hasItem ? "x" + inst.durability : "";
                }
                if (v.hotkeyLabel != null)
                {
                    v.hotkeyLabel.text = (i + 1).ToString();
                }
                if (v.emptyIndicator != null) v.emptyIndicator.SetActive(!hasItem);
            }
        }
    }
}
