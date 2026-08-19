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
            netSlot0.OnValueChanged += (a, b) => { RebuildLocalSlots(); OnItemsChanged?.Invoke(this, EventArgs.Empty); };
            netSlot1.OnValueChanged += (a, b) => { RebuildLocalSlots(); OnItemsChanged?.Invoke(this, EventArgs.Empty); };
            netStunTimer.OnValueChanged += (a, b) => { if (b > 0 && a <= 0) OnStunned?.Invoke(this, EventArgs.Empty); };
            netIsFishing.OnValueChanged += (a, b) =>
            {
                if (b && !a) OnFishingStarted?.Invoke(this, EventArgs.Empty);
                else if (!b && a) OnFishingEnded?.Invoke(this, EventArgs.Empty);
            };
            netWaterAmmo.OnValueChanged      += (a, b) => OnWaterGunChanged?.Invoke(this, EventArgs.Empty);
            netWaterReloading.OnValueChanged += (a, b) => OnWaterGunChanged?.Invoke(this, EventArgs.Empty);
            RebuildLocalSlots();

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
            }
            else if (isBot)
            {
                // Server drives the bot via SetAIMovement; clients see replicated transform only.
                movementInput = CanAuthor ? aiMovementInput : Vector3.zero;
            }
            else if (locked || !IsLocalController)
            {
                movementInput = Vector3.zero;
            }
            else if (netWaterReloading.Value)
            {
                // Locking movement during reload gives the reload window real cost + a clear
                // opening for opponents. Item hotkeys / mouse polling still run so the player can
                // interrupt the reload with RMB or press digit keys for their other items.
                movementInput = Vector3.zero;
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
                TickFishingServer();
            }

            HandleMovement();
        }

        private void PollItemHotkeys()
        {
            if (!useGameInput || !IsLocalController) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.digit1Key.wasPressedThisFrame) RequestUseItem(0);
            if (kb.digit2Key.wasPressedThisFrame) RequestUseItem(1);
            PollWaterGunMouse();
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
                // If we're already reloading, this second press cancels the reload.
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

            if (mouse.leftButton.wasPressedThisFrame && localFireCooldown <= 0f)
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
            Ray ray = cam.ScreenPointToRay(new Vector3(mp.x, mp.y, 0f));
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

        [ServerRpc] private void InteractServerRpc() => DoInteract_Server();
        [ServerRpc] private void DepositOneServerRpc() => DoDepositOne_Server();
        [ServerRpc] private void UseItemServerRpc(int slotIndex) => DoUseItem_Server(slotIndex);
        [ServerRpc] private void CancelFishingServerRpc() { if (activeFishing != null && !activeFishing.IsFinished) activeFishing.Cancel(); }
        [ServerRpc] private void FireWaterServerRpc(Vector3 targetWorld) => DoFireWater_Server(targetWorld);
        [ServerRpc] private void StartReloadServerRpc() => DoStartReload_Server();
        [ServerRpc] private void CancelReloadServerRpc() => DoCancelReload_Server();

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

        // ---- Water gun (server-authoritative) ----

        private void DoFireWater_Server(Vector3 targetWorld)
        {
            if (!CanAuthor) return;
            if (IsStunned) return;
            if (netWaterReloading.Value) return;
            if (netWaterAmmo.Value <= 0) return;

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
            movementInput = new Vector3(v.x, 0f, v.y);
        }

        private void HandleMovement()
        {
            // Real owners write to their own transform; bots are written on the server; clients passive.
            bool canDriveTransform = isBot ? CanAuthor : IsLocalController;
            if (!canDriveTransform) return;

            float forward = movementInput.z;
            float turn = movementInput.x;
            bool hasInput = Mathf.Abs(forward) > 0.01f || Mathf.Abs(turn) > 0.01f;

            if (hasInput && (activeFishing != null || IsFishingRemote))
            {
                if (IsSoloMode || isBot) activeFishing?.Cancel();
                else if (IsOwner) CancelFishingServerRpc();
            }

            float frenzyMul = PartyGameManager.Instance != null ? PartyGameManager.Instance.GetFrenzyMoveMultiplier() : 1f;

            if (Mathf.Abs(turn) > 0.01f)
            {
                float turnSpeed = 140f * frenzyMul;
                float deltaYaw = turn * turnSpeed * Time.deltaTime;
                // Rotate the ROOT transform so NetworkTransform replicates the yaw to other clients.
                transform.Rotate(0f, deltaYaw, 0f, Space.World);
            }

            if (Mathf.Abs(forward) > 0.01f)
            {
                Vector3 fwd = transform.forward;
                fwd.y = 0f; fwd.Normalize();
                lastMoveDir = fwd;
                float slowMul = IsSlowed && config != null ? config.waterGunSlowMultiplier : 1f;
                float speed = (config != null ? config.playerMoveSpeed : 6f) * frenzyMul * slowMul;
                float moveDistance = forward * speed * Time.deltaTime;
                Vector3 desired = fwd * Mathf.Sign(forward);
                Vector3 delta = TryMove(desired, Mathf.Abs(moveDistance)) * Mathf.Sign(forward);
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
