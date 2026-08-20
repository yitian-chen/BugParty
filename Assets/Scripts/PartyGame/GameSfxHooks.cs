using System;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Central subscriber that maps gameplay events to SFX. Attach one instance to GameScene —
    /// on Start it hooks into PartyGameManager / Island / PartyPlayer events (subscribing to
    /// FindObjectsOfType results and any players/islands that spawn later via a poll loop) and
    /// forwards to SoundManager.PlaySfx.
    ///
    /// Keeping SFX plumbing in one place means Mine / Island / PartyPlayer stay ignorant of the
    /// audio system — no scattered references to SoundManager throughout gameplay code.
    /// </summary>
    public class GameSfxHooks : MonoBehaviour
    {
        private PartyGameManager mgr;
        private readonly System.Collections.Generic.HashSet<PartyPlayer> subscribedPlayers = new System.Collections.Generic.HashSet<PartyPlayer>();
        private readonly System.Collections.Generic.HashSet<Island> subscribedIslands = new System.Collections.Generic.HashSet<Island>();
        private readonly System.Collections.Generic.Dictionary<Island, int> islandTotalsCache = new System.Collections.Generic.Dictionary<Island, int>();
        private readonly System.Collections.Generic.Dictionary<PartyPlayer, int> playerTotalsCache = new System.Collections.Generic.Dictionary<PartyPlayer, int>();

        private void Start()
        {
            TryBindManager();
            ScanAndBind();
        }

        private void Update()
        {
            if (mgr == null) TryBindManager();
            ScanAndBind();
        }

        private void TryBindManager()
        {
            var m = PartyGameManager.Instance;
            if (m == null || mgr == m) return;
            mgr = m;
            mgr.OnStateChanged += HandleStateChanged;
        }

        private void ScanAndBind()
        {
            foreach (var p in FindObjectsOfType<PartyPlayer>())
            {
                if (subscribedPlayers.Add(p))
                {
                    p.OnCarriedFishChanged += (s, e) => HandleCarriedFishChanged(p);
                    playerTotalsCache[p] = p.CarriedFishTotal;
                }
            }
            foreach (var isl in FindObjectsOfType<Island>())
            {
                if (subscribedIslands.Add(isl))
                {
                    isl.OnFishCountChanged += (s, e) => HandleIslandCountChanged(isl);
                    islandTotalsCache[isl] = isl.CommonFishCount + isl.GoldenFishCount;
                }
            }
        }

        private void HandleStateChanged(object sender, EventArgs e)
        {
            var sm = SoundManager.Instance;
            if (sm == null || sm.Library == null || mgr == null) return;
            if (mgr.CurrentState == PartyGameManager.State.CountdownToStart)
                sm.PlaySfx(sm.Library.sfxCountdown);
        }

        private void HandleCarriedFishChanged(PartyPlayer p)
        {
            if (p == null) return;
            int prev = playerTotalsCache.TryGetValue(p, out var v) ? v : 0;
            int cur = p.CarriedFishTotal;
            playerTotalsCache[p] = cur;
            if (cur <= prev) return; // only fire on increases (catch, not deposit/drop)
            var sm = SoundManager.Instance;
            if (sm != null && sm.Library != null) sm.PlaySfx(sm.Library.sfxCatch);
        }

        private void HandleIslandCountChanged(Island isl)
        {
            if (isl == null) return;
            int prev = islandTotalsCache.TryGetValue(isl, out var v) ? v : 0;
            int cur = isl.CommonFishCount + isl.GoldenFishCount;
            islandTotalsCache[isl] = cur;
            if (cur <= prev) return; // only fire on deposits (increases), not steal
            var sm = SoundManager.Instance;
            if (sm != null && sm.Library != null) sm.PlaySfx(sm.Library.sfxDeliverySuccess);
        }
    }
}
