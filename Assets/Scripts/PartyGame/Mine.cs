using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// A placed mine. Waits armDelay seconds after spawn (so its owner can walk away),
    /// then stuns the first player who enters its trigger. The stunned player has the
    /// option to pick up this mine (see PartyPlayer.Stun for auto-pickup logic).
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class Mine : MonoBehaviour
    {
        [SerializeField] private float armDelay = 1.0f;
        [SerializeField] private float stunDurationOverride = -1f; // <0 uses config
        [SerializeField] private ItemDataSO mineItemData;
        [SerializeField] private Renderer visualRenderer;

        private PartyPlayer owner;
        private float aliveTime;
        private bool triggered;
        private SphereCollider trigger;

        public ItemDataSO ItemData => mineItemData;

        private void Awake()
        {
            trigger = GetComponent<SphereCollider>();
            trigger.isTrigger = true;
            if (visualRenderer == null) visualRenderer = GetComponentInChildren<Renderer>();
        }

        public void Configure(PartyPlayer placingOwner)
        {
            owner = placingOwner;
        }

        private void Update()
        {
            aliveTime += Time.deltaTime;
            // Fade in a bit more once armed (still low alpha — "隐形" per design).
            if (visualRenderer != null && aliveTime >= armDelay)
            {
                var c = visualRenderer.material.color;
                c.a = 0.35f;
                visualRenderer.material.color = c;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (triggered) return;
            if (aliveTime < armDelay) return;

            if (other.TryGetComponent(out PartyPlayer victim))
            {
                triggered = true;
                float stun = stunDurationOverride > 0f
                    ? stunDurationOverride
                    : (PartyGameManager.Instance != null && PartyGameManager.Instance.Config != null
                        ? PartyGameManager.Instance.Config.mineStunDuration
                        : 5f);
                victim.Stun(stun);
                // Offer the mine to the victim's inventory so they can re-throw once un-stunned.
                if (mineItemData != null) victim.TryEquipItem(mineItemData);
                Destroy(gameObject);
            }
        }
    }
}
