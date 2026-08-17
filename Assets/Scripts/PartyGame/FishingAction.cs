using System;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// A single fishing action performed by a player at a fishing spot.
    /// Ticks a normalized progress that HUD listens to via <see cref="IHasProgress"/>.
    /// Cancels on movement, on target expiry, or on external interruption (knife).
    /// </summary>
    public class FishingAction : IHasProgress
    {
        public event EventHandler<IHasProgress.OnProgressChangedEventArgs> OnProgressChanged;
        public event EventHandler<FishingResultEventArgs> OnFinished;

        public class FishingResultEventArgs : EventArgs
        {
            public bool success;
            public int fishGained;
            public FishType fishType;
            public bool consumedItem;
        }

        private readonly PartyPlayer player;
        private readonly FishingSpot spot;
        private readonly float duration;
        private readonly int fishAmount;
        private readonly ItemInstance sourceItem;

        private float elapsed;
        private bool finished;

        public FishingAction(PartyPlayer player, FishingSpot spot, float duration, int fishAmount, ItemInstance sourceItem)
        {
            this.player = player;
            this.spot = spot;
            this.duration = duration;
            this.fishAmount = fishAmount;
            this.sourceItem = sourceItem;
        }

        public bool IsFinished => finished;
        public FishingSpot Spot => spot;
        public ItemInstance SourceItem => sourceItem;
        public float Duration => duration;
        public float Elapsed => elapsed;
        public float ProgressNormalized => duration > 0f ? Mathf.Clamp01(elapsed / duration) : 0f;

        public void Tick(float deltaTime)
        {
            if (finished) return;
            if (spot == null || spot.IsExpired)
            {
                Cancel();
                return;
            }

            elapsed += deltaTime;
            OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
            {
                progressNormalized = Mathf.Clamp01(elapsed / duration),
            });

            if (elapsed >= duration)
            {
                Complete();
            }
        }

        public void Cancel()
        {
            if (finished) return;
            finished = true;
            OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs { progressNormalized = 0f });
            OnFinished?.Invoke(this, new FishingResultEventArgs
            {
                success = false,
                fishGained = 0,
                fishType = spot != null ? spot.FishType : FishType.Common,
                consumedItem = false,
            });
        }

        /// <summary>
        /// External interruption by e.g. a knife hit. Consumes the source item durability.
        /// </summary>
        public void Interrupt()
        {
            if (finished) return;
            finished = true;
            OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs { progressNormalized = 0f });
            OnFinished?.Invoke(this, new FishingResultEventArgs
            {
                success = false,
                fishGained = 0,
                fishType = spot != null ? spot.FishType : FishType.Common,
                consumedItem = sourceItem != null,
            });
        }

        private void Complete()
        {
            finished = true;
            int gained = 0;
            if (spot != null)
            {
                spot.TryConsumeFish(fishAmount, out gained);
            }
            OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs { progressNormalized = 1f });
            OnFinished?.Invoke(this, new FishingResultEventArgs
            {
                success = gained > 0,
                fishGained = gained,
                fishType = spot != null ? spot.FishType : FishType.Common,
                consumedItem = sourceItem != null,
            });
        }
    }
}
