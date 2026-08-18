using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Bridges PartyPlayer state → Animator parameters on the character model.
    /// Attach to the PartyPlayer root; assign the child Animator on the humanoid model.
    ///
    /// Animator contract (see AnimationController_PartyPlayer):
    ///   Bool IsMoving   — set every frame from PartyPlayer.IsWalking (or its remote analog)
    ///   Trigger Fish    — fired on fishing start (server- or remote-driven)
    ///   Trigger Unload  — fired on deposit (OnCarriedFishChanged when carried count drops)
    ///   Trigger Hurt    — fired on OnStunned
    ///   Trigger Win     — fired via SetWin() from result screen
    /// </summary>
    [RequireComponent(typeof(PartyPlayer))]
    public class PartyPlayerAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;

        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
        private static readonly int FishHash     = Animator.StringToHash("Fish");
        private static readonly int UnloadHash   = Animator.StringToHash("Unload");
        private static readonly int HurtHash     = Animator.StringToHash("Hurt");
        private static readonly int WinHash      = Animator.StringToHash("Win");

        private PartyPlayer player;
        private int lastCarried;

        private void Awake()
        {
            player = GetComponent<PartyPlayer>();
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
        }

        private void OnEnable()
        {
            if (player == null) return;
            player.OnFishingStarted     += HandleFishing;
            player.OnCarriedFishChanged += HandleCarriedChanged;
            player.OnStunned            += HandleStunned;
            lastCarried = player.CarriedFishTotal;
        }

        private void OnDisable()
        {
            if (player == null) return;
            player.OnFishingStarted     -= HandleFishing;
            player.OnCarriedFishChanged -= HandleCarriedChanged;
            player.OnStunned            -= HandleStunned;
        }

        private void Update()
        {
            if (animator == null || player == null) return;
            // IsWalking already reflects the owner's local input; remote peers see idle because
            // input isn't replicated. That's fine — NetworkTransform still moves them and the
            // stationary idle looks acceptable for a party prototype. To sync properly later,
            // expose a NetworkVariable<bool> for IsMoving in PartyPlayer.
            animator.SetBool(IsMovingHash, player.IsWalking && !player.IsStunned);
        }

        private void HandleFishing(object sender, System.EventArgs e)
        {
            if (animator == null) return;
            animator.ResetTrigger(HurtHash);
            animator.SetTrigger(FishHash);
        }

        private void HandleStunned(object sender, System.EventArgs e)
        {
            if (animator == null) return;
            animator.SetTrigger(HurtHash);
        }

        private void HandleCarriedChanged(object sender, System.EventArgs e)
        {
            if (animator == null || player == null) return;
            int now = player.CarriedFishTotal;
            // Only trigger unload when count strictly decreases (deposit / drain), not on pickup.
            if (now < lastCarried) animator.SetTrigger(UnloadHash);
            lastCarried = now;
        }

        public void SetWin() { if (animator != null) animator.SetTrigger(WinHash); }
    }
}
