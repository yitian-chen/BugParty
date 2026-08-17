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
            subscribed = true;
            Debug.Log($"[PartyPlayer P{playerIndex}] Subscribed to GameInput.OnInteractAction.");
        }

        private void Update()
        {
            if (stunTimer > 0f)
            {
                stunTimer -= Time.deltaTime;
                movementInput = Vector3.zero;
            }
            else
            {
                ReadMovementInput();
            }

            TickFishing();
            HandleMovement();
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
            Debug.Log($"[PartyPlayer P{playerIndex}] HandleInteract fired. spot={(currentFishingSpot!=null?currentFishingSpot.name:"null")} island={(currentIsland!=null?currentIsland.name:"null")} stunned={IsStunned} activeFishing={(activeFishing!=null)} state={(PartyGameManager.Instance!=null?PartyGameManager.Instance.CurrentState.ToString():"nogm")}");

            if (IsStunned) return;
            if (PartyGameManager.Instance != null && !PartyGameManager.Instance.IsGamePlaying()) return;

            // Priority: fishing spot > island (deposit or steal).
            if (currentFishingSpot != null && activeFishing == null)
            {
                StartFishing(currentFishingSpot);
                return;
            }

            if (currentIsland != null)
            {
                // Any island accepts deposit; any island can be stolen from.
                // Priority: if the raft has fish AND the island still has space (unlimited anyway) → deposit.
                //          else if the raft has room AND the island has fish → steal.
                if (CarriedFishTotal > 0)
                {
                    int cCommon = carriedCommon;
                    int cGolden = carriedGolden;
                    var visual = currentIsland.GetComponent<IslandFishVisual>();
                    if (visual != null)
                    {
                        Vector3 from = transform.position + Vector3.up * 0.8f;
                        for (int i = 0; i < cCommon; i++) visual.SpawnFlyingFish(FishType.Common, from);
                        for (int i = 0; i < cGolden; i++) visual.SpawnFlyingFish(FishType.Golden, from);
                    }
                    currentIsland.DepositAll(this);
                }
                else if (CarriedFishTotal < RaftFishCapacity)
                {
                    currentIsland.StealOne(this);
                }
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
