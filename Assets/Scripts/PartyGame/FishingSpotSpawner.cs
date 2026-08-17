using System.Collections.Generic;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Spawns fishing spots wave-by-wave. Driven by PartyGameManager.
    /// Waves 1-5 spawn `commonSpotsPerWave` common spots at random locations
    /// (within map bounds, respecting minimum distance from islands).
    /// Wave 6 spawns a single golden spot at the map center.
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

        public void OnMatchStarted()
        {
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
            if (Config == null) return;
            if (activeSpots.Count > 0) return;
            SpawnWave(0);
            foreach (var s in activeSpots) if (s != null) s.SetPaused(true);
            nextWaveIndex = 0; // Will be advanced to 1 in OnMatchStarted.
        }

        private void Update()
        {
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
            spot.Initialize(FishType.Common, Config.fishPerCommonSpot, Config.waveInterval, Config.commonSpotRadius);
            activeSpots.Add(spot);
        }

        private void SpawnGolden(Vector3 pos)
        {
            if (goldenSpotPrefab == null) return;
            FishingSpot spot = Instantiate(goldenSpotPrefab, pos, Quaternion.identity, spotsParent);
            spot.Initialize(FishType.Golden, -1, Config.waveInterval, Config.goldenSpotRadius);
            activeSpots.Add(spot);
        }

        private Vector3 PickCommonSpotPosition()
        {
            Vector3 pos = Vector3.zero;
            Vector2 half = Config.mapHalfExtents;
            float edgeMargin = 3f; // Keep away from map edges.
            float xMax = Mathf.Max(1f, half.x - edgeMargin);
            float zMax = Mathf.Max(1f, half.y - edgeMargin);
            float minSpotDist = Config.commonSpotRadius * 2.5f; // Prevent overlaps.

            for (int attempt = 0; attempt < 40; attempt++)
            {
                pos = new Vector3(Random.Range(-xMax, xMax), 0f, Random.Range(-zMax, zMax));
                if (IsFarEnoughFromIslands(pos) && IsFarEnoughFromActiveSpots(pos, minSpotDist)) return pos;
            }
            return pos;
        }

        private bool IsFarEnoughFromActiveSpots(Vector3 pos, float minDist)
        {
            foreach (FishingSpot s in activeSpots)
            {
                if (s == null) continue;
                Vector3 delta = new Vector3(pos.x - s.transform.position.x, 0f, pos.z - s.transform.position.z);
                if (delta.sqrMagnitude < minDist * minDist) return false;
            }
            return true;
        }

        private bool IsFarEnoughFromIslands(Vector3 pos)
        {
            float minDist = Config.commonSpotMinDistanceFromIsland;
            foreach (Transform t in islandCenters)
            {
                if (t == null) continue;
                Vector3 delta = new Vector3(pos.x - t.position.x, 0f, pos.z - t.position.z);
                if (delta.sqrMagnitude < minDist * minDist) return false;
            }
            return true;
        }

        private void ClearActiveSpots()
        {
            for (int i = 0; i < activeSpots.Count; i++)
            {
                if (activeSpots[i] != null) Destroy(activeSpots[i].gameObject);
            }
            activeSpots.Clear();
        }
    }
}
