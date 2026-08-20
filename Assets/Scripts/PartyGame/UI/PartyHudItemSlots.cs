using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PartyGame.UI
{
    /// <summary>
    /// Renders up to N item slots (icon + durability) for the local player.
    ///
    /// When a slot is a Hook that's currently on cooldown, the icon is dimmed by a translucent
    /// dark overlay and a large centered "N.Ns" countdown replaces the durability label. The
    /// overlay + countdown children are lazily attached to each slot the first time they're
    /// needed, so scene setup doesn't require wiring them by hand.
    /// </summary>
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

            // Runtime-attached (see EnsureCooldownVisuals). Not serialized.
            [System.NonSerialized] public Image cooldownOverlay;
            [System.NonSerialized] public TextMeshProUGUI cooldownLabel;
        }

        [SerializeField] private PartyPlayer localPlayer;
        [SerializeField] private SlotView[] slotViews;
        [SerializeField] private Color equippedNameColor = new Color(1f, 0.9f, 0.3f, 1f);
        [SerializeField] private Color idleNameColor = new Color(1f, 1f, 1f, 1f);
        [SerializeField] private Color cooldownOverlayColor = new Color(0.1f, 0.1f, 0.12f, 0.72f);
        [SerializeField] private Color cooldownLabelColor = new Color(1f, 1f, 1f, 1f);
        [SerializeField] private float cooldownLabelFontSize = 42f;

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

        private bool wasHookCoolingLastFrame;

        private void Update()
        {
            if (localPlayer == null) return;
            // Hook cooldown countdown ticks every frame; NetworkVariable OnValueChanged only fires
            // on discrete transitions, not on continuous decrements. And when the cooldown reaches
            // 0 on the server, nothing on the client side flips — so we also detect the falling
            // edge locally and refresh one more time to clear the overlay.
            bool coolingNow = localPlayer.HookOnCooldown;
            if (coolingNow || wasHookCoolingLastFrame) Refresh(null, null);
            wasHookCoolingLastFrame = coolingNow;
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
                bool isBooster = hasItem && inst.data != null && inst.data.kind == ItemKind.Booster;
                bool isEquipped = hasItem && i == equipped;
                bool hookCooling = isHook && localPlayer.HookOnCooldown;

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
                    // Hide the durability corner text during hook cooldown — the big centered
                    // countdown replaces it. When ready, restore "x{durability}".
                    // Booster durability is measured in seconds → show with an "s" suffix so the
                    // player reads it as time, not shot count.
                    if (hookCooling)
                    {
                        v.durabilityLabel.text = "";
                    }
                    else if (isBooster)
                    {
                        v.durabilityLabel.text = inst.durability + "s";
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

                // Cooldown overlay + centered countdown (lazy-attached).
                if (hookCooling)
                {
                    EnsureCooldownVisuals(v);
                    if (v.cooldownOverlay != null) v.cooldownOverlay.gameObject.SetActive(true);
                    if (v.cooldownLabel != null)
                    {
                        v.cooldownLabel.gameObject.SetActive(true);
                        v.cooldownLabel.text = localPlayer.HookCooldownRemaining.ToString("0.0") + "s";
                    }
                }
                else
                {
                    if (v.cooldownOverlay != null) v.cooldownOverlay.gameObject.SetActive(false);
                    if (v.cooldownLabel != null) v.cooldownLabel.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Attaches (once) a translucent dark Image + a large centered TMP label to the slot
        /// root. The slot root is inferred as the icon's parent — that's how the other slot
        /// widgets (Durability/Hotkey/Name) are already arranged in the scene.
        /// </summary>
        private void EnsureCooldownVisuals(SlotView v)
        {
            if (v == null || v.icon == null) return;
            Transform slotTF = v.icon.transform.parent;
            if (slotTF == null) return;

            if (v.cooldownOverlay == null)
            {
                // Reuse if a previous run left them behind.
                var existingOverlay = slotTF.Find("CooldownOverlay");
                if (existingOverlay != null)
                {
                    v.cooldownOverlay = existingOverlay.GetComponent<Image>();
                }
                if (v.cooldownOverlay == null)
                {
                    var go = new GameObject("CooldownOverlay", typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(slotTF, false);
                    var rt = (RectTransform)go.transform;
                    // Cover the whole slot regardless of size.
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    var img = go.GetComponent<Image>();
                    img.color = cooldownOverlayColor;
                    img.raycastTarget = false;
                    v.cooldownOverlay = img;
                }
                else
                {
                    v.cooldownOverlay.color = cooldownOverlayColor;
                    v.cooldownOverlay.raycastTarget = false;
                }
                // Draw above the icon but below any name/hotkey text; sibling order matters.
                if (v.cooldownOverlay != null) v.cooldownOverlay.transform.SetAsLastSibling();
            }

            if (v.cooldownLabel == null)
            {
                var existingLabel = slotTF.Find("CooldownLabel");
                if (existingLabel != null)
                {
                    v.cooldownLabel = existingLabel.GetComponent<TextMeshProUGUI>();
                }
                if (v.cooldownLabel == null)
                {
                    var go = new GameObject("CooldownLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
                    go.transform.SetParent(slotTF, false);
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    var tmp = go.GetComponent<TextMeshProUGUI>();
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.fontSize = cooldownLabelFontSize;
                    tmp.enableAutoSizing = false;
                    tmp.color = cooldownLabelColor;
                    tmp.raycastTarget = false;
                    // Reuse the durability label's font asset if available so we stay on the
                    // ICE SDF the rest of the HUD uses.
                    if (v.durabilityLabel != null && v.durabilityLabel.font != null) tmp.font = v.durabilityLabel.font;
                    v.cooldownLabel = tmp;
                }
                else
                {
                    v.cooldownLabel.alignment = TextAlignmentOptions.Center;
                    v.cooldownLabel.fontSize = cooldownLabelFontSize;
                    v.cooldownLabel.color = cooldownLabelColor;
                    v.cooldownLabel.raycastTarget = false;
                }
                // The countdown text draws on top of the dim overlay.
                if (v.cooldownLabel != null) v.cooldownLabel.transform.SetAsLastSibling();
            }
        }
    }
}
