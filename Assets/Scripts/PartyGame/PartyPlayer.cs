using System;
using Unity.Netcode;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Party-game player controller. Server-authoritative state + local input via ServerRpc.
    ///
    /// Solo mode (no networking): behaves like a plain MonoBehaviour, everything runs locally.
    /// Networked mode: only the server mutates fish counts, item slots, stun and fishing state;
    /// the owning client sends ServerRpcs for E/Q/1/2/CancelFishing, and reads NetworkVariables
    /// for HUD + visuals.
    /// </summary>
    public class PartyPlayer : NetworkBehaviour
    {
        [SerializeField] private int playerIndex;
        [SerializeField] private PartyGameConfig config;
        [SerializeField] private bool useGameInput = true;
        [SerializeField] private Transform visualRoot;

        // Set true for server-driven AI bots. When true: no GameInput subscription,
        // no hotkey polling, no local input read; movement/actions come from PartyPlayerAI on the server.
        private bool isBot;

        private Rigidbody rb;
        private Vector3 movementInput;
        private Vector3 aiMovementInput; // written by PartyPlayerAI on the server
        private Vector3 lastMoveDir = Vector3.forward;
        // Cached kinematic state for inertia (owner-side only; transform is authoritative via CNT).
        private float currentForwardSpeed;
        private float currentTurnRate;

        // ---- Server-authoritative state (NetworkVariables) ----
        // Slot payload: kind + durability. Empty when durability<=0 (kind then meaningless).
        [Serializable]
        public struct SlotSync : INetworkSerializable, IEquatable<SlotSync>
        {
            public int kind;         // (int)ItemKind, -1 = empty
            public int durability;

            public bool IsEmpty => kind < 0 || durability <= 0;
            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter { s.SerializeValue(ref kind); s.SerializeValue(ref durability); }
            public bool Equals(SlotSync o) => kind == o.kind && durability == o.durability;
        }

        private NetworkVariable<int> netCarriedCommon = new NetworkVariable<int>(0);
        private NetworkVariable<int> netCarriedGolden = new NetworkVariable<int>(0);
        private NetworkVariable<float> netStunTimer = new NetworkVariable<float>(0f);
        private NetworkVariable<SlotSync> netSlot0 = new NetworkVariable<SlotSync>(new SlotSync{kind=-1});
        private NetworkVariable<SlotSync> netSlot1 = new NetworkVariable<SlotSync>(new SlotSync{kind=-1});
        private NetworkVariable<bool>  netIsFishing = new NetworkVariable<bool>(false);
        private NetworkVariable<float> netFishingProgress = new NetworkVariable<float>(0f);
        // Water gun: server-authoritative ammo, reload state and slow-after-hit timer.
        private NetworkVariable<int>   netWaterAmmo       = new NetworkVariable<int>(5);
        private NetworkVariable<bool>  netWaterReloading  = new NetworkVariable<bool>(false);
        private NetworkVariable<float> netWaterReloadT    = new NetworkVariable<float>(0f); // seconds remaining
        private NetworkVariable<float> netSlowTimer       = new NetworkVariable<float>(0f); // seconds remaining
        // Hook (grappling hook item): server-authoritative cooldown timer; the shot itself is
        // triggered per-fire, but consecutive fires are blocked while this timer > 0.
        private NetworkVariable<float> netHookCooldownT   = new NetworkVariable<float>(0f);
        // Currently-equipped weapon slot index (0 or 1). Digit 1/2 selects a weapon in that slot;
        // LMB routes to the weapon kind in the equipped slot. Server-authoritative but local input
        // requests a change via ServerRpc. -1 = no weapon equipped.
        private NetworkVariable<int> netEquippedSlot = new NetworkVariable<int>(-1);

        // Local mirrors of item slots (materialized ItemInstance from SlotSync + config lookup).
        private ItemInstance[] itemSlots;

        // Server-side runtime state (not synced directly — clients infer via NetworkVariables).
        private FishingAction activeFishing;
        private FishingSpot currentFishingSpot;
        private Island currentIsland;

        public event EventHandler OnFishingStarted;
        public event EventHandler OnFishingEnded;
        public event EventHandler OnCarriedFishChanged;
        public event EventHandler OnItemsChanged;
        public event EventHandler OnStunned;
        public event EventHandler OnWaterGunChanged;   // fired when ammo / reload state changes
        public event EventHandler OnWaterGunFired;     // fired on client when a shot lands (for VFX)
        public event EventHandler OnEquippedWeaponChanged; // fired when the equipped slot changes

        public int PlayerIndex => playerIndex;
        public int CarriedCommon => netCarriedCommon.Value;
        public int CarriedGolden => netCarriedGolden.Value;
        public int CarriedFishTotal => netCarriedCommon.Value + netCarriedGolden.Value;
        public int RaftFishCapacity => config != null ? config.raftFishCapacity : 2;
        public bool IsStunned => netStunTimer.Value > 0f;
        public bool IsSlowed => netSlowTimer.Value > 0f;
        public bool IsWalking => movementInput.sqrMagnitude > 0.01f;
        public FishingAction ActiveFishing => activeFishing;
        public bool IsFishingRemote => netIsFishing.Value; // for UI on non-server clients
        public float FishingProgressRemote => netFishingProgress.Value;
        public FishingSpot CurrentFishingSpot => currentFishingSpot;
        public Island CurrentIsland => currentIsland;
        public ItemInstance[] ItemSlots => itemSlots;
        public PartyGameConfig Config => config;
        // Water gun read-only state for HUD.
        public int WaterAmmo => netWaterAmmo.Value;
        public int WaterClipSize => config != null ? config.waterGunClipSize : 5;
        public bool WaterReloading => netWaterReloading.Value;
        public float WaterReloadNormalized
        {
            get
            {
                float total = config != null ? config.waterGunReloadSeconds : 4f;
                if (total <= 0f) return 0f;
                // Clamp01 handles both the initial (t=total, n=0) and the grace-window overshoot (t<0, n>1).
                return Mathf.Clamp01(1f - netWaterReloadT.Value / total);
            }
        }
        // Hook state (read by HUD / crosshair prompts).
        public float HookCooldownRemaining => netHookCooldownT.Value;
        public bool HookOnCooldown => netHookCooldownT.Value > 0f;
        public bool HasHookEquipped
        {
            get
            {
                if (itemSlots == null) return false;
                foreach (var s in itemSlots)
                    if (s != null && !s.IsEmpty && s.data != null && s.data.kind == ItemKind.Hook) return true;
                return false;
            }
        }
        // ---- Weapon selection ----
        public int EquippedSlot => netEquippedSlot.Value;
        public ItemDataSO EquippedWeaponData
        {
            get
            {
                int i = netEquippedSlot.Value;
                if (itemSlots == null || i < 0 || i >= itemSlots.Length) return null;
                var s = itemSlots[i];
                if (s == null || s.IsEmpty || s.data == null) return null;
                return s.data.category == ItemCategory.Weapon ? s.data : null;
            }
        }
        public bool IsEquippedKind(ItemKind kind)
        {
            var d = EquippedWeaponData;
            return d != null && d.kind == kind;
        }
        /// <summary>The currently-equipped item instance (any category), or null.</summary>
        public ItemInstance EquippedItem
        {
            get
            {
                int i = netEquippedSlot.Value;
                if (itemSlots == null || i < 0 || i >= itemSlots.Length) return null;
                var s = itemSlots[i];
                return (s == null || s.IsEmpty) ? null : s;
            }
        }

        private bool IsSoloMode => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
        private bool CanAuthor => IsSoloMode || IsServer;
        /// <summary>Owner client (or solo) reads local input. Bots never read local input — the server AI drives them.</summary>
        public bool IsLocalController => !isBot && (IsSoloMode || IsOwner);
        public bool IsBot => isBot;

        /// <summary>Marks this player as an AI bot. Must be called on the server before/after spawn.</summary>
        public void SetIsBot(bool value)
        {
            isBot = value;
            if (isBot && subscribed && GameInput.Instance != null)
            {
                GameInput.Instance.OnInteractAction -= HandleInteract;
                GameInput.Instance.OnInteractAlternateAction -= HandleInteractAlternate;
                subscribed = false;
            }
        }

        /// <summary>Called by PartyPlayerAI on the server every tick; a Vector3 in world-space input space (x=turn, z=forward).</summary>
        public void SetAIMovement(Vector3 xzInput)
        {
            aiMovementInput = new Vector3(xzInput.x, 0f, xzInput.z);
        }

        /// <summary>Server-side entry point for AI to trigger E (fish / steal).</summary>
        public void AI_TryInteract()
        {
            if (!isBot || !CanAuthor) return;
            if (IsStunned) return;
            if (PartyGameManager.Instance != null && !PartyGameManager.Instance.IsGamePlaying()) return;
            DoInteract_Server();
        }

        /// <summary>Server-side entry point for AI to trigger Q (deposit one).</summary>
        public void AI_TryDepositOne()
        {
            if (!isBot || !CanAuthor) return;
            if (IsStunned) return;
            if (PartyGameManager.Instance != null && !PartyGameManager.Instance.IsGamePlaying()) return;
            DoDepositOne_Server();
        }

        /// <summary>Server-side entry point for AI to cancel its fishing (e.g. to move away).</summary>
        public void AI_CancelFishing()
        {
            if (!isBot || !CanAuthor) return;
            if (activeFishing != null && !activeFishing.IsFinished) activeFishing.Cancel();
        }

        /// <summary>Server-side entry point for AI to use an item slot (mine, knife, etc.).</summary>
        public void AI_TryUseItem(int slotIndex)
        {
            if (!isBot || !CanAuthor) return;
            if (IsStunned) return;
            if (PartyGameManager.Instance != null && !PartyGameManager.Instance.IsGamePlaying()) return;
            DoUseItem_Server(slotIndex);
        }

        public Vector3 LastMoveDir => lastMoveDir;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            itemSlots = new ItemInstance[config != null ? config.itemSlotCount : 2];
        }

        public override void OnNetworkSpawn()
        {
            netCarriedCommon.OnValueChanged += (a, b) => OnCarriedFishChanged?.Invoke(this, EventArgs.Empty);
            netCarriedGolden.OnValueChanged += (a, b) => OnCarriedFishChanged?.Invoke(this, EventArgs.Empty);
            netSlot0.OnValueChanged += (a, b) => { RebuildLocalSlots(); OnItemsChanged?.Invoke(this, EventArgs.Empty); if (CanAuthor) HandleSlotChanged_Server(0); };
            netSlot1.OnValueChanged += (a, b) => { RebuildLocalSlots(); OnItemsChanged?.Invoke(this, EventArgs.Empty); if (CanAuthor) HandleSlotChanged_Server(1); };
            netStunTimer.OnValueChanged += (a, b) => { if (b > 0 && a <= 0) OnStunned?.Invoke(this, EventArgs.Empty); };
            netIsFishing.OnValueChanged += (a, b) =>
            {
                if (b && !a) OnFishingStarted?.Invoke(this, EventArgs.Empty);
                else if (!b && a) OnFishingEnded?.Invoke(this, EventArgs.Empty);
            };
            netWaterAmmo.OnValueChanged      += (a, b) => OnWaterGunChanged?.Invoke(this, EventArgs.Empty);
            netWaterReloading.OnValueChanged += (a, b) => OnWaterGunChanged?.Invoke(this, EventArgs.Empty);
            netEquippedSlot.OnValueChanged   += (a, b) => OnEquippedWeaponChanged?.Invoke(this, EventArgs.Empty);
            RebuildLocalSlots();
            // If we have a weapon in a slot and none equipped yet, auto-equip the first weapon slot
            // on the server. This is what lets the demo loadout show slot 0 (water gun) as ready.
            if (CanAuthor) AutoEquipFirstWeaponIfNone_Server();

            // Auto-attach owner-only helpers so the prefab doesn't need to carry these components.
            if (IsLocalController && !isBot)
            {
                if (GetComponent<PartyPlayerCrosshair>() == null) gameObject.AddComponent<PartyPlayerCrosshair>();
            }
            // Reload bar is world-space and visible to everyone, so every peer gets its own copy.
            if (GetComponent<WaterReloadBar>() == null) gameObject.AddComponent<WaterReloadBar>();
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
            if (!IsLocalController) return;
            if (GameInput.Instance == null) return;
            GameInput.Instance.OnInteractAction += HandleInteract;
            GameInput.Instance.OnInteractAlternateAction += HandleInteractAlternate;
            subscribed = true;
        }

        private void Update()
        {
            bool locked = PartyGameManager.Instance != null && !PartyGameManager.Instance.IsGamePlaying();

            if (IsStunned)
            {
                movementInput = Vector3.zero;
                // Kill inertia so we don't glide during / after stun.
                currentForwardSpeed = 0f;
                currentTurnRate = 0f;
            }
            else if (isBot)
            {
                // Server drives the bot via SetAIMovement; clients see replicated transform only.
                movementInput = CanAuthor ? aiMovementInput : Vector3.zero;
            }
            else if (locked || !IsLocalController)
            {
                movementInput = Vector3.zero;
                currentForwardSpeed = 0f;
                currentTurnRate = 0f;
            }
            else if (netWaterReloading.Value)
            {
                // Locking movement during reload gives the reload window real cost + a clear
                // opening for opponents. Item hotkeys / mouse polling still run so the player can
                // interrupt the reload with RMB or press digit keys for their other items.
                movementInput = Vector3.zero;
                currentForwardSpeed = 0f;
                currentTurnRate = 0f;
                PollItemHotkeys();
            }
            else
            {
                ReadMovementInput();
                PollItemHotkeys();
            }

            // Server decrements stun timer and ticks fishing action.
            if (CanAuthor)
            {
                if (netStunTimer.Value > 0f)
                {
                    float t = netStunTimer.Value - Time.deltaTime;
                    netStunTimer.Value = Mathf.Max(0f, t);
                }
                if (netSlowTimer.Value > 0f)
                {
                    netSlowTimer.Value = Mathf.Max(0f, netSlowTimer.Value - Time.deltaTime);
                }
                if (netWaterReloading.Value)
                {
                    netWaterReloadT.Value -= Time.deltaTime;
                    // Small grace window past t=0 so clients definitely sample a "near-done" tick
                    // before we flip netWaterReloading to false. Without it, network sampling jitter
                    // means clients often saw the bar stop at ~80% then vanish.
                    if (netWaterReloadT.Value <= -0.15f)
                    {
                        netWaterAmmo.Value = config != null ? config.waterGunClipSize : 5;
                        netWaterReloading.Value = false;
                        netWaterReloadT.Value = 0f;
                    }
                }
                if (netHookCooldownT.Value > 0f)
                {
                    netHookCooldownT.Value = Mathf.Max(0f, netHookCooldownT.Value - Time.deltaTime);
                }
                TickFishingServer();
            }

            HandleMovement();
        }

        private void PollItemHotkeys()
        {
            if (!useGameInput || !IsLocalController) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            // Digit 1/2: if the target slot is a Weapon, equip it; otherwise fall through to the
            // legacy "use item now" path (mines/knife etc.) so those still work if we ever put one back.
            if (kb.digit1Key.wasPressedThisFrame) HandleDigit(0);
            if (kb.digit2Key.wasPressedThisFrame) HandleDigit(1);
            PollWaterGunMouse();
        }

        private void HandleDigit(int slotIndex)
        {
            if (itemSlots != null && slotIndex >= 0 && slotIndex < itemSlots.Length)
            {
                var s = itemSlots[slotIndex];
                if (s != null && !s.IsEmpty && s.data != null && s.data.category == ItemCategory.Weapon)
                {
                    RequestEquipSlot(slotIndex);
                    return;
                }
            }
            RequestUseItem(slotIndex);
        }

        private float localFireCooldown;

        private void PollWaterGunMouse()
        {
            if (!IsLocalController) return;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return;
            if (localFireCooldown > 0f) localFireCooldown -= Time.deltaTime;

            if (mouse.rightButton.wasPressedThisFrame)
            {
                // RMB is water-gun-only: reload / cancel reload. Ignore if the water gun isn't equipped.
                if (IsEquippedKind(ItemKind.WaterGun))
                {
                    if (netWaterReloading.Value)
                    {
                        if (IsSoloMode) DoCancelReload_Server();
                        else if (IsOwner) CancelReloadServerRpc();
                    }
                    else
                    {
                        if (IsSoloMode) DoStartReload_Server();
                        else if (IsOwner) StartReloadServerRpc();
                    }
                }
            }

            if (mouse.leftButton.wasPressedThisFrame && localFireCooldown <= 0f)
            {
                var weapon = EquippedWeaponData;
                if (weapon == null)
                {
                    // No weapon equipped — remind the local player.
                    var ch = GetComponent<PartyPlayerCrosshair>();
                    if (ch != null) ch.ShowHeadBanner("请按 1 或 2 装备武器");
                    return;
                }

                if (weapon.kind == ItemKind.Hook)
                {
                    if (HookOnCooldown)
                    {
                        var ch = GetComponent<PartyPlayerCrosshair>();
                        if (ch != null) ch.ShowHeadBanner($"钩爪冷却中 {netHookCooldownT.Value:0.0}s");
                        return;
                    }
                    Vector3 hookTarget;
                    if (TryReadAimWorldPosition(out hookTarget))
                    {
                        // Small owner-side cooldown so we don't spam the RPC; server has its own
                        // (netHookCooldownT) which is the source of truth.
                        localFireCooldown = 0.15f;
                        if (IsSoloMode) DoFireHook_Server(hookTarget);
                        else if (IsOwner) FireHookServerRpc(hookTarget);
                    }
                    return;
                }

                if (weapon.kind == ItemKind.WaterGun)
                {
                    // Owner-local out-of-ammo hint. Server is the source of truth for ammo, but the
                    // client mirror is fine for showing an immediate "empty click" prompt.
                    if (netWaterAmmo.Value <= 0 && !netWaterReloading.Value)
                    {
                        var ch = GetComponent<PartyPlayerCrosshair>();
                        if (ch != null) ch.ShowHeadBanner("弹药耗尽 请装填 (右键)");
                        return;
                    }
                    Vector3 target;
                    if (TryReadAimWorldPosition(out target))
                    {
                        localFireCooldown = config != null ? config.waterGunFireCooldown : 0.25f;
                        if (IsSoloMode) DoFireWater_Server(target);
                        else if (IsOwner) FireWaterServerRpc(target);
                    }
                    return;
                }
            }
        }

        /// <summary>Owner-side helper: shoot a ray from the main camera through the mouse and intersect y=0 to get an aim point.</summary>
        public bool TryReadAimWorldPosition(out Vector3 world)
        {
            world = default;
            var cam = GameWorldCamera.Resolve();
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (cam == null || mouse == null) return false;
            Vector2 mp = mouse.position.ReadValue();
            // With the pixel camera, cam.targetTexture (RT) is 640x360 while the OS mouse coord
            // is in screen pixels. ScreenPointToRay would treat mp as RT-space and give a wildly
            // wrong world position. Convert via viewport (0..1) using the screen size instead.
            float sw = Screen.width, sh = Screen.height;
            if (sw <= 0f || sh <= 0f) return false;
            Vector2 vp = new Vector2(mp.x / sw, mp.y / sh);
            Ray ray = cam.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f));
            // Intersect with y = player-height plane (approx torso height) to keep aim visually near feet at any camera tilt.
            float planeY = transform.position.y + 0.5f;
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = (planeY - ray.origin.y) / ray.direction.y;
            if (t <= 0f) return false;
            world = ray.origin + ray.direction * t;
            return true;
        }

        // ---- Input entry points: solo path calls locally; networked owner path calls ServerRpc. ----

        private void HandleInteract(object sender, EventArgs e)
        {
            if (IsStunned) return;
            if (PartyGameManager.Instance != null && !PartyGameManager.Instance.IsGamePlaying()) return;
            if (IsSoloMode) { DoInteract_Server(); return; }
            if (IsOwner) InteractServerRpc();
        }

        private void HandleInteractAlternate(object sender, EventArgs e)
        {
            if (IsStunned) return;
            if (PartyGameManager.Instance != null && !PartyGameManager.Instance.IsGamePlaying()) return;
            if (IsSoloMode) { DoDepositOne_Server(); return; }
            if (IsOwner) DepositOneServerRpc();
        }

        private void RequestUseItem(int slotIndex)
        {
            if (IsStunned) return;
            if (PartyGameManager.Instance != null && !PartyGameManager.Instance.IsGamePlaying()) return;
            if (IsSoloMode) { DoUseItem_Server(slotIndex); return; }
            if (IsOwner) UseItemServerRpc(slotIndex);
        }

        /// <summary>Owner-side entry: try to equip the weapon in the given slot (1/2 hotkey).</summary>
        private void RequestEquipSlot(int slotIndex)
        {
            if (IsStunned) return;
            if (PartyGameManager.Instance != null && !PartyGameManager.Instance.IsGamePlaying()) return;
            if (IsSoloMode) { DoEquipSlot_Server(slotIndex); return; }
            if (IsOwner) EquipSlotServerRpc(slotIndex);
        }

        [ServerRpc] private void InteractServerRpc() => DoInteract_Server();
        [ServerRpc] private void DepositOneServerRpc() => DoDepositOne_Server();
        [ServerRpc] private void UseItemServerRpc(int slotIndex) => DoUseItem_Server(slotIndex);
        [ServerRpc] private void CancelFishingServerRpc() { if (activeFishing != null && !activeFishing.IsFinished) activeFishing.Cancel(); }
        [ServerRpc] private void FireWaterServerRpc(Vector3 targetWorld) => DoFireWater_Server(targetWorld);
        [ServerRpc] private void StartReloadServerRpc() => DoStartReload_Server();
        [ServerRpc] private void CancelReloadServerRpc() => DoCancelReload_Server();
        [ServerRpc] private void FireHookServerRpc(Vector3 targetWorld) => DoFireHook_Server(targetWorld);
        [ServerRpc] private void EquipSlotServerRpc(int slotIndex) => DoEquipSlot_Server(slotIndex);

        // ---- Server-side handlers ----

        private void DoInteract_Server()
        {
            if (!CanAuthor) return;
            if (IsStunned) return;
            // E during a reload is a no-op — the reload window is meant to lock the player out of
            // other actions. Cancel with RMB first if you want to fish/steal.
            if (netWaterReloading.Value) return;
            // E: fish or steal.
            if (currentFishingSpot != null && activeFishing == null)
            {
                StartFishing_Server(currentFishingSpot);
                return;
            }
            if (currentIsland != null && CarriedFishTotal < RaftFishCapacity)
            {
                var stolen = currentIsland.StealOne(this);
                if (stolen != null) BroadcastStolenClientRpc((int)stolen.Value);
            }
        }

        private void DoDepositOne_Server()
        {
            if (!CanAuthor) return;
            if (IsStunned) return;
            if (currentIsland == null || CarriedFishTotal <= 0) return;
            FishType toDeposit = CarriedCommon > 0 ? FishType.Common : FishType.Golden;
            var deposited = currentIsland.DepositOne(this);
            if (deposited != null)
            {
                Vector3 from = transform.position + Vector3.up * 0.8f;
                BroadcastDepositClientRpc(currentIsland.OwnerPlayerIndex, (int)toDeposit, from);
            }
        }

        private void DoUseItem_Server(int slotIndex)
        {
            if (!CanAuthor) return;
            if (IsStunned) return;
            if (itemSlots == null || slotIndex < 0 || slotIndex >= itemSlots.Length) return;
            var inst = itemSlots[slotIndex];
            if (inst == null || inst.IsEmpty) return;

            switch (inst.data.kind)
            {
                case ItemKind.Knife: UseKnife_Server(slotIndex, inst); break;
                case ItemKind.Mine:  UseMine_Server(slotIndex, inst);  break;
                default: break;
            }
        }

        private void UseKnife_Server(int slotIndex, ItemInstance inst)
        {
            float range = config != null ? config.knifeRange : 1.5f;
            PartyPlayer target = FindNearestFishingVictim(range);
            if (target == null) return;
            target.activeFishing?.Interrupt();
            ConsumeSlotDurability_Server(slotIndex);
        }

        private void UseMine_Server(int slotIndex, ItemInstance inst)
        {
            if (config == null || config.minePrefabRef == null) return;
            if (currentIsland != null) return;

            Vector3 spawnPos = transform.position + lastMoveDir.normalized * 1.2f;
            spawnPos.y = 0.1f;
            var mineGO = Instantiate(config.minePrefabRef, spawnPos, Quaternion.identity);
            var mine = mineGO.GetComponent<Mine>();
            if (mine != null) mine.Configure(this);
            var netObj = mineGO.GetComponent<NetworkObject>();
            if (!IsSoloMode && netObj != null) netObj.Spawn(true);
            ConsumeSlotDurability_Server(slotIndex);
        }

        // ---- Weapon equip (server) ----

        private void DoEquipSlot_Server(int slotIndex)
        {
            if (!CanAuthor) return;
            if (itemSlots == null || slotIndex < 0 || slotIndex >= itemSlots.Length) return;
            var s = itemSlots[slotIndex];
            if (s == null || s.IsEmpty || s.data == null) return;
            if (s.data.category != ItemCategory.Weapon) return;
            if (netEquippedSlot.Value == slotIndex) return;
            netEquippedSlot.Value = slotIndex;
        }

        private void AutoEquipFirstWeaponIfNone_Server()
        {
            if (!CanAuthor) return;
            if (itemSlots == null) return;
            int cur = netEquippedSlot.Value;
            if (cur >= 0 && cur < itemSlots.Length)
            {
                var s = itemSlots[cur];
                if (s != null && !s.IsEmpty && s.data != null && s.data.category == ItemCategory.Weapon) return;
            }
            for (int i = 0; i < itemSlots.Length; i++)
            {
                var s = itemSlots[i];
                if (s != null && !s.IsEmpty && s.data != null && s.data.category == ItemCategory.Weapon)
                {
                    netEquippedSlot.Value = i;
                    return;
                }
            }
            netEquippedSlot.Value = -1;
        }

        /// <summary>Called on the server when a slot's contents change. Keeps `netEquippedSlot` valid.</summary>
        private void HandleSlotChanged_Server(int slotIndex)
        {
            if (!CanAuthor) return;
            // If we just gained our first weapon, auto-equip it. If the equipped slot just went empty
            // (e.g. hook durability drained), fall back to any other weapon slot.
            int cur = netEquippedSlot.Value;
            if (cur == slotIndex)
            {
                var s = itemSlots != null && slotIndex < itemSlots.Length ? itemSlots[slotIndex] : null;
                bool stillWeapon = s != null && !s.IsEmpty && s.data != null && s.data.category == ItemCategory.Weapon;
                if (!stillWeapon)
                {
                    AutoEquipFirstWeaponIfNone_Server();
                    return;
                }
            }
            if (cur < 0) AutoEquipFirstWeaponIfNone_Server();
        }

        // ---- Water gun (server-authoritative) ----

        private void DoFireWater_Server(Vector3 targetWorld)
        {
            if (!CanAuthor) return;
            if (IsStunned) return;
            if (netWaterReloading.Value) return;
            if (netWaterAmmo.Value <= 0) return;
            if (!IsEquippedKind(ItemKind.WaterGun)) return;

            float range = config != null ? config.waterGunRange : 8f;
            float radius = config != null ? config.waterGunHitRadius : 0.7f;

            Vector3 origin = transform.position + Vector3.up * 0.9f;
            Vector3 aim = targetWorld; aim.y = origin.y;
            Vector3 dir = aim - origin; dir.y = 0f;
            float aimDist = dir.magnitude;
            if (aimDist < 0.01f) return;
            dir /= aimDist;

            float castLen = Mathf.Min(range, aimDist + radius);
            PartyPlayer bestVictim = null; float bestDist = float.PositiveInfinity;
            // Iterate players; server-side hit resolution is a simple capsule-vs-ray so aim assist stays generous.
            var all = FindObjectsOfType<PartyPlayer>();
            foreach (var p in all)
            {
                if (p == null || p == this) continue;
                Vector3 to = p.transform.position - origin; to.y = 0f;
                float projected = Vector3.Dot(to, dir);
                if (projected < 0f || projected > castLen) continue;
                Vector3 closest = origin + dir * projected;
                float lateral = Vector2.Distance(new Vector2(closest.x, closest.z), new Vector2(p.transform.position.x, p.transform.position.z));
                if (lateral > radius + 0.5f) continue; // 0.5 body radius allowance
                if (projected < bestDist) { bestDist = projected; bestVictim = p; }
            }

            netWaterAmmo.Value = Mathf.Max(0, netWaterAmmo.Value - 1);
            Vector3 endPoint = origin + dir * (bestVictim != null ? bestDist : castLen);
            BroadcastWaterShotClientRpc(origin, endPoint, bestVictim != null);

            if (bestVictim != null)
            {
                bestVictim.ApplyWaterHit_Server(dir);
            }
        }

        private void DoStartReload_Server()
        {
            if (!CanAuthor) return;
            if (IsStunned) return;
            if (netWaterReloading.Value) return;
            if (netWaterAmmo.Value >= (config != null ? config.waterGunClipSize : 5)) return;
            netWaterReloadT.Value = config != null ? config.waterGunReloadSeconds : 4f;
            netWaterReloading.Value = true;
        }

        private void DoCancelReload_Server()
        {
            if (!CanAuthor) return;
            if (!netWaterReloading.Value) return;
            netWaterReloading.Value = false;
            netWaterReloadT.Value = 0f;
            // Ammo stays at whatever it was — canceling loses the in-progress reload.
        }

        private void ApplyWaterHit_Server(Vector3 shotDir)
        {
            if (!CanAuthor) return;
            netSlowTimer.Value = Mathf.Max(netSlowTimer.Value, config != null ? config.waterGunSlowDuration : 1f);
            // Interrupt any ongoing fishing / stealing (Interrupt consumes item durability like a
            // knife hit — deliberate, since the victim's action is being cut short by force).
            if (activeFishing != null && !activeFishing.IsFinished)
            {
                activeFishing.Interrupt();
            }
            // Stun the victim for a short window in addition to the knockback + slow, so the shot
            // has real interrupt weight (they can't immediately shoot back). Stun uses the shared
            // netStunTimer path (same as mines/wave stuns).
            float stun = config != null ? config.waterGunStunDuration : 1f;
            if (stun > 0f) Stun(stun);
            // The victim's transform is driven by ClientNetworkTransform (client-authoritative), so
            // writing to it server-side would be silently overwritten by the owner's next update.
            // Route the knockback to the owning client via a targeted ClientRpc; the owner applies
            // the impulse to its own transform, and NT replicates it back to everyone.
            float push = config != null ? config.waterGunKnockbackDistance : 1.5f;
            Vector3 delta = new Vector3(shotDir.x, 0f, shotDir.z).normalized * push;

            if (IsSoloMode)
            {
                // Solo has no NGO transport; the server IS the owner.
                transform.position += delta;
                return;
            }

            // Send only to the victim's owner client.
            var target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };
            ApplyKnockbackClientRpc(delta, target);
        }

        [ClientRpc]
        private void ApplyKnockbackClientRpc(Vector3 delta, ClientRpcParams _ = default)
        {
            // Only the owner ever receives this (we scoped it above). Owner writes its authoritative
            // transform; ClientNetworkTransform replicates the new position to the server and peers.
            if (!IsOwner) return;
            transform.position += delta;
        }

        [ClientRpc]
        private void BroadcastWaterShotClientRpc(Vector3 from, Vector3 to, bool hit)
        {
            // Visible on every client (including the server host) — spawns a short-lived tracer at
            // the actual muzzle→impact segment the server resolved.
            WaterShotTracer.Spawn(from, to, hit);
            OnWaterGunFired?.Invoke(this, new WaterShotEventArgs { from = from, to = to, hit = hit });
        }

        public class WaterShotEventArgs : System.EventArgs
        {
            public Vector3 from;
            public Vector3 to;
            public bool hit;
        }

        private void ConsumeSlotDurability_Server(int slotIndex)
        {
            var s = ReadSlot(slotIndex);
            s.durability--;
            WriteSlot(slotIndex, s.durability <= 0 ? new SlotSync{kind=-1,durability=0} : s);
        }

        // ---- Hook / grappling item (server-authoritative) ----

        private void DoFireHook_Server(Vector3 targetWorld)
        {
            if (!CanAuthor) return;
            if (IsStunned) return;
            if (netHookCooldownT.Value > 0f) return;
            if (!IsEquippedKind(ItemKind.Hook)) return;

            int hookSlot = FindHookSlotIndex();
            if (hookSlot < 0) return;

            float range = config != null ? config.hookRange : 14f;
            float radius = config != null ? config.hookHitRadius : 0.7f;

            Vector3 origin = transform.position + Vector3.up * 0.9f;
            Vector3 aim = targetWorld; aim.y = origin.y;
            Vector3 dir = aim - origin; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;
            dir.Normalize();

            // Islands are static so we resolve them at fire time (safe). Players are resolved
            // per-frame in the sweep coroutine below, matching the rope tip's live position — so
            // a target that runs out of the ray isn't hit, and a target that runs into it IS hit.
            Island islandHit; float islandDist;
            FindHookIslandHit(origin, dir, range, radius, out islandHit, out islandDist);

            // The rope's visual extends to whatever we CAN'T rule out yet: island distance if an
            // island is in the line, otherwise max range. If the sweep resolves a player-hit early,
            // the pull fires immediately even though the rope visual continues to its endpoint;
            // this small mismatch is preferable to lagging the tracer by a whole travel time.
            float initialEndDist = islandHit != null ? islandDist : range;
            Vector3 endPoint = origin + dir * initialEndDist;
            BroadcastHookShotClientRpc(origin, endPoint, islandHit != null);

            // Cooldown + durability consumed at fire time so a shot can't be re-fired mid-flight.
            netHookCooldownT.Value = config != null ? config.hookCooldown : 4f;
            ConsumeSlotDurability_Server(hookSlot);

            float castSpeed = config != null && config.hookCastSpeed > 0.01f ? config.hookCastSpeed : 18f;
            StartCoroutine(HookSweepAndResolve_Server(origin, dir, radius, castSpeed, initialEndDist, islandHit, endPoint));
        }

        /// <summary>
        /// Advance the rope tip at castSpeed and, each frame, look for a player whose CURRENT
        /// position is within hit-radius of the tip. First-in-first-hit; if we reach the initial
        /// island target with no player hit and an island was queued, apply the island steal.
        /// Otherwise the shot misses (durability already burned).
        /// </summary>
        private System.Collections.IEnumerator HookSweepAndResolve_Server(Vector3 origin, Vector3 dir, float radius, float castSpeed, float initialEndDist, Island islandHit, Vector3 initialEndPoint)
        {
            float elapsed = 0f;
            float maxTime = initialEndDist / Mathf.Max(0.01f, castSpeed);
            while (elapsed < maxTime)
            {
                yield return null;
                elapsed += Time.deltaTime;
                if (!CanAuthor) yield break;

                float tipDist = Mathf.Min(elapsed * castSpeed, initialEndDist);
                Vector3 tip = origin + dir * tipDist;

                var all = FindObjectsOfType<PartyPlayer>();
                foreach (var p in all)
                {
                    if (p == null || p == this) continue;
                    // XZ-plane hit test around the tip's current position. Slightly generous with
                    // an added body-radius allowance so a raft grazed by the rope still counts.
                    Vector3 dp = p.transform.position - tip; dp.y = 0f;
                    float d = dp.magnitude;
                    if (d <= radius + 0.5f)
                    {
                        // Player hit — pull them to the caster's front. Cut the rope short on all
                        // clients so the visual stops extending past the actual catch point.
                        float dropDist = config != null ? config.hookPullTargetDistance : 3.5f;
                        Vector3 dropPos = transform.position + dir * dropDist;
                        dropPos.y = p.transform.position.y;
                        // Anchor the tracer at the victim's chest height, matching the shot origin plane.
                        Vector3 tipAnchor = new Vector3(p.transform.position.x, origin.y, p.transform.position.z);
                        BroadcastHookTracerCutClientRpc(tipAnchor);
                        ApplyHookPullPlayer_Server(p, dropPos);
                        yield break;
                    }
                }
            }

            // Rope reached the initial endpoint without catching a player.
            if (islandHit == null) yield break;

            var reservedFish = islandHit.ReserveSteal(this);
            if (reservedFish == null) yield break;

            Vector3 fromWorld = initialEndPoint;
            Vector3 toWorld = transform.position + Vector3.up * 0.5f;
            float dist = Vector3.Distance(fromWorld, toWorld);
            float flyTime = Mathf.Max(0.05f, dist / castSpeed);
            BroadcastHookFishFlyClientRpc(fromWorld, toWorld, (int)reservedFish.Value, flyTime);
            yield return new WaitForSeconds(flyTime);
            if (!CanAuthor) yield break;
            AddFish_Server(reservedFish.Value, 1);
            BroadcastStolenClientRpc((int)reservedFish.Value);
        }

        private int FindHookSlotIndex()
        {
            if (itemSlots == null) return -1;
            for (int i = 0; i < itemSlots.Length; i++)
            {
                var s = itemSlots[i];
                if (s != null && !s.IsEmpty && s.data != null && s.data.kind == ItemKind.Hook) return i;
            }
            return -1;
        }

        private void FindHookIslandHit(Vector3 origin, Vector3 dir, float range, float radius, out Island island, out float distance)
        {
            island = null; distance = float.PositiveInfinity;
            var mgr = PartyGameManager.Instance;
            if (mgr == null) return;
            foreach (var isl in mgr.Islands)
            {
                if (isl == null) continue;
                if (isl.CommonFishCount + isl.GoldenFishCount <= 0) continue;
                Vector3 to = isl.transform.position - origin; to.y = 0f;
                float projected = Vector3.Dot(to, dir);
                if (projected < 0f || projected > range) continue;
                Vector3 closest = origin + dir * projected;
                // Islands are much larger than players — accept anything within ~2m of the ray plus
                // the trigger radius, so aiming near the platform is enough.
                float lateral = Vector2.Distance(new Vector2(closest.x, closest.z), new Vector2(isl.transform.position.x, isl.transform.position.z));
                if (lateral > radius + 2.5f) continue;
                if (projected < distance) { distance = projected; island = isl; }
            }
        }

        private void ApplyHookPullPlayer_Server(PartyPlayer victim, Vector3 dropPos)
        {
            if (victim == null) return;
            // Interrupt whatever they were doing (fishing / stealing) — being yanked cancels the action.
            if (victim.activeFishing != null && !victim.activeFishing.IsFinished)
                victim.activeFishing.Interrupt();

            // Compute the pull duration now so we can schedule the follow-up stun to land exactly
            // when the visual arrives. Kept in sync with PullLerpCoroutine's own formula.
            float castSpeed = config != null && config.hookCastSpeed > 0.01f ? config.hookCastSpeed : 18f;
            float pullDist = Vector3.Distance(victim.transform.position, dropPos);
            float pullDuration = Mathf.Max(0.05f, pullDist / castSpeed);

            if (IsSoloMode)
            {
                if (victim.activePullCoroutine != null) victim.StopCoroutine(victim.activePullCoroutine);
                victim.activePullCoroutine = victim.StartCoroutine(victim.PullLerpCoroutine(dropPos));
            }
            else
            {
                // ClientNetworkTransform is client-authoritative, so the owner must move themselves. Route
                // via targeted ClientRpc to the victim's owner client (same pattern as water-gun knockback).
                var target = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { victim.OwnerClientId } }
                };
                victim.PullToPositionClientRpc(dropPos, target);
            }

            // Server-authoritative stun scheduled to fire when the pull lands. Stun uses the shared
            // netStunTimer path (same as mines / water-gun) so all clients see 眩晕 label + input lock.
            float stunDur = config != null ? config.hookVictimStunDuration : 2f;
            if (stunDur > 0f) StartCoroutine(StunVictimAfterPull_Server(victim, pullDuration, stunDur));
        }

        private System.Collections.IEnumerator StunVictimAfterPull_Server(PartyPlayer victim, float delay, float stunDur)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (!CanAuthor || victim == null) yield break;
            victim.Stun(stunDur);
        }

        [ClientRpc]
        internal void PullToPositionClientRpc(Vector3 pos, ClientRpcParams _ = default)
        {
            // Only the victim's owner receives this (scoped above). Owner writes its transform;
            // ClientNetworkTransform replicates the new pose to server + peers.
            if (!IsOwner) return;
            if (activePullCoroutine != null) StopCoroutine(activePullCoroutine);
            activePullCoroutine = StartCoroutine(PullLerpCoroutine(pos));
        }

        private Coroutine activePullCoroutine;

        private System.Collections.IEnumerator PullLerpCoroutine(Vector3 target)
        {
            Vector3 start = transform.position;
            target.y = start.y;
            // Pull duration mirrors the hook's cast time so the reel-in is visually symmetric with the rope-out.
            float castSpeed = config != null && config.hookCastSpeed > 0.01f ? config.hookCastSpeed : 18f;
            float dist = Vector3.Distance(start, target);
            float duration = Mathf.Max(0.05f, dist / castSpeed);
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                // Ease-out so the yank feels sharp at first then settles.
                float eased = 1f - (1f - k) * (1f - k);
                transform.position = Vector3.LerpUnclamped(start, target, eased);
                yield return null;
            }
            transform.position = target;
            activePullCoroutine = null;
        }

        [ClientRpc]
        private void BroadcastHookShotClientRpc(Vector3 from, Vector3 to, bool hit)
        {
            activeHookTracer = HookShotTracer.Spawn(from, to, hit);
        }

        [ClientRpc]
        private void BroadcastHookTracerCutClientRpc(Vector3 tip)
        {
            // Freeze the current tracer's tip at the actual catch position. If for any reason we
            // missed the spawn (RPC ordering / late-join), silently ignore.
            if (activeHookTracer != null) activeHookTracer.CutShort(tip);
        }

        // Per-caster reference to the most recent tracer visual, so the server can cut it short
        // mid-flight when the sweep resolves a player-hit before the tracer's initial endpoint.
        private HookShotTracer activeHookTracer;

        [ClientRpc]
        private void BroadcastHookFishFlyClientRpc(Vector3 fromWorld, Vector3 toWorld, int fishType, float duration)
        {
            HookFishFlyVisual.Spawn(fromWorld, toWorld, (FishType)fishType, duration);
        }


        private PartyPlayer FindNearestFishingVictim(float range)
        {
            PartyPlayer best = null;
            float bestDist = range;
            var all = FindObjectsOfType<PartyPlayer>();
            foreach (var p in all)
            {
                if (p == this) continue;
                if (p.activeFishing == null || p.activeFishing.IsFinished) continue;
                float d = Vector3.Distance(transform.position, p.transform.position);
                if (d <= bestDist) { best = p; bestDist = d; }
            }
            return best;
        }

        // ---- Fishing action (server holds the object, syncs progress) ----

        private void StartFishing_Server(FishingSpot spot)
        {
            if (CarriedFishTotal >= RaftFishCapacity) return;
            if (spot == null || spot.IsExpired) return;
            (float duration, int amount, ItemInstance source) = ResolveFishingParams();
            float speedMul = PartyGameManager.Instance != null
                ? PartyGameManager.Instance.GetFrenzyFishingSpeedMultiplier() : 1f;
            float finalDuration = duration / Mathf.Max(0.01f, speedMul);

            activeFishing = new FishingAction(this, spot, finalDuration, amount, source);
            activeFishing.OnFinished += HandleFishingFinished_Server;
            netIsFishing.Value = true;
            netFishingProgress.Value = 0f;
        }

        private void TickFishingServer()
        {
            if (activeFishing == null) return;
            if (activeFishing.IsFinished) { activeFishing = null; return; }
            activeFishing.Tick(Time.deltaTime);
            netFishingProgress.Value = activeFishing.ProgressNormalized;
        }

        private (float duration, int amount, ItemInstance source) ResolveFishingParams()
        {
            ItemInstance netItem = FindFishingItem();
            if (netItem != null && netItem.data != null)
                return (netItem.data.fishingDuration, netItem.data.fishingAmount, netItem);
            float d = config != null ? config.bareHandDuration : 8f;
            int a = config != null ? config.bareHandFishAmount : 1;
            return (d, a, null);
        }

        private ItemInstance FindFishingItem()
        {
            if (itemSlots == null) return null;
            foreach (var s in itemSlots)
                if (s != null && !s.IsEmpty && s.data.category == ItemCategory.Fishing) return s;
            return null;
        }

        private void HandleFishingFinished_Server(object sender, FishingAction.FishingResultEventArgs e)
        {
            if (e.consumedItem && activeFishing != null && activeFishing.SourceItem != null)
            {
                int idx = FindFishingItemSlotIndex();
                if (idx >= 0) ConsumeSlotDurability_Server(idx);
            }
            if (e.success && e.fishGained > 0) AddFish_Server(e.fishType, e.fishGained);

            activeFishing = null;
            netIsFishing.Value = false;
            netFishingProgress.Value = 0f;
        }

        private int FindFishingItemSlotIndex()
        {
            for (int i = 0; i < itemSlots.Length; i++)
                if (itemSlots[i] != null && !itemSlots[i].IsEmpty && itemSlots[i].data.category == ItemCategory.Fishing) return i;
            return -1;
        }

        // ---- Movement (owner authoritative, NetworkTransform replicates position) ----

        private void ReadMovementInput()
        {
            if (!useGameInput || GameInput.Instance == null) { movementInput = Vector3.zero; return; }
            Vector2 v = GameInput.Instance.GetMovementVectorNormalized();
            // Disable reverse (S key): forward-only raft. Clamp any negative forward component.
            float forward = Mathf.Max(0f, v.y);
            movementInput = new Vector3(v.x, 0f, forward);
        }

        private void HandleMovement()
        {
            // Real owners write to their own transform; bots are written on the server; clients passive.
            bool canDriveTransform = isBot ? CanAuthor : IsLocalController;
            if (!canDriveTransform)
            {
                // Passive peers still need the cached state reset so on-ownership-change we don't drift.
                currentForwardSpeed = 0f;
                currentTurnRate = 0f;
                return;
            }

            float forwardInput = movementInput.z;
            float turnInput = movementInput.x;
            bool hasInput = Mathf.Abs(forwardInput) > 0.01f || Mathf.Abs(turnInput) > 0.01f;

            if (hasInput && (activeFishing != null || IsFishingRemote))
            {
                if (IsSoloMode || isBot) activeFishing?.Cancel();
                else if (IsOwner) CancelFishingServerRpc();
            }

            float frenzyMul = PartyGameManager.Instance != null ? PartyGameManager.Instance.GetFrenzyMoveMultiplier() : 1f;
            float slowMul = IsSlowed && config != null ? config.waterGunSlowMultiplier : 1f;
            float maxSpeed = (config != null ? config.playerMoveSpeed : 6f) * frenzyMul * slowMul;
            float accel = (config != null ? config.playerAccel : 12f) * frenzyMul;
            float decel = (config != null ? config.playerDecel : 6f) * frenzyMul;
            float maxTurnRate = 140f * frenzyMul;
            float turnAccel = (config != null ? config.playerTurnAccel : 360f) * frenzyMul;

            // Turn inertia: yaw rate accelerates toward the target rate, decelerates back to 0 when no input.
            float targetTurnRate = turnInput * maxTurnRate;
            float turnDelta = (Mathf.Abs(turnInput) > 0.01f ? turnAccel : turnAccel * 1.5f) * Time.deltaTime;
            currentTurnRate = Mathf.MoveTowards(currentTurnRate, targetTurnRate, turnDelta);
            if (Mathf.Abs(currentTurnRate) > 0.001f)
            {
                float deltaYaw = currentTurnRate * Time.deltaTime;
                transform.Rotate(0f, deltaYaw, 0f, Space.World);
            }

            // Forward inertia: current speed accelerates toward target while input is held, drifts back
            // toward 0 (using `decel`) when input is released. Using decel < accel makes coasting obvious.
            float targetSpeed = forwardInput * maxSpeed;
            float speedDelta = (Mathf.Abs(forwardInput) > 0.01f ? accel : decel) * Time.deltaTime;
            currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, targetSpeed, speedDelta);
            // Clamp so slow / frenzy multipliers changing mid-glide don't leave residual speed above cap.
            currentForwardSpeed = Mathf.Clamp(currentForwardSpeed, -maxSpeed, maxSpeed);

            if (Mathf.Abs(currentForwardSpeed) > 0.001f)
            {
                Vector3 fwd = transform.forward;
                fwd.y = 0f; fwd.Normalize();
                if (currentForwardSpeed > 0f) lastMoveDir = fwd;
                float moveDistance = currentForwardSpeed * Time.deltaTime;
                Vector3 desired = fwd * Mathf.Sign(currentForwardSpeed);
                Vector3 delta = TryMove(desired, Mathf.Abs(moveDistance)) * Mathf.Sign(currentForwardSpeed);
                // NOTE: intentionally do NOT clamp currentForwardSpeed to 0 when delta is zero — with
                // tiny per-frame moveDistances (accel-ramp startup) CapsuleCast frequently reports an
                // initial-overlap hit and returns 0, which would strand the raft at rest. Old
                // behavior (before inertia) was to keep re-issuing input every frame; keeping the
                // accumulated speed nonzero preserves that behavior so TryEscapeOverlap can gradually
                // push us free.
                transform.position += delta;
            }
        }

        private Vector3 TryMove(Vector3 dir, float distance)
        {
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
            TryEscapeOverlap();
            return Vector3.zero;
        }

        private void TryEscapeOverlap()
        {
            const float capsuleRadius = 1.4f;
            const float capsuleHeight = 3f;
            Vector3 p1 = transform.position + Vector3.up * capsuleRadius;
            Vector3 p2 = transform.position + Vector3.up * (capsuleHeight - capsuleRadius);
            Collider[] overlaps = Physics.OverlapCapsule(p1, p2, capsuleRadius, ~0, QueryTriggerInteraction.Ignore);
            if (overlaps == null || overlaps.Length == 0) return;
            Collider selfCol = GetComponent<Collider>();
            if (selfCol == null) return;
            Vector3 pushSum = Vector3.zero;
            foreach (var other in overlaps)
            {
                if (other == null) continue;
                if (other.transform.IsChildOf(transform) || other.transform == transform) continue;
                if (Physics.ComputePenetration(selfCol, transform.position, transform.rotation,
                    other, other.transform.position, other.transform.rotation, out var dir, out float dist))
                {
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-6f) continue;
                    pushSum += dir.normalized * (dist + 0.02f);
                }
            }
            if (pushSum.sqrMagnitude > 1e-6f) transform.position += pushSum;
        }

        // ---- Server-side inventory + fish mutations (single truth) ----

        public void AddFish(FishType type, int amount) => AddFish_Server(type, amount);
        private void AddFish_Server(FishType type, int amount)
        {
            if (!CanAuthor) return;
            int free = Mathf.Max(0, RaftFishCapacity - CarriedFishTotal);
            int add = Mathf.Min(free, amount);
            if (add <= 0) return;
            if (type == FishType.Common) netCarriedCommon.Value += add;
            else netCarriedGolden.Value += add;
        }

        public (int common, int golden) DrainCarriedFish()
        {
            if (!CanAuthor) return (0, 0);
            int c = netCarriedCommon.Value;
            int g = netCarriedGolden.Value;
            netCarriedCommon.Value = 0;
            netCarriedGolden.Value = 0;
            return (c, g);
        }

        public FishType RemoveOneFishForDeposit()
        {
            if (!CanAuthor) return FishType.Common;
            if (netCarriedCommon.Value > 0) { netCarriedCommon.Value--; return FishType.Common; }
            netCarriedGolden.Value--;
            return FishType.Golden;
        }

        public bool TryEquipItem(ItemDataSO data)
        {
            if (!CanAuthor) return false;
            if (data == null) return false;
            for (int i = 0; i < 2; i++)
            {
                var s = ReadSlot(i);
                if (s.IsEmpty) { WriteSlot(i, new SlotSync{kind=(int)data.kind, durability=data.startingDurability}); return true; }
            }
            return false;
        }

        public void ForceReplaceLastSlot(ItemDataSO data)
        {
            if (!CanAuthor) return;
            if (data == null) return;
            WriteSlot(1, new SlotSync{kind=(int)data.kind, durability=data.startingDurability});
        }

        public void Stun(float duration)
        {
            if (!CanAuthor) return;
            netStunTimer.Value = Mathf.Max(netStunTimer.Value, duration);
            if (activeFishing != null) activeFishing.Interrupt();
            OnStunned?.Invoke(this, EventArgs.Empty);
        }

        // ---- Slot helpers ----
        private SlotSync ReadSlot(int i) => i == 0 ? netSlot0.Value : netSlot1.Value;
        private void WriteSlot(int i, SlotSync s) { if (i == 0) netSlot0.Value = s; else netSlot1.Value = s; }
        private void RebuildLocalSlots()
        {
            if (itemSlots == null) itemSlots = new ItemInstance[2];
            for (int i = 0; i < 2; i++)
            {
                var s = ReadSlot(i);
                if (s.IsEmpty) { itemSlots[i] = null; continue; }
                ItemDataSO data = config != null ? config.GetItemByKind((ItemKind)s.kind) : null;
                if (data == null) { itemSlots[i] = null; continue; }
                itemSlots[i] = new ItemInstance(data) { durability = s.durability };
            }
        }

        // ---- Client RPCs for cosmetic feedback (deposit fly, stolen VFX) ----
        [ClientRpc] private void BroadcastDepositClientRpc(int islandOwnerIndex, int fishType, Vector3 from)
        {
            var island = PartyGameManager.Instance != null ? PartyGameManager.Instance.GetIslandOfPlayer(islandOwnerIndex) : null;
            if (island == null) return;
            var visual = island.GetComponent<IslandFishVisual>();
            if (visual != null) visual.SpawnFlyingFish((FishType)fishType, from);
        }

        [ClientRpc] private void BroadcastStolenClientRpc(int fishType) { /* future: play VFX / SFX */ }

        public void SetCurrentFishingSpot(FishingSpot spot) => currentFishingSpot = spot;
        public void SetCurrentIsland(Island island) => currentIsland = island;
    }
}
