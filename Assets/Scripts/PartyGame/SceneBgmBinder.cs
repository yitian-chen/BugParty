using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Attach to a GameObject in any scene to switch the BGM as soon as the scene loads. Which
    /// track to play is selected via `mode`. No-op if SoundManager or the library entry is missing.
    /// </summary>
    public class SceneBgmBinder : MonoBehaviour
    {
        public enum Track { None, Lobby, Battle }
        [SerializeField] private Track track = Track.Lobby;

        private void Start()
        {
            var sm = SoundManager.Instance;
            if (sm == null || sm.Library == null) return;
            AudioClip clip = null;
            switch (track)
            {
                case Track.Lobby:  clip = sm.Library.bgmLobby;  break;
                case Track.Battle: clip = sm.Library.bgmBattle; break;
            }
            sm.PlayBGM(clip);
        }
    }
}
