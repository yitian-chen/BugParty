using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 音效与特效总线。挂在场景里任意一个空物体上即可，只需一个实例。
    ///
    /// 【设计意图】
    /// 全部通过订阅 RoomEvents 工作，玩法脚本零改动。
    /// 所有槽位都可留空——留空就是不播，不会报错。
    /// 想加新音效不用改玩法代码，只要 RoomEvents 里有对应事件。
    ///
    /// 【用法】
    /// 1. 场景里新建空物体，挂上本组件
    /// 2. 把音频与粒子 Prefab 拖进对应槽位
    /// 3. 完成，不需要任何其他接线
    /// </summary>
    [DisallowMultipleComponent]
    public class RoomAudioVfx : MonoBehaviour
    {
        [Header("═══ 音效 ═══")]
        [Tooltip("留空会自动创建一个 AudioSource")]
        public AudioSource sfxSource;

        [Space(4)]
        public AudioClip sfxSearchStart;
        public AudioClip sfxSearchInterrupt;
        public AudioClip sfxItemCollected;
        [Tooltip("搜到稀有道具时优先播这个")]
        public AudioClip sfxItemRare;
        public AudioClip sfxElbowHit;
        public AudioClip sfxItemKnockedOut;
        public AudioClip sfxJump;
        public AudioClip sfxLand;
        [Tooltip("从高处落下才播，阈值见 heavyLandHeight")]
        public AudioClip sfxLandHeavy;

        [Space(4)]
        [Tooltip("地板开裂预警。这是玩家躲开塌陷的关键听觉线索，建议一定要有")]
        public AudioClip sfxTileCracking;
        public AudioClip sfxTileCollapsed;
        public AudioClip sfxPitfall;
        public AudioClip sfxFinalCollapse;

        [Header("═══ 循环音 ═══")]
        [Tooltip("警报循环音。会随倒计时推进自动升高音调与音量")]
        public AudioSource alarmLoop;
        [Tooltip("警报音调范围：搜索刚开始 → 即将结束")]
        public Vector2 alarmPitchRange = new Vector2(0.85f, 1.35f);
        public Vector2 alarmVolumeRange = new Vector2(0.25f, 0.85f);

        [Header("═══ 粒子特效 Prefab ═══")]
        [Tooltip("肘击命中的撞击星星")]
        public GameObject vfxElbowImpact;
        public GameObject vfxItemPickup;
        [Tooltip("地板开裂的碎屑与红光")]
        public GameObject vfxTileCracking;
        [Tooltip("地板塌陷的坠落尘土")]
        public GameObject vfxTileCollapse;
        public GameObject vfxLandDust;

        [Header("═══ 参数 ═══")]
        [Tooltip("落地高度超过这个值才算重落地")]
        public float heavyLandHeight = 1.2f;

        [Tooltip("特效自动销毁时间")]
        public float vfxLifetime = 2.5f;

        [Range(0f, 1f)] public float sfxVolume = 0.9f;

        float _searchTotal = 1f;
        float _searchRemain = 1f;

        void Awake()
        {
            if (sfxSource == null)
            {
                sfxSource = gameObject.AddComponent<AudioSource>();
                sfxSource.playOnAwake = false;
                sfxSource.spatialBlend = 0f;   // 2D 俯视用 2D 音效更清晰
            }
        }

        void OnEnable()
        {
            RoomEvents.OnSearchStarted += OnSearchStarted;
            RoomEvents.OnSearchInterrupted += OnSearchInterrupted;
            RoomEvents.OnItemCollected += OnItemCollected;
            RoomEvents.OnItemKnockedOut += OnItemKnockedOut;
            RoomEvents.OnElbowHit += OnElbowHit;
            RoomEvents.OnJump += OnJump;
            RoomEvents.OnLand += OnLand;
            RoomEvents.OnTileCracking += OnTileCracking;
            RoomEvents.OnTileCollapsed += OnTileCollapsed;
            RoomEvents.OnPlayerPitfall += OnPitfall;
            RoomEvents.OnFinalCollapseStarted += OnFinalCollapse;
            RoomEvents.OnTimerTick += OnTimerTick;
            RoomEvents.OnPhaseChanged += OnPhaseChanged;
        }

        void OnDisable()
        {
            RoomEvents.OnSearchStarted -= OnSearchStarted;
            RoomEvents.OnSearchInterrupted -= OnSearchInterrupted;
            RoomEvents.OnItemCollected -= OnItemCollected;
            RoomEvents.OnItemKnockedOut -= OnItemKnockedOut;
            RoomEvents.OnElbowHit -= OnElbowHit;
            RoomEvents.OnJump -= OnJump;
            RoomEvents.OnLand -= OnLand;
            RoomEvents.OnTileCracking -= OnTileCracking;
            RoomEvents.OnTileCollapsed -= OnTileCollapsed;
            RoomEvents.OnPlayerPitfall -= OnPitfall;
            RoomEvents.OnFinalCollapseStarted -= OnFinalCollapse;
            RoomEvents.OnTimerTick -= OnTimerTick;
            RoomEvents.OnPhaseChanged -= OnPhaseChanged;
        }

        // ══════════════════════════════════════════════
        //  播放helper
        // ══════════════════════════════════════════════

        void Play(AudioClip clip, Vector3 at, float pitchJitter = 0.06f)
        {
            if (clip == null || sfxSource == null) return;

            // 轻微随机音调，避免同一音效连播时的机械感
            sfxSource.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            sfxSource.PlayOneShot(clip, sfxVolume);
        }

        void Spawn(GameObject prefab, Vector3 at)
        {
            if (prefab == null) return;
            var go = Instantiate(prefab, at, Quaternion.identity);
            if (vfxLifetime > 0f) Destroy(go, vfxLifetime);
        }

        // ══════════════════════════════════════════════
        //  事件回调
        // ══════════════════════════════════════════════

        void OnSearchStarted(PlayerActor p, SearchContainer c)
        {
            Play(sfxSearchStart, p != null ? p.transform.position : Vector3.zero);
        }

        void OnSearchInterrupted(PlayerActor p, SearchContainer c)
        {
            Play(sfxSearchInterrupt, p != null ? p.transform.position : Vector3.zero);
        }

        void OnItemCollected(PlayerActor p, ItemDefinition item)
        {
            var pos = p != null ? p.transform.position : Vector3.zero;

            // 稀有道具用专属音效，这是给玩家的即时正反馈
            var clip = (item != null && item.isRare && sfxItemRare != null)
                ? sfxItemRare : sfxItemCollected;

            Play(clip, pos);
            Spawn(vfxItemPickup, pos + Vector3.up);
        }

        void OnItemKnockedOut(PlayerActor p, ItemDefinition item)
        {
            Play(sfxItemKnockedOut, p != null ? p.transform.position : Vector3.zero);
        }

        void OnElbowHit(PlayerActor attacker, PlayerActor victim)
        {
            if (victim == null) return;
            var pos = victim.transform.position + Vector3.up * 0.9f;
            Play(sfxElbowHit, pos, 0.1f);
            Spawn(vfxElbowImpact, pos);
        }

        void OnJump(PlayerActor p)
        {
            Play(sfxJump, p != null ? p.transform.position : Vector3.zero);
        }

        void OnLand(PlayerActor p, float fallHeight)
        {
            if (p == null) return;

            bool heavy = fallHeight > heavyLandHeight;
            var clip = (heavy && sfxLandHeavy != null) ? sfxLandHeavy : sfxLand;
            Play(clip, p.transform.position);

            if (heavy) Spawn(vfxLandDust, p.transform.position);
        }

        void OnTileCracking(FloorTile t)
        {
            if (t == null) return;
            Play(sfxTileCracking, t.transform.position, 0.03f);
            Spawn(vfxTileCracking, t.transform.position + Vector3.up * 0.1f);
        }

        void OnTileCollapsed(FloorTile t)
        {
            if (t == null) return;
            Play(sfxTileCollapsed, t.transform.position);
            Spawn(vfxTileCollapse, t.transform.position);
        }

        void OnPitfall(PlayerActor p)
        {
            Play(sfxPitfall, p != null ? p.transform.position : Vector3.zero);
        }

        void OnFinalCollapse()
        {
            Play(sfxFinalCollapse, Vector3.zero, 0f);
        }

        void OnPhaseChanged(RoundPhase phase)
        {
            if (alarmLoop == null) return;

            bool shouldPlay = phase == RoundPhase.Searching;
            if (shouldPlay && !alarmLoop.isPlaying)
            {
                alarmLoop.loop = true;
                alarmLoop.Play();
            }
            else if (!shouldPlay && alarmLoop.isPlaying)
            {
                alarmLoop.Stop();
            }
        }

        void OnTimerTick(float remain)
        {
            // 记录首次收到的剩余时间作为总时长基准
            if (remain > _searchTotal) _searchTotal = remain;
            _searchRemain = remain;

            if (alarmLoop == null || !alarmLoop.isPlaying) return;

            // 越接近结束，警报越急越响
            float t = 1f - Mathf.Clamp01(_searchRemain / Mathf.Max(0.01f, _searchTotal));
            alarmLoop.pitch = Mathf.Lerp(alarmPitchRange.x, alarmPitchRange.y, t);
            alarmLoop.volume = Mathf.Lerp(alarmVolumeRange.x, alarmVolumeRange.y, t);
        }
    }
}
