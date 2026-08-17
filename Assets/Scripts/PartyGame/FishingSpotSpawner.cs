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
            nextWaveIndex = 0;
            nextWaveTimer = 0f;
            waveActive = false;
            ClearActiveSpots();
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
            for (int attempt = 0; attempt < 20; attempt++)
            {
                pos = new Vector3(Random.Range(-half.x, half.x), 0f, Random.Range(-half.y, half.y));
                if (IsFarEnoughFromIslands(pos)) return pos;
            }
            return pos;
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
