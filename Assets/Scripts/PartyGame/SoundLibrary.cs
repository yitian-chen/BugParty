using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// One-stop registry of all audio clips used by the party game. Populate in the inspector on
    /// a ScriptableObject asset; SoundManager reads from it at runtime. Keeps clip references off
    /// of hot classes so scene-loaded scripts don't drag audio into memory on menu screens.
    /// </summary>
    [CreateAssetMenu(fileName = "SoundLibrary", menuName = "PartyGame/SoundLibrary")]
    public class SoundLibrary : ScriptableObject
    {
        [Header("BGM")]
        public AudioClip bgmLobby;
        public AudioClip bgmBattle;

        [Header("SFX")]
        public AudioClip sfxUiClick;
        public AudioClip sfxCountdown;
        public AudioClip sfxCatch;           // fishing progress finished
        public AudioClip sfxDeliverySuccess; // fish deposited on island
        public AudioClip sfxDeliveryFail;    // island reject / raft full
        public AudioClip sfxExplode;         // mine boom
        public AudioClip sfxKnife;           // water gun / knife hit
        public AudioClip sfxRamHit;          // booster ram / elbow-hit
        public AudioClip sfxDrop;            // fish drop from stunned player
        public AudioClip sfxPickup;          // fish picked up
    }
}
