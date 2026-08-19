using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// A player-owned island with a "fish deposit platform".
    /// Server-authoritative counts synced to clients via NetworkVariables.
    /// </summary>
    public class Island : NetworkBehaviour
    {
        [SerializeField] private int ownerPlayerIndex;
        [SerializeField] private Transform depositAnchor;

        private NetworkVariable<int> netCommon = new NetworkVariable<int>(0);
        private NetworkVariable<int> netGolden = new NetworkVariable<int>(0);

        private readonly HashSet<PartyPlayer> playersOnPlatform = new HashSet<PartyPlayer>();

        public event EventHandler OnFishCountChanged;

        public int OwnerPlayerIndex => ownerPlayerIndex;
        public int CommonFishCount => netCommon.Value;
        public int GoldenFishCount => netGolden.Value;

        private bool IsSoloMode => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
        private bool CanAuthor => IsSoloMode || IsServer;

        public override void OnNetworkSpawn()
        {
            netCommon.OnValueChanged += (a, b) => OnFishCountChanged?.Invoke(this, EventArgs.Empty);
            netGolden.OnValueChanged += (a, b) => OnFishCountChanged?.Invoke(this, EventArgs.Empty);
        }

        public int GetScore(PartyGameConfig config)
        {
            int c = netCommon.Value, g = netGolden.Value;
            if (config == null) return c + g * 2;
            return c * config.commonFishScore + g * config.goldenFishScore;
        }

        public bool ContainsPlayer(PartyPlayer player) => playersOnPlatform.Contains(player);

        public void DepositAll(PartyPlayer player)
        {
            if (!CanAuthor || player == null) return;
            (int common, int golden) = player.DrainCarriedFish();
            if (common > 0) netCommon.Value += common;
            if (golden > 0) netGolden.Value += golden;
            if ((common > 0 || golden > 0) && IsSoloMode)
                OnFishCountChanged?.Invoke(this, EventArgs.Empty);
        }

        public FishType? DepositOne(PartyPlayer player)
        {
            if (!CanAuthor || player == null) return null;
            if (player.CarriedFishTotal <= 0) return null;
            FishType t = player.RemoveOneFishForDeposit();
            if (t == FishType.Common) netCommon.Value++; else netGolden.Value++;
            if (IsSoloMode) OnFishCountChanged?.Invoke(this, EventArgs.Empty);
            return t;
        }

        public FishType? StealOne(PartyPlayer thief)
        {
            if (!CanAuthor || thief == null) return null;
            if (thief.CarriedFishTotal >= thief.RaftFishCapacity) return null;
            FishType? stolen = null;
            if (netCommon.Value > 0) { netCommon.Value--; stolen = FishType.Common; }
            else if (netGolden.Value > 0) { netGolden.Value--; stolen = FishType.Golden; }
            if (stolen != null)
            {
                thief.AddFish(stolen.Value, 1);
                if (IsSoloMode) OnFishCountChanged?.Invoke(this, EventArgs.Empty);
            }
            return stolen;
        }

        /// <summary>
        /// Server-only: reserve one fish for a delayed transfer (e.g. hook grapple). Decrements the
        /// island's count immediately so nothing else can grab it, but does NOT add to the thief's
        /// raft — the caller must call thief.AddFish later when the flying-fish visual arrives.
        /// </summary>
        public FishType? ReserveSteal(PartyPlayer thief)
        {
            if (!CanAuthor || thief == null) return null;
            if (thief.CarriedFishTotal >= thief.RaftFishCapacity) return null;
            FishType? stolen = null;
            if (netCommon.Value > 0) { netCommon.Value--; stolen = FishType.Common; }
            else if (netGolden.Value > 0) { netGolden.Value--; stolen = FishType.Golden; }
            if (stolen != null && IsSoloMode) OnFishCountChanged?.Invoke(this, EventArgs.Empty);
            return stolen;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!CanAuthor) return; // server tracks presence; clients don't
            if (other.TryGetComponent(out PartyPlayer player))
            {
                playersOnPlatform.Add(player);
                player.SetCurrentIsland(this);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!CanAuthor) return;
            if (other.TryGetComponent(out PartyPlayer player))
            {
                playersOnPlatform.Remove(player);
                if (player.CurrentIsland == this) player.SetCurrentIsland(null);
            }
        }
    }
}
