using System;
using System.Collections.Generic;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// A player-owned island with a "fish deposit platform".
    /// Own-owner players drop their whole raft catch here in one action.
    /// Other players standing here can steal one fish per interact.
    /// </summary>
    public class Island : MonoBehaviour
    {
        [SerializeField] private int ownerPlayerIndex;
        [SerializeField] private Transform depositAnchor;

        private int commonFishCount;
        private int goldenFishCount;
        private readonly HashSet<PartyPlayer> playersOnPlatform = new HashSet<PartyPlayer>();

        public event EventHandler OnFishCountChanged;

        public int OwnerPlayerIndex => ownerPlayerIndex;
        public int CommonFishCount => commonFishCount;
        public int GoldenFishCount => goldenFishCount;

        public int GetScore(PartyGameConfig config)
        {
            if (config == null) return commonFishCount + goldenFishCount * 2;
            return commonFishCount * config.commonFishScore + goldenFishCount * config.goldenFishScore;
        }

        public bool ContainsPlayer(PartyPlayer player) => playersOnPlatform.Contains(player);

        public void DepositAll(PartyPlayer player)
        {
            if (player == null) return;
            (int common, int golden) = player.DrainCarriedFish();
            commonFishCount += common;
            goldenFishCount += golden;
            if (common > 0 || golden > 0)
            {
                OnFishCountChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Deposit a single fish. Returns the type deposited (Common preferred), or null if raft empty.</summary>
        public FishType? DepositOne(PartyPlayer player)
        {
            if (player == null) return null;
            if (player.CarriedFishTotal <= 0) return null;
            FishType t = player.RemoveOneFishForDeposit();
            if (t == FishType.Common) commonFishCount++; else goldenFishCount++;
            OnFishCountChanged?.Invoke(this, EventArgs.Empty);
            return t;
        }

        /// <summary>Steal a single fish. Returns the type stolen (Common preferred), or null if empty.</summary>
        public FishType? StealOne(PartyPlayer thief)
        {
            if (thief == null) return null;
            if (thief.CarriedFishTotal >= thief.RaftFishCapacity) return null;

            FishType? stolen = null;
            if (commonFishCount > 0)
            {
                commonFishCount--;
                stolen = FishType.Common;
            }
            else if (goldenFishCount > 0)
            {
                goldenFishCount--;
                stolen = FishType.Golden;
            }

            if (stolen != null)
            {
                thief.AddFish(stolen.Value, 1);
                OnFishCountChanged?.Invoke(this, EventArgs.Empty);
            }
            return stolen;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent(out PartyPlayer player))
            {
                playersOnPlatform.Add(player);
                player.SetCurrentIsland(this);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.TryGetComponent(out PartyPlayer player))
            {
                playersOnPlatform.Remove(player);
                if (player.CurrentIsland == this)
                {
                    player.SetCurrentIsland(null);
                }
            }
        }
    }
}
