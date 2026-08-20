using Unity.Netcode;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// A placed mine. Server ticks armDelay and detects OnTriggerEnter; on hit it stuns
    /// the victim via a NetworkBehaviour path (Stun mutates a NetworkVariable) and despawns.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class Mine : NetworkBehaviour
    {
        [SerializeField] private float armDelay = 1.0f;
        [SerializeField] private float stunDurationOverride = -1f;
        [SerializeField] private ItemDataSO mineItemData;
        [SerializeField] private Renderer visualRenderer;

        private PartyPlayer owner;
        private float aliveTime;
        private bool triggered;
        private SphereCollider trigger;

        public ItemDataSO ItemData => mineItemData;

        private bool IsSoloMode => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
        private bool CanAuthor => IsSoloMode || IsServer;

        private void Awake()
        {
            trigger = GetComponent<SphereCollider>();
            trigger.isTrigger = true;
            if (visualRenderer == null) visualRenderer = GetComponentInChildren<Renderer>();
        }

        public void Configure(PartyPlayer placingOwner) => owner = placingOwner;

        private void Update()
        {
            aliveTime += Time.deltaTime;
            if (visualRenderer != null && aliveTime >= armDelay)
            {
                // Visual only; safe on both server + client.
                var c = visualRenderer.material.color;
                if (c.a < 0.5f) { c.a = 0.5f; visualRenderer.material.color = c; }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!CanAuthor) return; // Only the authoritative side reacts to triggers.
            if (triggered) return;
            if (aliveTime < armDelay) return;

            if (other.TryGetComponent(out PartyPlayer victim))
            {
                triggered = true;
                float stun = stunDurationOverride > 0f
                    ? stunDurationOverride
                    : (PartyGameManager.Instance != null && PartyGameManager.Instance.Config != null
                        ? PartyGameManager.Instance.Config.mineStunDuration : 5f);
                victim.Stun(stun);
                if (mineItemData != null) victim.TryEquipItem(mineItemData);

                // Explosion SFX. In solo mode we just play locally; networked, we broadcast a
                // ClientRpc so every peer hears it (only the server sees the trigger).
                if (IsSoloMode)
                {
                    var sm = SoundManager.Instance;
                    if (sm != null && sm.Library != null) sm.PlaySfx(sm.Library.sfxExplode);
                }
                else
                {
                    PlayExplosionSfxClientRpc();
                }

                if (!IsSoloMode)
                {
                    var netObj = GetComponent<NetworkObject>();
                    if (netObj != null && netObj.IsSpawned) { netObj.Despawn(true); return; }
                }
                Destroy(gameObject);
            }
        }

        [ClientRpc]
        private void PlayExplosionSfxClientRpc()
        {
            var sm = SoundManager.Instance;
            if (sm != null && sm.Library != null) sm.PlaySfx(sm.Library.sfxExplode);
        }
    }
}
