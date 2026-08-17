using System;
using System.Collections.Generic;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// A spawned fishing spot in the world. Holds fish count, lifetime, and the players inside it.
    /// Players call <see cref="TryConsumeFish"/> when their <see cref="FishingAction"/> finishes.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class FishingSpot : MonoBehaviour
    {
        [SerializeField] private FishType fishType = FishType.Common;
        [SerializeField] private GameObject visualCommon;
        [SerializeField] private GameObject visualGolden;
        [Tooltip("Start blinking when remaining lifetime falls below this many seconds. 0 disables blinking.")]
        [SerializeField] private float blinkWarningSeconds = 8f;
        [Tooltip("Slowest blink period (used at the start of the warning window).")]
        [SerializeField] private float blinkPeriodSlow = 0.5f;
        [Tooltip("Fastest blink period (used right before expiry).")]
        [SerializeField] private float blinkPeriodFast = 0.1f;

        private int remainingFish;
        private float lifetime;
        private bool unlimited;
        private bool paused;
        private SphereCollider trigger;

        private readonly HashSet<PartyPlayer> playersInside = new HashSet<PartyPlayer>();

        public event EventHandler OnRemainingChanged;
        public event EventHandler OnExpired;

        public FishType FishType => fishType;
        public int RemainingFish => remainingFish;
        public float LifetimeRemaining => lifetime;
        public bool IsExpired { get; private set; }

        private void Awake()
        {
            trigger = GetComponent<SphereCollider>();
            trigger.isTrigger = true;
        }

        public void Initialize(FishType type, int startingFish, float initialLifetime, float radius)
        {
            fishType = type;
            unlimited = startingFish < 0;
            remainingFish = unlimited ? int.MaxValue : startingFish;
            lifetime = initialLifetime;
            IsExpired = false;

            if (trigger != null) trigger.radius = radius;
            if (visualCommon != null) visualCommon.SetActive(type == FishType.Common);
            if (visualGolden != null) visualGolden.SetActive(type == FishType.Golden);

            // Reset blink state so a re-used pooled instance doesn't start hidden.
            blinkAccumulator = 0f;
            blinkOn = true;
        }

        private void Update()
        {
            if (IsExpired || paused) return;
            lifetime -= Time.deltaTime;
            UpdateBlink();
            if (lifetime <= 0f)
            {
                Expire();
            }
        }

        private float blinkAccumulator;
        private bool blinkOn = true;

        private void UpdateBlink()
        {
            GameObject visual = fishType == FishType.Common ? visualCommon : visualGolden;
            if (visual == null) return;

            if (blinkWarningSeconds <= 0f || lifetime > blinkWarningSeconds)
            {
                if (!blinkOn) { visual.SetActive(true); blinkOn = true; }
                return;
            }

            // Ramp period from slow -> fast as we approach expiry.
            float t = Mathf.Clamp01(1f - lifetime / blinkWarningSeconds); // 0 at start of window, 1 at expiry
            float period = Mathf.Lerp(blinkPeriodSlow, blinkPeriodFast, t);
            blinkAccumulator += Time.deltaTime;
            if (blinkAccumulator >= period)
            {
                blinkAccumulator = 0f;
                blinkOn = !blinkOn;
                visual.SetActive(blinkOn);
            }
        }

        public void SetPaused(bool p)
        {
            paused = p;
        }

        public bool TryConsumeFish(int amount, out int actualConsumed)
        {
            actualConsumed = 0;
            if (IsExpired) return false;

            if (unlimited)
            {
                actualConsumed = amount;
                OnRemainingChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }

            actualConsumed = Mathf.Min(amount, remainingFish);
            remainingFish -= actualConsumed;
            OnRemainingChanged?.Invoke(this, EventArgs.Empty);

            if (remainingFish <= 0)
            {
                Expire();
            }
            return actualConsumed > 0;
        }

        private void Expire()
        {
            if (IsExpired) return;
            IsExpired = true;
            OnExpired?.Invoke(this, EventArgs.Empty);
            // Only the server destroys/despawns; clients let the replicated despawn cascade in.
            if (Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsListening)
            {
                Destroy(gameObject);
                return;
            }
            var netObj = GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                if (Unity.Netcode.NetworkManager.Singleton.IsServer) netObj.Despawn(true);
            }
            else
            {
                if (Unity.Netcode.NetworkManager.Singleton.IsServer) Destroy(gameObject);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            // Trigger presence is used by the server to answer "which spot is this player at?" —
            // in a networked game the client copies never influence that decision. So only the
            // server (or solo mode) tracks players inside.
            if (Unity.Netcode.NetworkManager.Singleton != null
                && Unity.Netcode.NetworkManager.Singleton.IsListening
                && !Unity.Netcode.NetworkManager.Singleton.IsServer) return;

            if (other.TryGetComponent(out PartyPlayer player))
            {
                playersInside.Add(player);
                player.SetCurrentFishingSpot(this);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (Unity.Netcode.NetworkManager.Singleton != null
                && Unity.Netcode.NetworkManager.Singleton.IsListening
                && !Unity.Netcode.NetworkManager.Singleton.IsServer) return;

            if (other.TryGetComponent(out PartyPlayer player))
            {
                playersInside.Remove(player);
                if (player.CurrentFishingSpot == this) player.SetCurrentFishingSpot(null);
            }
        }
    }
}
