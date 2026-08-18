using UnityEngine;
using Unity.Netcode;

namespace PartyGame
{
    /// <summary>
    /// Server-only AI brain for a PartyPlayer flagged as a bot.
    /// State machine: SeekFish / StealFish / ReturnHome / Fishing / Depositing.
    ///
    /// Each bot rolls a personality at spawn (Catcher/Mixed/Thief archetype + small jitter):
    ///   - stealBias      : preference for stealing from enemy islands vs. fishing
    ///   - minePropensity : how eagerly the bot drops mines near enemies / enemy islands
    ///   - aggression     : scales decision + action frequency
    /// </summary>
    [RequireComponent(typeof(PartyPlayer))]
    public class PartyPlayerAI : MonoBehaviour
    {
        private enum State { SeekFish, StealFish, ReturnHome, Fishing, Depositing }

        [System.Serializable]
        public struct Personality
        {
            [Range(0f, 1f)] public float stealBias;
            [Range(0f, 1f)] public float minePropensity;
            [Range(0.5f, 2.0f)] public float aggression;

            public static Personality Catcher => new Personality { stealBias = 0.15f, minePropensity = 0.20f, aggression = 0.9f };
            public static Personality Mixed   => new Personality { stealBias = 0.50f, minePropensity = 0.45f, aggression = 1.0f };
            public static Personality Thief   => new Personality { stealBias = 0.85f, minePropensity = 0.70f, aggression = 1.2f };
        }

        [Header("Personality (auto-randomized on Awake unless disabled)")]
        [SerializeField] private Personality personality = new Personality { stealBias = 0.5f, minePropensity = 0.4f, aggression = 1f };
        [SerializeField] private bool randomizePersonalityOnAwake = true;

        [Header("Tuning")]
        [Tooltip("How often (seconds) the AI re-evaluates its goal.")]
        [SerializeField] private float decisionInterval = 0.5f;
        [Tooltip("Cone half-angle (degrees) within which the bot considers itself aimed at target and drives forward.")]
        [SerializeField] private float aimTolerance = 25f;
        [Tooltip("Distance at which the bot considers 'arrived' at target and stops.")]
        [SerializeField] private float arriveDistance = 1.5f;
        [Tooltip("Base cooldown (seconds) between AI-triggered actions (E/Q/item use).")]
        [SerializeField] private float actionInterval = 0.35f;
        [Tooltip("Minimum seconds between mine drops per bot.")]
        [SerializeField] private float mineCooldown = 4f;
        [Tooltip("Range within which the bot considers dropping a mine near an enemy or enemy island.")]
        [SerializeField] private float mineDropRange = 6f;

        private PartyPlayer player;
        private State state;
        private FishingSpot targetSpot;
        private Island targetEnemyIsland;
        private Vector3 wanderOffset;
        private float decisionTimer;
        private float actionCooldown;
        private float mineCooldownTimer;

        private bool IsSoloMode => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
        private bool CanAct => IsSoloMode || NetworkManager.Singleton.IsServer;

        public Personality PersonalityData => personality;

        private void Awake()
        {
            player = GetComponent<PartyPlayer>();
            wanderOffset = new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f));

            if (randomizePersonalityOnAwake)
            {
                float roll = Random.value;
                Personality archetype = roll < 0.34f ? Personality.Catcher
                                      : roll < 0.67f ? Personality.Mixed
                                      : Personality.Thief;
                personality = new Personality
                {
                    stealBias      = Mathf.Clamp01(archetype.stealBias      + Random.Range(-0.10f, 0.10f)),
                    minePropensity = Mathf.Clamp01(archetype.minePropensity + Random.Range(-0.10f, 0.10f)),
                    aggression     = Mathf.Clamp(archetype.aggression       + Random.Range(-0.10f, 0.10f), 0.5f, 2.0f),
                };
            }
        }

        private void Update()
        {
            if (!CanAct) return;
            if (player == null || !player.IsBot) return;
            if (player.IsStunned) { player.SetAIMovement(Vector3.zero); return; }
            if (PartyGameManager.Instance == null || !PartyGameManager.Instance.IsGamePlaying())
            {
                player.SetAIMovement(Vector3.zero);
                return;
            }

            float dt = Time.deltaTime;
            decisionTimer -= dt;
            actionCooldown -= dt;
            mineCooldownTimer -= dt;

            if (decisionTimer <= 0f)
            {
                decisionTimer = decisionInterval / Mathf.Max(0.5f, personality.aggression);
                Decide();
            }

            DriveTowardTarget();
            MaybeDropMine();
        }

        // --- Decision layer ---

        private void Decide()
        {
            if (player.ActiveFishing != null && !player.ActiveFishing.IsFinished)
            {
                state = State.Fishing;
                return;
            }

            bool full = player.CarriedFishTotal >= player.RaftFishCapacity;
            bool hasFish = player.CarriedFishTotal > 0;

            // 1) Full → deposit run.
            if (full)
            {
                targetSpot = null;
                targetEnemyIsland = null;
                state = State.ReturnHome;
                return;
            }

            // 2) Personality-biased pick between fishing and stealing.
            var spot = FindBestSpot();
            var enemyIsland = FindEnemyIslandWithFish();
            bool preferSteal = Random.value < personality.stealBias;
            // If already carrying some fish, nudge toward opportunistic stealing.
            if (hasFish && enemyIsland != null && Random.value < 0.35f) preferSteal = true;

            if (preferSteal && enemyIsland != null)
            {
                targetEnemyIsland = enemyIsland;
                targetSpot = null;
                state = State.StealFish;
                return;
            }

            if (spot != null)
            {
                targetSpot = spot;
                targetEnemyIsland = null;
                state = State.SeekFish;
                return;
            }

            // 3) Fallbacks — no fishing spot: steal if we can, otherwise return home.
            if (enemyIsland != null)
            {
                targetEnemyIsland = enemyIsland;
                targetSpot = null;
                state = State.StealFish;
                return;
            }

            state = State.ReturnHome;
        }

        private FishingSpot FindBestSpot()
        {
            var spots = Object.FindObjectsOfType<FishingSpot>();
            FishingSpot best = null;
            float bestScore = float.NegativeInfinity;
            foreach (var s in spots)
            {
                if (s == null || s.IsExpired) continue;
                if (s.RemainingFish <= 0) continue;
                float dist = Vector3.Distance(transform.position, s.transform.position);
                float score = (s.FishType == FishType.Golden ? 100f : 0f) - dist;
                if (score > bestScore) { bestScore = score; best = s; }
            }
            return best;
        }

        private Island FindEnemyIslandWithFish()
        {
            var mgr = PartyGameManager.Instance;
            if (mgr == null) return null;
            Island best = null;
            float bestScore = float.NegativeInfinity;
            foreach (var island in mgr.Islands)
            {
                if (island == null) continue;
                if (island.OwnerPlayerIndex == player.PlayerIndex) continue;
                int stock = island.CommonFishCount + island.GoldenFishCount;
                if (stock <= 0) continue;
                float dist = Vector3.Distance(transform.position, island.transform.position);
                // Prefer islands with more fish; nearer > farther.
                float score = stock * 8f - dist;
                if (score > bestScore) { bestScore = score; best = island; }
            }
            return best;
        }

        // --- Movement / action ---

        private void DriveTowardTarget()
        {
            Vector3 targetPos = GetTargetPosition();
            Vector3 delta = targetPos - transform.position;
            delta.y = 0f;
            float dist = delta.magnitude;

            switch (state)
            {
                case State.SeekFish:
                    if (targetSpot != null && player.CurrentFishingSpot == targetSpot)
                    {
                        player.SetAIMovement(Vector3.zero);
                        FireActionIfReady(() => player.AI_TryInteract());
                        return;
                    }
                    break;

                case State.StealFish:
                    // Stealing uses the same E-key path: DoInteract_Server steals from currentIsland
                    // when the bot is on a non-owner island and has capacity. We just need to enter the trigger.
                    if (targetEnemyIsland != null && player.CurrentIsland == targetEnemyIsland
                        && player.CarriedFishTotal < player.RaftFishCapacity)
                    {
                        player.SetAIMovement(Vector3.zero);
                        FireActionIfReady(() => player.AI_TryInteract());
                        return;
                    }
                    if (targetEnemyIsland != null
                        && (targetEnemyIsland.CommonFishCount + targetEnemyIsland.GoldenFishCount <= 0
                            || player.CarriedFishTotal >= player.RaftFishCapacity))
                    {
                        decisionTimer = 0f; // Re-decide on next tick.
                    }
                    break;

                case State.ReturnHome:
                case State.Depositing:
                    if (player.CurrentIsland != null && player.CurrentIsland.OwnerPlayerIndex == player.PlayerIndex)
                    {
                        player.SetAIMovement(Vector3.zero);
                        if (player.CarriedFishTotal > 0)
                            FireActionIfReady(() => player.AI_TryDepositOne());
                        return;
                    }
                    break;

                case State.Fishing:
                    // Freeze — any input cancels fishing.
                    player.SetAIMovement(Vector3.zero);
                    return;
            }

            if (dist <= arriveDistance)
            {
                player.SetAIMovement(Vector3.zero);
                return;
            }

            Vector3 desiredDir = delta.normalized;
            Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
            float signedAngle = Vector3.SignedAngle(fwd, desiredDir, Vector3.up);
            float turn = Mathf.Clamp(signedAngle / 45f, -1f, 1f);
            float forward = Mathf.Abs(signedAngle) < aimTolerance ? 1f : 0f;
            player.SetAIMovement(new Vector3(turn, 0f, forward));
        }

        private void FireActionIfReady(System.Action a)
        {
            if (actionCooldown > 0f) return;
            actionCooldown = actionInterval / Mathf.Max(0.5f, personality.aggression);
            a?.Invoke();
        }

        private Vector3 GetTargetPosition()
        {
            switch (state)
            {
                case State.SeekFish:
                    if (targetSpot != null && !targetSpot.IsExpired) return targetSpot.transform.position + wanderOffset;
                    return transform.position;
                case State.StealFish:
                    if (targetEnemyIsland != null) return targetEnemyIsland.transform.position + wanderOffset;
                    return transform.position;
                case State.ReturnHome:
                case State.Depositing:
                    var home = PartyGameManager.Instance != null
                        ? PartyGameManager.Instance.GetIslandOfPlayer(player.PlayerIndex) : null;
                    if (home != null) return home.transform.position + wanderOffset;
                    return transform.position;
                default:
                    return transform.position;
            }
        }

        // --- Mines ---

        private void MaybeDropMine()
        {
            if (mineCooldownTimer > 0f) return;
            if (player.CurrentIsland != null) return; // PartyPlayer.UseMine_Server refuses while on an island.
            if (player.ItemSlots == null) return;

            int mineSlot = FindMineSlot();
            if (mineSlot < 0) return;

            bool nearEnemyIsland = state == State.StealFish
                && targetEnemyIsland != null
                && Vector3.Distance(transform.position, targetEnemyIsland.transform.position) < mineDropRange;
            bool nearEnemy = IsNearAnyEnemy(mineDropRange);

            float roll = Random.value;
            bool shouldDrop = false;
            if (nearEnemyIsland && roll < personality.minePropensity * 0.5f) shouldDrop = true;
            else if (nearEnemy && roll < personality.minePropensity * 0.35f) shouldDrop = true;
            else if (roll < personality.minePropensity * 0.02f) shouldDrop = true; // rare roaming drop

            if (!shouldDrop) return;

            mineCooldownTimer = mineCooldown;
            player.AI_TryUseItem(mineSlot);
        }

        private int FindMineSlot()
        {
            var slots = player.ItemSlots;
            if (slots == null) return -1;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s != null && !s.IsEmpty && s.data != null && s.data.kind == ItemKind.Mine) return i;
            }
            return -1;
        }

        private bool IsNearAnyEnemy(float range)
        {
            var all = Object.FindObjectsOfType<PartyPlayer>();
            foreach (var p in all)
            {
                if (p == null || p == player) continue;
                if (p.PlayerIndex == player.PlayerIndex) continue;
                if (Vector3.Distance(transform.position, p.transform.position) <= range) return true;
            }
            return false;
        }
    }
}
