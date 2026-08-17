using System;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Local-only party-game player controller (phase A).
    /// Handles top-down movement, single fishing action lifecycle, item slots,
    /// and interactions with fishing spots / islands.
    ///
    /// Only one player is expected to drive input via <see cref="GameInput"/>;
    /// remaining slots (for 2-4 player Editor testing) can be driven by additional
    /// controllers in later iterations.
    /// </summary>
    public class PartyPlayer : MonoBehaviour
    {
        [SerializeField] private int playerIndex;
        [SerializeField] private PartyGameConfig config;
        [SerializeField] private bool useGameInput = true;
        [SerializeField] private Transform visualRoot;

        private Rigidbody rb;
        private Vector3 movementInput;
        private Vector3 lastMoveDir = Vector3.forward;

        private int carriedCommon;
        private int carriedGolden;
        private ItemInstance[] itemSlots;

        private FishingAction activeFishing;
        private FishingSpot currentFishingSpot;
        private Island currentIsland;

        private float stunTimer;

        public event EventHandler OnFishingStarted;
        public event EventHandler OnFishingEnded;
        public event EventHandler OnCarriedFishChanged;
        public event EventHandler OnItemsChanged;
        public event EventHandler OnStunned;

        public int PlayerIndex => playerIndex;
        public int CarriedCommon => carriedCommon;
        public int CarriedGolden => carriedGolden;
        public int CarriedFishTotal => carriedCommon + carriedGolden;
        public int RaftFishCapacity => config != null ? config.raftFishCapacity : 2;
        public bool IsStunned => stunTimer > 0f;
        public bool IsWalking => movementInput.sqrMagnitude > 0.01f;
        public FishingAction ActiveFishing => activeFishing;
        public FishingSpot CurrentFishingSpot => currentFishingSpot;
        public Island CurrentIsland => currentIsland;
        public ItemInstance[] ItemSlots => itemSlots;
        public PartyGameConfig Config => config;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            itemSlots = new ItemInstance[config != null ? config.itemSlotCount : 2];
        }

        private bool subscribed;

        private void Start()
        {
            TrySubscribeInput();
        }

        private void OnEnable()
        {
            TrySubscribeInput();
        }

        private void OnDisable()
        {
            if (subscribed && GameInput.Instance != null)
            {
                GameInput.Instance.OnInteractAction -= HandleInteract;
                GameInput.Instance.OnInteractAlternateAction -= HandleInteractAlternate;
                subscribed = false;
            }
        }

        private void TrySubscribeInput()
        {
            if (subscribed || !useGameInput) return;
            if (GameInput.Instance == null)
            {
                Debug.LogWarning($"[PartyPlayer P{playerIndex}] GameInput.Instance is null at subscribe time — will retry.");
                return;
            }
            GameInput.Instance.OnInteractAction += HandleInteract;
            GameInput.Instance.OnInteractAlternateAction += HandleInteractAlternate;
            subscribed = true;
            Debug.Log($"[PartyPlayer P{playerIndex}] Subscribed to GameInput.OnInteractAction.");
        }

        private void Update()
        {
            // Lock movement during pre-match countdown.
            bool locked = PartyGameManager.Instance != null && !PartyGameManager.Instance.IsGamePlaying();

            if (stunTimer > 0f)
            {
                stunTimer -= Time.deltaTime;
                movementInput = Vector3.zero;
            }
            else if (locked)
            {
                movementInput = Vector3.zero;
            }
            else
            {
                ReadMovementInput();
                PollItemHotkeys();
            }

            TickFishing();
            HandleMovement();
        }

        private void PollItemHotkeys()
        {
            if (!useGameInput) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.digit1Key.wasPressedThisFrame) TryUseItem(0);
            if (kb.digit2Key.wasPressedThisFrame) TryUseItem(1);
        }

        public void TryUseItem(int slotIndex)
        {
            if (IsStunned) return;
            if (PartyGameManager.Instance != null && !PartyGameManager.Instance.IsGamePlaying()) return;
            if (itemSlots == null || slotIndex < 0 || slotIndex >= itemSlots.Length) return;

            var inst = itemSlots[slotIndex];
            if (inst == null || inst.IsEmpty) return;

            switch (inst.data.kind)
            {
                case ItemKind.Knife: UseKnife(inst); break;
                case ItemKind.Mine: UseMine(inst); break;
                // Nets are consumed automatically at fishing completion, not by hotkey.
                default: break;
            }
        }

        private void UseKnife(ItemInstance inst)
        {
            float range = config != null ? config.knifeRange : 1.5f;
            PartyPlayer target = FindNearestFishingVictim(range);
            if (target == null) return; // Miss — do not consume durability.

            // Break the victim's fishing (their net/hand item is consumed via Interrupt())
            target.ActiveFishing?.Interrupt();

            inst.durability--;
            if (inst.durability <= 0) ClearEmptySlots();
            OnItemsChanged?.Invoke(this, EventArgs.Empty);
        }

        private PartyPlayer FindNearestFishingVictim(float range)
        {
            PartyPlayer best = null;
            float bestDist = range;
            var all = FindObjectsOfType<PartyPlayer>();
            foreach (var p in all)
            {
                if (p == this) continue;
                if (p.ActiveFishing == null || p.ActiveFishing.IsFinished) continue;
                float d = Vector3.Distance(transform.position, p.transform.position);
                if (d <= bestDist)
                {
                    best = p;
                    bestDist = d;
                }
            }
            return best;
        }

        private void UseMine(ItemInstance inst)
        {
            if (config == null || config.minePrefabRef == null) return;
            // Do not place while standing on an island (avoid griefing self-safe zones).
            if (currentIsland != null) return;

            Vector3 spawnPos = transform.position + lastMoveDir.normalized * 1.2f;
            spawnPos.y = 0.1f;
            var mineGO = Instantiate(config.minePrefabRef, spawnPos, Quaternion.identity);
            var mine = mineGO.GetComponent<Mine>();
            if (mine != null) mine.Configure(this);

            inst.durability--;
            if (inst.durability <= 0) ClearEmptySlots();
            OnItemsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ReadMovementInput()
        {
            if (!useGameInput || GameInput.Instance == null)
            {
                movementInput = Vector3.zero;
                return;
            }
            Vector2 v = GameInput.Instance.GetMovementVectorNormalized();
            movementInput = new Vector3(v.x, 0f, v.y);
        }

        private void HandleMovement()
        {
            // Car-like control: W/S -> forward/back along facing, A/D -> rotate in place.
            float forward = movementInput.z;       // W/S
            float turn = movementInput.x;          // A/D
            bool hasInput = Mathf.Abs(forward) > 0.01f || Mathf.Abs(turn) > 0.01f;

            if (hasInput && activeFishing != null)
            {
                activeFishing.Cancel();
            }

            float frenzyMul = PartyGameManager.Instance != null ? PartyGameManager.Instance.GetFrenzyMoveMultiplier() : 1f;

            // Rotation (in place).
            if (Mathf.Abs(turn) > 0.01f)
            {
                float turnSpeed = 140f * frenzyMul; // deg/sec
                float deltaYaw = turn * turnSpeed * Time.deltaTime;
                if (visualRoot != null)
                {
                    visualRoot.Rotate(0f, deltaYaw, 0f, Space.World);
                }
                else
                {
                    transform.Rotate(0f, deltaYaw, 0f, Space.World);
                }
            }

            // Forward/back translation along the visual's forward vector.
            if (Mathf.Abs(forward) > 0.01f)
            {
                Vector3 fwd = (visualRoot != null ? visualRoot.forward : transform.forward);
                fwd.y = 0f;
                fwd.Normalize();
                lastMoveDir = fwd;

                float speed = (config != null ? config.playerMoveSpeed : 6f) * frenzyMul;
                float moveDistance = forward * speed * Time.deltaTime;
                Vector3 desired = fwd * Mathf.Sign(forward);
                Vector3 delta = TryMove(desired, Mathf.Abs(moveDistance)) * Mathf.Sign(forward);

                transform.position += delta;
            }
        }

        private Vector3 TryMove(Vector3 dir, float distance)
        {
            // Cast radius matches the player's actual CapsuleCollider (~1.5) so the
            // cast footprint aligns with the physical hull — otherwise a smaller cast
            // radius lets the hull sink into obstacles and then subsequent casts from
            // "inside" don't register a hit, trapping the player.
            const float castRadius = 1.4f;
            const float castHeight = 3f;
            Vector3 p1 = transform.position + Vector3.up * castRadius;
            Vector3 p2 = transform.position + Vector3.up * (castHeight - castRadius);

            System.Func<Vector3, bool> Blocked = (d) => {
                var hits = Physics.CapsuleCastAll(p1, p2, castRadius, d, distance, ~0, QueryTriggerInteraction.Ignore);
                foreach (var h in hits)
                {
                    if (h.collider == null) continue;
                    if (h.collider.transform.IsChildOf(transform) || h.collider.transform == transform) continue;
                    return true;
                }
                return false;
            };

            if (!Blocked(dir)) return dir * distance;

            Vector3 dx = new Vector3(dir.x, 0, 0).normalized;
            if (dx.sqrMagnitude > 0.01f && !Blocked(dx)) return dx * distance;

            Vector3 dz = new Vector3(0, 0, dir.z).normalized;
            if (dz.sqrMagnitude > 0.01f && !Blocked(dz)) return dz * distance;

            // Fully blocked. If we've been shoved into geometry (e.g. spawn overlap,
            // mine knockback, or a prior frame's slide), push the player out along the
            // shortest separation vector so we never get permanently stuck.
            TryEscapeOverlap();
            return Vector3.zero;
        }

        /// <summary>
        /// If the player's CapsuleCollider is currently overlapping non-trigger geometry,
        /// nudge the player out along the shortest separation direction (world-space).
        /// </summary>
        private void TryEscapeOverlap()
        {
            const float capsuleRadius = 1.4f;
            const float capsuleHeight = 3f;
            Vector3 p1 = transform.position + Vector3.up * capsuleRadius;
            Vector3 p2 = transform.position + Vector3.up * (capsuleHeight - capsuleRadius);

            Collider[] overlaps = Physics.OverlapCapsule(p1, p2, capsuleRadius, ~0, QueryTriggerInteraction.Ignore);
            if (overlaps == null || overlaps.Length == 0) return;

            Collider selfCol = GetComponent<Collider>();
            Vector3 pushSum = Vector3.zero;

            foreach (Collider other in overlaps)
            {
                if (other == null) continue;
                if (other.transform.IsChildOf(transform) || other.transform == transform) continue;
                if (selfCol == null) continue;

                if (Physics.ComputePenetration(
                        selfCol, transform.position, transform.rotation,
                        other, other.transform.position, other.transform.rotation,
                        out Vector3 dir, out float dist))
                {
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-6f) continue;
                    pushSum += dir.normalized * (dist + 0.02f);
                }
            }

            if (pushSum.sqrMagnitude > 1e-6f)
            {
                transform.position += pushSum;
            }
        }

        private void TickFishing()
        {
            if (activeFishing == null || activeFishing.IsFinished) return;
            activeFishing.Tick(Time.deltaTime);
        }

        private void HandleInteract(object sender, EventArgs e)
        {
            Debug.Log($"[PartyPlayer P{playerIndex}] E (Interact) spot={(currentFishingSpot!=null?currentFishingSpot.name:"null")} island={(currentIsland!=null?currentIsland.name:"null")} carried={CarriedFishTotal}");

            if (IsStunned) return;
            if (PartyGameManager.Instance != null && !PartyGameManager.Instance.IsGamePlaying()) return;

            // E: fish or take.
            if (currentFishingSpot != null && activeFishing == null)
            {
                StartFishing(currentFishingSpot);
                return;
            }
            if (currentIsland != null && CarriedFishTotal < RaftFishCapacity)
            {
                currentIsland.StealOne(this);
            }
        }

        private void HandleInteractAlternate(object sender, EventArgs e)
        {
            Debug.Log($"[PartyPlayer P{playerIndex}] Q (Deposit) island={(currentIsland!=null?currentIsland.name:"null")} carried={CarriedFishTotal}");
            if (IsStunned) return;
            if (PartyGameManager.Instance != null && !PartyGameManager.Instance.IsGamePlaying()) return;

            // Q: deposit one fish to any island.
            if (currentIsland != null && CarriedFishTotal > 0)
            {
                var visual = currentIsland.GetComponent<IslandFishVisual>();
                // Peek the fish we're about to deposit (common preferred) — must match Island.DepositOne order.
                FishType toDeposit = CarriedCommon > 0 ? FishType.Common : FishType.Golden;
                if (visual != null)
                {
                    Vector3 from = transform.position + Vector3.up * 0.8f;
                    visual.SpawnFlyingFish(toDeposit, from);
                }
                currentIsland.DepositOne(this);
            }
        }

        private void StartFishing(FishingSpot spot)
        {
            if (CarriedFishTotal >= RaftFishCapacity) return;
            if (spot == null || spot.IsExpired) return;

            (float duration, int amount, ItemInstance source) = ResolveFishingParams();

            float speedMul = PartyGameManager.Instance != null
                ? PartyGameManager.Instance.GetFrenzyFishingSpeedMultiplier()
                : 1f;
            float finalDuration = duration / Mathf.Max(0.01f, speedMul);

            activeFishing = new FishingAction(this, spot, finalDuration, amount, source);
            activeFishing.OnFinished += HandleFishingFinished;
            OnFishingStarted?.Invoke(this, EventArgs.Empty);
        }

        private (float duration, int amount, ItemInstance source) ResolveFishingParams()
        {
            ItemInstance netItem = FindFishingItem();
            if (netItem != null && netItem.data != null)
            {
                return (netItem.data.fishingDuration, netItem.data.fishingAmount, netItem);
            }
            float d = config != null ? config.bareHandDuration : 8f;
            int a = config != null ? config.bareHandFishAmount : 1;
            return (d, a, null);
        }

        private ItemInstance FindFishingItem()
        {
            if (itemSlots == null) return null;
            foreach (ItemInstance slot in itemSlots)
            {
                if (slot != null && !slot.IsEmpty && slot.data.category == ItemCategory.Fishing) return slot;
            }
            return null;
        }

        private void HandleFishingFinished(object sender, FishingAction.FishingResultEventArgs e)
        {
            if (e.consumedItem && activeFishing != null && activeFishing.SourceItem != null)
            {
                activeFishing.SourceItem.durability--;
                if (activeFishing.SourceItem.durability <= 0)
                {
                    ClearEmptySlots();
                }
                OnItemsChanged?.Invoke(this, EventArgs.Empty);
            }

            if (e.success && e.fishGained > 0)
            {
                AddFish(e.fishType, e.fishGained);
            }

            activeFishing = null;
            OnFishingEnded?.Invoke(this, EventArgs.Empty);
        }

        private void ClearEmptySlots()
        {
            for (int i = 0; i < itemSlots.Length; i++)
            {
                if (itemSlots[i] != null && itemSlots[i].IsEmpty) itemSlots[i] = null;
            }
        }

        public void AddFish(FishType type, int amount)
        {
            int free = Mathf.Max(0, RaftFishCapacity - CarriedFishTotal);
            int add = Mathf.Min(free, amount);
            if (add <= 0) return;
            if (type == FishType.Common) carriedCommon += add;
            else carriedGolden += add;
            OnCarriedFishChanged?.Invoke(this, EventArgs.Empty);
        }

        public (int common, int golden) DrainCarriedFish()
        {
            int c = carriedCommon;
            int g = carriedGolden;
            carriedCommon = 0;
            carriedGolden = 0;
            if (c > 0 || g > 0) OnCarriedFishChanged?.Invoke(this, EventArgs.Empty);
            return (c, g);
        }

        /// <summary>Removes and returns one fish from the raft (common first, then golden). Assumes caller checked CarriedFishTotal > 0.</summary>
        public FishType RemoveOneFishForDeposit()
        {
            FishType t;
            if (carriedCommon > 0) { carriedCommon--; t = FishType.Common; }
            else { carriedGolden--; t = FishType.Golden; }
            OnCarriedFishChanged?.Invoke(this, EventArgs.Empty);
            return t;
        }

        public bool TryEquipItem(ItemDataSO data)
        {
            if (data == null) return false;
            for (int i = 0; i < itemSlots.Length; i++)
            {
                if (itemSlots[i] == null || itemSlots[i].IsEmpty)
                {
                    itemSlots[i] = new ItemInstance(data);
                    OnItemsChanged?.Invoke(this, EventArgs.Empty);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Demo helper: overwrite the last slot with a new item (used to seed a Mine when both slots are full).</summary>
        public void ForceReplaceLastSlot(ItemDataSO data)
        {
            if (data == null || itemSlots == null || itemSlots.Length == 0) return;
            itemSlots[itemSlots.Length - 1] = new ItemInstance(data);
            OnItemsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Stun(float duration)
        {
            stunTimer = Mathf.Max(stunTimer, duration);
            if (activeFishing != null) activeFishing.Interrupt();
            OnStunned?.Invoke(this, EventArgs.Empty);
        }

        public void SetCurrentFishingSpot(FishingSpot spot) => currentFishingSpot = spot;
        public void SetCurrentIsland(Island island) => currentIsland = island;
    }
}
