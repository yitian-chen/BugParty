using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Server-authoritative master controller for a Party Fishing match.
    /// State + timers live in NetworkVariables so clients can read them for HUD/logic.
    /// Only the server ticks the state machine and drives spawns.
    ///
    /// Falls back to solo (non-networked) behavior when NetworkManager isn't listening,
    /// keeping single-machine dev / Play-in-Editor working without a menu.
    /// </summary>
    public class PartyGameManager : NetworkBehaviour
    {
        public static PartyGameManager Instance { get; private set; }

        public enum State
        {
            WaitingToStart,
            CountdownToStart,
            GamePlaying,
            GameOver,
        }

        [SerializeField] private PartyGameConfig config;
        [SerializeField] private FishingSpotSpawner spawner;
        [SerializeField] private List<Island> islands = new List<Island>();

        [Header("Default Loadout (v0.1 — no search phase yet)")]
        [SerializeField] private ItemDataSO defaultFishingItem;
        [SerializeField] private ItemDataSO defaultDisruptionItem;
        [Tooltip("Optional extra seed item (e.g. a demo Mine) to overwrite an empty slot for phase-B testing.")]
        [SerializeField] private ItemDataSO seedItemForDemo;

        [Tooltip("If true, immediately skip WaitingToStart on scene load (useful during phase A prototyping).")]
        [SerializeField] private bool autoStart = true;

        // Networked authoritative fields — server writes, everyone reads.
        private NetworkVariable<int> netState = new NetworkVariable<int>((int)State.WaitingToStart);
        private NetworkVariable<float> netCountdownTimer = new NetworkVariable<float>(0f);
        private NetworkVariable<float> netMatchTimer = new NetworkVariable<float>(0f);
        private NetworkVariable<bool> netFrenzy = new NetworkVariable<bool>(false);

        public event EventHandler OnStateChanged;
        public event EventHandler OnFrenzyStarted;

        public PartyGameConfig Config => config;
        public ItemDataSO DefaultFishingItem => defaultFishingItem;
        public ItemDataSO DefaultDisruptionItem => defaultDisruptionItem;
        public ItemDataSO SeedItemForDemo => seedItemForDemo;
        public State CurrentState => (State)netState.Value;
        public float CountdownTimer => netCountdownTimer.Value;
        public float MatchTimeRemaining => Mathf.Max(0f, netMatchTimer.Value);
        public float MatchTimeElapsed => (config != null ? config.matchDuration : 0f) - netMatchTimer.Value;
        public bool IsFrenzy => netFrenzy.Value;
        public IReadOnlyList<Island> Islands => islands;

        /// <summary>Are we in solo/local dev mode (no networking active)?</summary>
        private bool IsSoloMode => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
        /// <summary>Should this instance mutate authoritative state?</summary>
        private bool CanAuthor => IsSoloMode || IsServer;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public override void OnNetworkSpawn()
        {
            netState.OnValueChanged += HandleStateNet;
            netFrenzy.OnValueChanged += HandleFrenzyNet;
        }

        public override void OnNetworkDespawn()
        {
            netState.OnValueChanged -= HandleStateNet;
            netFrenzy.OnValueChanged -= HandleFrenzyNet;
        }

        private void HandleStateNet(int prev, int cur) => OnStateChanged?.Invoke(this, EventArgs.Empty);
        private void HandleFrenzyNet(bool prev, bool cur) { if (cur && !prev) OnFrenzyStarted?.Invoke(this, EventArgs.Empty); }

        private void Start()
        {
            // In networked mode, wait one full frame after network spawn so the PartyPlayerSpawner
            // has already spawned player objects for every connected client. In solo mode we can
            // just kick off immediately.
            if (autoStart)
            {
                if (IsSoloMode) { if (CanAuthor) BeginCountdown(); }
                else if (IsServer) StartCoroutine(BeginCountdownNextFrame());
            }
        }

        private System.Collections.IEnumerator BeginCountdownNextFrame()
        {
            yield return null; // one frame — spawner runs from OnLoadEventCompleted
            yield return null; // extra safety
            BeginCountdown();
        }

        public void BeginCountdown()
        {
            if (!CanAuthor) return;
            if ((State)netState.Value != State.WaitingToStart) return;
            if (config == null)
            {
                Debug.LogError("[PartyGameManager] Config is not assigned.");
                return;
            }
            netCountdownTimer.Value = config.countdownToStart;
            ChangeState(State.CountdownToStart);
            spawner?.PreSpawnFirstWave();
        }

        private void Update()
        {
            if (!CanAuthor) return; // Clients are pure observers.

            State s = (State)netState.Value;
            switch (s)
            {
                case State.WaitingToStart:
                    break;

                case State.CountdownToStart:
                    netCountdownTimer.Value -= Time.deltaTime;
                    if (netCountdownTimer.Value <= 0f)
                    {
                        netMatchTimer.Value = config.matchDuration;
                        netFrenzy.Value = false;
                        EquipDefaultLoadout();
                        ChangeState(State.GamePlaying);
                        spawner?.OnMatchStarted();
                    }
                    break;

                case State.GamePlaying:
                    netMatchTimer.Value -= Time.deltaTime;
                    CheckFrenzyStart();
                    if (netMatchTimer.Value <= 0f)
                    {
                        ChangeState(State.GameOver);
                    }
                    break;

                case State.GameOver:
                    break;
            }
        }

        private void CheckFrenzyStart()
        {
            if (netFrenzy.Value || config == null) return;
            if (MatchTimeElapsed >= config.frenzyStartTime)
            {
                netFrenzy.Value = true;
                // OnFrenzyStarted event fires via netFrenzy.OnValueChanged for all clients.
            }
        }

        private void ChangeState(State newState)
        {
            netState.Value = (int)newState;
            // OnStateChanged event fires via netState.OnValueChanged (also on server thanks to NGO callback semantics).
        }

        public bool IsGamePlaying() => (State)netState.Value == State.GamePlaying;
        public bool IsGameOver() => (State)netState.Value == State.GameOver;
        public bool IsCountdownToStartActive() => (State)netState.Value == State.CountdownToStart;
        public bool IsWaitingToStart() => (State)netState.Value == State.WaitingToStart;

        public float GetFrenzyMoveMultiplier()
        {
            return netFrenzy.Value && config != null ? config.frenzyMoveMultiplier : 1f;
        }

        public float GetFrenzyFishingSpeedMultiplier()
        {
            return netFrenzy.Value && config != null ? config.frenzyFishingSpeedMultiplier : 1f;
        }

        public Island GetIslandOfPlayer(int playerIndex)
        {
            foreach (Island island in islands)
            {
                if (island != null && island.OwnerPlayerIndex == playerIndex) return island;
            }
            return null;
        }

        private void EquipDefaultLoadout()
        {
            PartyPlayer[] all = FindObjectsOfType<PartyPlayer>();
            foreach (PartyPlayer p in all) EquipDefaultLoadoutFor(p);
        }

        /// <summary>Equip a single player with the default loadout. Safe to call at spawn time (server only).</summary>
        public void EquipDefaultLoadoutFor(PartyPlayer p)
        {
            if (!CanAuthor || p == null) return;
            if (defaultFishingItem != null) p.TryEquipItem(defaultFishingItem);
            if (defaultDisruptionItem != null) p.TryEquipItem(defaultDisruptionItem);
            if (seedItemForDemo != null)
            {
                if (!p.TryEquipItem(seedItemForDemo)) p.ForceReplaceLastSlot(seedItemForDemo);
            }
        }
    }
}
