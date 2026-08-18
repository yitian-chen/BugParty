using UnityEngine;
using Unity.Netcode;

namespace PartyGame
{
    /// <summary>
    /// Server-only AI brain for a PartyPlayer flagged as a bot.
    /// Runs a tiny state machine: SeekFish -> Fish -> ReturnHome -> Deposit -> loop.
    ///
    /// Attached at runtime by PartyPlayerSpawner after Spawn(true). Only ticks on the server
    /// (or in solo mode). Feeds movement to PartyPlayer.SetAIMovement and triggers actions
    /// via PartyPlayer.AI_TryInteract / AI_TryDepositOne.
    /// </summary>
    [RequireComponent(typeof(PartyPlayer))]
    public class PartyPlayerAI : MonoBehaviour
    {
        private enum State { SeekFish, Fishing, ReturnHome, Deposit }

        private PartyPlayer player;
        private State state;
        private FishingSpot targetSpot;
        private Vector3 wanderOffset;
        private float decisionTimer;
        private float actionCooldown;

        [Tooltip("How often (seconds) the AI re-evaluates its goal.")]
        [SerializeField] private float decisionInterval = 0.5f;
        [Tooltip("Cone half-angle (degrees) within which the bot considers itself aimed at target and drives forward.")]
        [SerializeField] private float aimTolerance = 25f;
        [Tooltip("Distance at which the bot considers 'arrived' at target and stops.")]
        [SerializeField] private float arriveDistance = 1.5f;
        [Tooltip("How often to press E/Q (avoid spamming server RPC-equivalent methods each frame).")]
        [SerializeField] private float actionInterval = 0.3f;

        private bool IsSoloMode => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
        private bool CanAct => IsSoloMode || NetworkManager.Singleton.IsServer;

        private void Awake()
        {
            player = GetComponent<PartyPlayer>();
            // Small per-bot random wander so multiple bots don't stack on the exact same spot.
            wanderOffset = new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f));
        }

        private void Update()
        {
            if (!CanAct) return;                       // Clients never tick AI.
            if (player == null || !player.IsBot) return;
            if (player.IsStunned) { player.SetAIMovement(Vector3.zero); return; }
            if (PartyGameManager.Instance == null || !PartyGameManager.Instance.IsGamePlaying())
            {
                player.SetAIMovement(Vector3.zero);
                return;
            }

            decisionTimer -= Time.deltaTime;
            actionCooldown -= Time.deltaTime;
            if (decisionTimer <= 0f)
            {
                decisionTimer = decisionInterval;
                Decide();
            }

            DriveTowardTarget();
        }

        // --- Decisions ---

        private void Decide()
        {
            // Load full → head home to deposit.
            bool full = player.CarriedFishTotal >= player.RaftFishCapacity;
            bool hasFish = player.CarriedFishTotal > 0;

            // Priority 1: If actively fishing, keep fishing.
            if (player.ActiveFishing != null && !player.ActiveFishing.IsFinished)
            {
                state = State.Fishing;
                return;
            }

            // Priority 2: Full or (has fish AND no live spot) → go home.
            var spot = FindBestSpot();
            if (full || (hasFish && spot == null))
            {
                targetSpot = null;
                state = State.ReturnHome;
                return;
            }

            // Priority 3: Chase a spot.
            if (spot != null)
            {
                targetSpot = spot;
                state = State.SeekFish;
                return;
            }

            // No spot, no fish: idle at home.
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
                // Prefer golden > common by big margin; then closer > farther.
                float score = (s.FishType == FishType.Golden ? 100f : 0f) - dist;
                if (score > bestScore) { bestScore = score; best = s; }
            }
            return best;
        }

        // --- Movement / action execution ---

        private void DriveTowardTarget()
        {
            Vector3 targetPos = GetTargetPosition();
            Vector3 delta = targetPos - transform.position;
            delta.y = 0f;
            float dist = delta.magnitude;

            // Trigger context-appropriate actions when close enough.
            if (state == State.SeekFish && targetSpot != null && player.CurrentFishingSpot == targetSpot)
            {
                // Stop and let interact fire.
                player.SetAIMovement(Vector3.zero);
                if (actionCooldown <= 0f)
                {
                    actionCooldown = actionInterval;
                    player.AI_TryInteract();
                }
                return;
            }

            if (state == State.ReturnHome && player.CurrentIsland != null
                && player.CurrentIsland.OwnerPlayerIndex == player.PlayerIndex)
            {
                player.SetAIMovement(Vector3.zero);
                if (player.CarriedFishTotal > 0 && actionCooldown <= 0f)
                {
                    actionCooldown = actionInterval;
                    player.AI_TryDepositOne();
                }
                return;
            }

            if (dist <= arriveDistance)
            {
                player.SetAIMovement(Vector3.zero);
                return;
            }

            // Aim + drive forward.
            Vector3 desiredDir = delta.normalized;
            Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
            float signedAngle = Vector3.SignedAngle(fwd, desiredDir, Vector3.up);
            float turn = Mathf.Clamp(signedAngle / 45f, -1f, 1f); // proportional turn
            float forward = Mathf.Abs(signedAngle) < aimTolerance ? 1f : 0f;
            player.SetAIMovement(new Vector3(turn, 0f, forward));
        }

        private Vector3 GetTargetPosition()
        {
            switch (state)
            {
                case State.SeekFish:
                    if (targetSpot != null && !targetSpot.IsExpired) return targetSpot.transform.position + wanderOffset;
                    return transform.position; // nothing to do
                case State.ReturnHome:
                case State.Deposit:
                    var island = PartyGameManager.Instance != null
                        ? PartyGameManager.Instance.GetIslandOfPlayer(player.PlayerIndex) : null;
                    if (island != null) return island.transform.position + wanderOffset;
                    return transform.position;
                default:
                    return transform.position;
            }
        }
    }
}
