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
        }

        private void Update()
        {
            if (IsExpired || paused) return;
            lifetime -= Time.deltaTime;
            if (lifetime <= 0f)
            {
                Expire();
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
            Destroy(gameObject);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent(out PartyPlayer player))
            {
                playersInside.Add(player);
                player.SetCurrentFishingSpot(this);
                Debug.Log($"[FishingSpot {name}] Enter by P{player.PlayerIndex}");
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.TryGetComponent(out PartyPlayer player))
            {
                playersInside.Remove(player);
                if (player.CurrentFishingSpot == this)
                {
                    player.SetCurrentFishingSpot(null);
                }
                Debug.Log($"[FishingSpot {name}] Exit by P{player.PlayerIndex}");
            }
        }
    }
}
