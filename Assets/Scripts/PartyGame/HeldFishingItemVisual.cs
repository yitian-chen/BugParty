using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Shows a held-item mesh (e.g. the net) on the player during an active fishing action.
    /// Hidden otherwise.
    /// </summary>
    public class HeldFishingItemVisual : MonoBehaviour
    {
        [SerializeField] private PartyPlayer owner;
        [SerializeField] private GameObject heldRoot;

        private void Awake()
        {
            SetVisible(false);
        }

        private void LateUpdate()
        {
            if (owner == null) { SetVisible(false); return; }
            bool show = (owner.ActiveFishing != null && !owner.ActiveFishing.IsFinished) || owner.IsFishingRemote;
            SetVisible(show);
        }

        private void SetVisible(bool v)
        {
            if (heldRoot != null && heldRoot.activeSelf != v) heldRoot.SetActive(v);
        }
    }
}
