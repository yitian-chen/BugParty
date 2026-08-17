using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// When the owner is stunned, tilts the player's Visual root sideways (raft + character together)
    /// while preserving the yaw driven by movement. Smoothly restores upright when the stun ends.
    /// </summary>
    public class StunTilt : MonoBehaviour
    {
        [SerializeField] private PartyPlayer owner;
        [Tooltip("Root transform that gets tilted (typically the player's Visual node containing Raft + Body + FishSlots + HeldNet).")]
        [SerializeField] private Transform visualRoot;
        [Tooltip("Target roll angle (Z axis) applied while stunned, in degrees. Negative rolls to the character's left.")]
        [SerializeField] private float tiltAngle = 75f;
        [Tooltip("How many seconds to reach full tilt (or return to upright). Lower = snappier.")]
        [SerializeField] private float tiltDuration = 0.35f;

        private float currentTilt;

        private void Reset()
        {
            owner = GetComponent<PartyPlayer>();
            var v = transform.Find("Visual");
            if (v != null) visualRoot = v;
        }

        private void LateUpdate()
        {
            if (visualRoot == null) return;

            float target = (owner != null && owner.IsStunned) ? tiltAngle : 0f;
            float step = tiltDuration > 0.001f ? Mathf.Abs(tiltAngle) / tiltDuration * Time.deltaTime : 999f;
            currentTilt = Mathf.MoveTowards(currentTilt, target, step);

            // Preserve movement-driven yaw; force pitch = 0; override roll.
            Vector3 e = visualRoot.localEulerAngles;
            visualRoot.localEulerAngles = new Vector3(0f, e.y, currentTilt);
        }
    }
}
