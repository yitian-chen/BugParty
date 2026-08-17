using TMPro;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Shows a floating "眩晕" text over the player while they are stunned.
    /// </summary>
    public class StunLabel : MonoBehaviour
    {
        [SerializeField] private PartyPlayer owner;
        [SerializeField] private Transform followTarget;
        [SerializeField] private TextMeshPro label;
        [SerializeField] private Vector3 offset = new Vector3(0, 6f, 0);

        private void Awake()
        {
            if (label == null) label = GetComponentInChildren<TextMeshPro>();
            SetVisible(false);
        }

        private void LateUpdate()
        {
            if (followTarget != null) transform.position = followTarget.position + offset;

            bool show = owner != null && owner.IsStunned;
            SetVisible(show);

            Camera cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }

        private void SetVisible(bool v)
        {
            if (label != null && label.gameObject.activeSelf != v) label.gameObject.SetActive(v);
        }
    }
}
