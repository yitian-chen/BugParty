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
            // TODO(阶段 D3-D5): implement Mine spawning here. Kept as stub so the switch compiles.
            // Placeholder: consume durability so the slot still updates in HUD.
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
            if (movementInput.sqrMagnitude > 0.01f)
            {
                if (activeFishing != null)
                {
                    activeFishing.Cancel();
                }

                lastMoveDir = movementInput;
                float speed = (config != null ? config.playerMoveSpeed : 6f)
                              * (PartyGameManager.Instance != null ? PartyGameManager.Instance.GetFrenzyMoveMultiplier() : 1f);
                Vector3 delta = movementInput * speed * Time.deltaTime;
                if (rb != null)
                {
                    rb.MovePosition(rb.position + delta);
                }
                else
                {
                    transform.position += delta;
                }
            }

            if (visualRoot != null && movementInput.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(movementInput);
                visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, targetRot, Time.deltaTime * 10f);
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
