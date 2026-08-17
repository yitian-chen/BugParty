using UnityEngine;

namespace PartyGame.UI
{
    /// <summary>
    /// Tiny helper: pushes the local PartyPlayer reference into the HUD components once found.
    /// Attach to the HUD Canvas root. Set localPlayerIndex to which PartyPlayer this instance is following.
    /// </summary>
    public class PartyHudBinder : MonoBehaviour
    {
        [SerializeField] private int localPlayerIndex = 0;
        [SerializeField] private PartyHudRaftFish raftFish;
        [SerializeField] private PartyHudItemSlots itemSlots;

        private bool bound;

        private void Update()
        {
            if (bound) return;
            PartyPlayer[] players = FindObjectsOfType<PartyPlayer>();
            foreach (PartyPlayer p in players)
            {
                if (p.PlayerIndex == localPlayerIndex)
                {
                    if (raftFish != null) raftFish.SetLocalPlayer(p);
                    if (itemSlots != null) itemSlots.SetLocalPlayer(p);
                    bound = true;
                    return;
                }
            }
        }
    }
}
