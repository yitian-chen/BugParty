using System;
using System.Collections.Generic;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Local (non-networked) master controller for a Party Fishing match.
    /// Owns the state machine, timers, wave scheduling, and settlement.
    ///
    /// Not a NetworkBehaviour on purpose — phase A runs single-machine.
    /// Phase C will fold this authority into the host via NetworkVariables + RPCs.
    /// </summary>
    public class PartyGameManager : MonoBehaviour
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

        [Tooltip("If true, immediately skip WaitingToStart on scene load (useful during phase A prototyping).")]
        [SerializeField] private bool autoStart = true;

        private State state = State.WaitingToStart;
        private float countdownTimer;
        private float matchTimer;
        private bool frenzyTriggered;

        public event EventHandler OnStateChanged;
        public event EventHandler OnFrenzyStarted;

        public PartyGameConfig Config => config;
        public State CurrentState => state;
        public float CountdownTimer => countdownTimer;
        public float MatchTimeRemaining => Mathf.Max(0f, matchTimer);
        public float MatchTimeElapsed => (config != null ? config.matchDuration : 0f) - matchTimer;
        public bool IsFrenzy => frenzyTriggered;
        public IReadOnlyList<Island> Islands => islands;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (autoStart)
            {
                BeginCountdown();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void BeginCountdown()
        {
            if (state != State.WaitingToStart) return;
            if (config == null)
            {
                Debug.LogError("[PartyGameManager] Config is not assigned.");
                return;
            }
            countdownTimer = config.countdownToStart;
            ChangeState(State.CountdownToStart);
        }

        private void Update()
        {
            switch (state)
            {
                case State.WaitingToStart:
                    break;

                case State.CountdownToStart:
                    countdownTimer -= Time.deltaTime;
                    if (countdownTimer <= 0f)
                    {
                        matchTimer = config.matchDuration;
                        frenzyTriggered = false;
                        EquipDefaultLoadout();
                        ChangeState(State.GamePlaying);
                        spawner?.OnMatchStarted();
                    }
                    break;

                case State.GamePlaying:
                    matchTimer -= Time.deltaTime;
                    CheckFrenzyStart();
                    if (matchTimer <= 0f)
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
            if (frenzyTriggered || config == null) return;
            if (MatchTimeElapsed >= config.frenzyStartTime)
            {
                frenzyTriggered = true;
                OnFrenzyStarted?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ChangeState(State newState)
        {
            state = newState;
            OnStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool IsGamePlaying() => state == State.GamePlaying;
        public bool IsGameOver() => state == State.GameOver;
        public bool IsCountdownToStartActive() => state == State.CountdownToStart;
        public bool IsWaitingToStart() => state == State.WaitingToStart;

        public float GetFrenzyMoveMultiplier()
        {
            return frenzyTriggered && config != null ? config.frenzyMoveMultiplier : 1f;
        }

        public float GetFrenzyFishingSpeedMultiplier()
        {
            return frenzyTriggered && config != null ? config.frenzyFishingSpeedMultiplier : 1f;
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
            foreach (PartyPlayer p in all)
            {
                if (defaultFishingItem != null) p.TryEquipItem(defaultFishingItem);
                if (defaultDisruptionItem != null) p.TryEquipItem(defaultDisruptionItem);
            }
        }
    }
}
