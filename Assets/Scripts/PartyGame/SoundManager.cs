using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Persistent audio manager. Lives across scene loads (DontDestroyOnLoad) — attach a
    /// prefab / bootstrap GameObject to the FIRST scene (LanMenuScene) with a `library` reference
    /// to the SoundLibrary SO, and every other scene can access it via SoundManager.Instance.
    ///
    /// Two AudioSources: one dedicated to BGM (loops, smooth cross-swap), one shared pool for SFX
    /// (PlayOneShot). No spatial audio for SFX yet — party game is top-down and 2D-ish.
    /// </summary>
    public class SoundManager : MonoBehaviour
    {
        public static SoundManager Instance { get; private set; }

        [SerializeField] private SoundLibrary library;
        [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.85f;
        [Range(0f, 1f)] [SerializeField] private float sfxVolume = 0.9f;

        private AudioSource musicSrc;
        private AudioSource sfxSrc;
        private AudioClip currentBgm;

        public SoundLibrary Library => library;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            musicSrc = gameObject.AddComponent<AudioSource>();
            musicSrc.loop = true;
            musicSrc.playOnAwake = false;
            musicSrc.volume = musicVolume;
            musicSrc.spatialBlend = 0f;

            sfxSrc = gameObject.AddComponent<AudioSource>();
            sfxSrc.loop = false;
            sfxSrc.playOnAwake = false;
            sfxSrc.volume = sfxVolume;
            sfxSrc.spatialBlend = 0f;
        }

        /// <summary>
        /// Start playing `clip` on loop. Skips reassign if the requested clip is already playing —
        /// prevents scene-load bindings from resetting the track when re-entering the same scene.
        /// Pass null to stop BGM entirely.
        /// </summary>
        public void PlayBGM(AudioClip clip)
        {
            if (musicSrc == null) return;
            if (clip == null)
            {
                musicSrc.Stop();
                musicSrc.clip = null;
                currentBgm = null;
                return;
            }
            if (currentBgm == clip && musicSrc.isPlaying) return;
            currentBgm = clip;
            musicSrc.clip = clip;
            musicSrc.volume = musicVolume;
            musicSrc.Play();
        }

        public void PlaySfx(AudioClip clip, float volumeMul = 1f)
        {
            if (sfxSrc == null || clip == null) return;
            sfxSrc.PlayOneShot(clip, sfxVolume * volumeMul);
        }

        /// <summary>Positional one-shot; falls back to non-positional if we don't have 3D audio.</summary>
        public void PlaySfxAt(AudioClip clip, Vector3 worldPos, float volumeMul = 1f)
        {
            // The pooled sfxSrc is 2D; for now we ignore worldPos and play flat. Kept as a stable
            // callsite so we can upgrade later without touching every event site.
            PlaySfx(clip, volumeMul);
        }

        public void SetMusicVolume(float v) { musicVolume = Mathf.Clamp01(v); if (musicSrc != null) musicSrc.volume = musicVolume; }
        public void SetSfxVolume(float v)   { sfxVolume   = Mathf.Clamp01(v); if (sfxSrc != null) sfxSrc.volume = sfxVolume; }
    }
}
