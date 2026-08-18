using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Spawns fishing spots wave-by-wave. Driven by PartyGameManager.
    /// Waves 1-5 spawn `commonSpotsPerWave` common spots at random locations
    /// (within map bounds, respecting minimum distance from islands).
    /// Wave 6 spawns a single golden spot at the map center.
    ///
    /// Server-authoritative: in networked mode only the server executes SpawnWave
    /// and calls NetworkObject.Spawn so instances replicate to clients.
    /// In solo mode (no networking) it falls back to plain Instantiate.
    /// </summary>
    public class FishingSpotSpawner : MonoBehaviour
    {
        [SerializeField] private PartyGameManager gameManager;
        [SerializeField] private FishingSpot commonSpotPrefab;
        [SerializeField] private FishingSpot goldenSpotPrefab;
        [SerializeField] private Transform spotsParent;
        [SerializeField] private Transform mapCenter;
        [SerializeField] private List<Transform> islandCenters = new List<Transform>();

        private int nextWaveIndex;
        private float nextWaveTimer;
        private bool waveActive;
        private readonly List<FishingSpot> activeSpots = new List<FishingSpot>();

        private PartyGameConfig Config => gameManager != null ? gameManager.Config : null;

        private bool IsSoloMode => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
        private bool CanAuthor => IsSoloMode || NetworkManager.Singleton.IsServer;

        public void OnMatchStarted()
        {
            if (!CanAuthor) return;

            // If we pre-spawned wave 0 during countdown, unpause it and continue from wave 1.
            if (nextWaveIndex == 0 && activeSpots.Count > 0)
            {
                foreach (var s in activeSpots) if (s != null) s.SetPaused(false);
                nextWaveIndex = 1;
                nextWaveTimer = Config.waveInterval;
                return;
            }

            nextWaveIndex = 0;
            nextWaveTimer = 0f;
            waveActive = false;
            ClearActiveSpots();
        }

        /// <summary>
        /// Called during CountdownToStart to place the first wave visible but paused.
        /// </summary>
        public void PreSpawnFirstWave()
        {
            if (!CanAuthor) return;
            if (Config == null) return;
            if (activeSpots.Count > 0) return;
            SpawnWave(0);
            foreach (var s in activeSpots) if (s != null) s.SetPaused(true);
            nextWaveIndex = 0; // Will be advanced to 1 in OnMatchStarted.
        }

        private void Update()
        {
            if (!CanAuthor) return;
            if (gameManager == null || !gameManager.IsGamePlaying()) return;
            if (Config == null) return;

            nextWaveTimer -= Time.deltaTime;
            if (nextWaveTimer <= 0f && nextWaveIndex < Config.totalWaves)
            {
                SpawnWave(nextWaveIndex);
                nextWaveIndex++;
                nextWaveTimer = Config.waveInterval;
            }
        }

        private void SpawnWave(int waveIndex)
        {
            ClearActiveSpots();

            if (waveIndex < Config.totalWaves - 1)
            {
                for (int i = 0; i < Config.commonSpotsPerWave; i++)
                {
                    Vector3 pos = PickCommonSpotPosition();
                    SpawnCommon(pos);
                }
            }
            else
            {
                SpawnGolden(mapCenter != null ? mapCenter.position : Vector3.zero);
            }
        }

        private void SpawnCommon(Vector3 pos)
        {
            if (commonSpotPrefab == null) return;
            FishingSpot spot = Instantiate(commonSpotPrefab, pos, Quaternion.identity, spotsParent);
            var netObj = spot.GetComponent<NetworkObject>();
            if (!IsSoloMode && netObj != null) netObj.Spawn(true);
            spot.Initialize(FishType.Common, Config.fishPerCommonSpot, Config.waveInterval, Config.commonSpotRadius);
            activeSpots.Add(spot);
        }

        private void SpawnGolden(Vector3 pos)
        {
            if (goldenSpotPrefab == null) return;
            FishingSpot spot = Instantiate(goldenSpotPrefab, pos, Quaternion.identity, spotsParent);
            var netObj = spot.GetComponent<NetworkObject>();
            if (!IsSoloMode && netObj != null) netObj.Spawn(true);
            spot.Initialize(FishType.Golden, -1, Config.waveInterval, Config.goldenSpotRadius);
            activeSpots.Add(spot);
        }

        private Vector3 PickCommonSpotPosition()
        {
            Vector2 half = Config.mapHalfExtents;
            float edgeMargin = 3f;
            float xMax = Mathf.Max(1f, half.x - edgeMargin);
            float zMax = Mathf.Max(1f, half.y - edgeMargin);
            float minSpotDist = Config.commonSpotRadius * 2.5f;
            Vector3 center = mapCenter != null ? mapCenter.position : Vector3.zero;

            // Sample many candidates; score each by (min distance to any island) minus penalties for
            // (a) being close to other active spots, (b) being far from the map center. Then take the
            // highest-scoring candidate. This guarantees we always pick the *most central* option
            // available rather than falling back to a poor last-attempt point when the min-distance
            // hard filter can't be satisfied.
            Vector3 best = center;
            float bestScore = float.NegativeInfinity;
            const int candidateCount = 24;
            for (int i = 0; i < candidateCount; i++)
            {
                Vector3 pos = new Vector3(Random.Range(-xMax, xMax), 0f, Random.Range(-zMax, zMax));

                float minIslandDist = MinDistanceToIsland(pos);
                if (minIslandDist < Config.commonSpotMinDistanceFromIsland) continue; // hard reject too-close-to-island

                float minSpotOverlap = MinDistanceToActiveSpots(pos);
                if (minSpotOverlap < minSpotDist) continue; // hard reject overlap

                // Score: reward staying far from islands; penalize distance from map center.
                float distFromCenter = new Vector2(pos.x - center.x, pos.z - center.z).magnitude;
                float score = minIslandDist - 0.4f * distFromCenter;
                if (score > bestScore) { bestScore = score; best = pos; }
            }

            // If no candidate survived both hard filters (rare with 24 samples on a small map), fall
            // back to the most central spot that still respects the min-island distance.
            if (bestScore == float.NegativeInfinity)
            {
                for (int i = 0; i < candidateCount * 2; i++)
                {
                    Vector3 pos = new Vector3(Random.Range(-xMax, xMax), 0f, Random.Range(-zMax, zMax));
                    float minIslandDist = MinDistanceToIsland(pos);
                    if (minIslandDist < Config.commonSpotMinDistanceFromIsland) continue;
                    float distFromCenter = new Vector2(pos.x - center.x, pos.z - center.z).magnitude;
                    float score = -distFromCenter;
                    if (score > bestScore) { bestScore = score; best = pos; }
                }
            }
            return best;
        }

        private float MinDistanceToActiveSpots(Vector3 pos)
        {
            float min = float.PositiveInfinity;
            foreach (FishingSpot s in activeSpots)
            {
                if (s == null) continue;
                Vector3 delta = new Vector3(pos.x - s.transform.position.x, 0f, pos.z - s.transform.position.z);
                float d = delta.magnitude;
                if (d < min) min = d;
            }
            return min;
        }

        private float MinDistanceToIsland(Vector3 pos)
        {
            float min = float.PositiveInfinity;
            foreach (Transform t in islandCenters)
            {
                if (t == null) continue;
                Vector3 delta = new Vector3(pos.x - t.position.x, 0f, pos.z - t.position.z);
                float d = delta.magnitude;
                if (d < min) min = d;
            }
            return min;
        }

        private void ClearActiveSpots()
        {
            for (int i = 0; i < activeSpots.Count; i++)
            {
                var s = activeSpots[i];
                if (s == null) continue;
                if (!IsSoloMode)
                {
                    var netObj = s.GetComponent<NetworkObject>();
                    if (netObj != null && netObj.IsSpawned) { netObj.Despawn(true); continue; }
                }
                Destroy(s.gameObject);
            }
            activeSpots.Clear();
        }
    }
}
