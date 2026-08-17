using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Renders fish discs stacked on the island platform.
    /// Updates in response to Island.OnFishCountChanged and also accepts
    /// "flying" fish from a raft handoff (see SpawnFlyingFish).
    /// </summary>
    public class IslandFishVisual : MonoBehaviour
    {
        [SerializeField] private Island island;
        [SerializeField] private Transform stackRoot;
        [SerializeField] private GameObject fishDiscPrefab;
        [Tooltip("Optional: use this prefab for common fish (overrides fishDiscPrefab).")]
        [SerializeField] private GameObject fishCommonPrefab;
        [Tooltip("Optional: use this prefab for golden fish (overrides fishDiscPrefab).")]
        [SerializeField] private GameObject fishGoldenPrefab;
        [SerializeField] private Material commonMat;
        [SerializeField] private Material goldenMat;

        [Tooltip("Grid dimensions of the fish stack on the platform.")]
        [SerializeField] private int columns = 4;
        [SerializeField] private float spacing = 0.35f;
        [SerializeField] private float rowHeight = 0.12f;
        [SerializeField] private float flyDuration = 0.6f;
        [SerializeField] private float flyArc = 1.5f;

        [Tooltip("Uniform scale applied to spawned fish (relative to the prefab).")]
        [SerializeField] private float fishSpawnScale = 0.01f;
        [Tooltip("Local euler rotation applied to spawned fish. Default lays fish flat with head along +X of the stack; alternation flips flank.")]
        [SerializeField] private Vector3 fishSpawnEuler = new Vector3(90f, 0f, 0f);
        [Tooltip("If true, alternates fish roll so one shows one flank, the next shows the other (eyes up vs down).")]
        [SerializeField] private bool alternateOrientation = true;

        private readonly List<GameObject> commonDiscs = new List<GameObject>();
        private readonly List<GameObject> goldenDiscs = new List<GameObject>();

        private void OnEnable()
        {
            if (island != null) island.OnFishCountChanged += HandleChanged;
        }

        private void OnDisable()
        {
            if (island != null) island.OnFishCountChanged -= HandleChanged;
        }

        private void HandleChanged(object sender, System.EventArgs e)
        {
            // Only reconcile SHRINKING (steals). Growth is driven by SpawnFlyingFish so we can animate.
            if (commonDiscs.Count > island.CommonFishCount) ReconcileShrink(commonDiscs, island.CommonFishCount);
            if (goldenDiscs.Count > island.GoldenFishCount) ReconcileShrink(goldenDiscs, island.GoldenFishCount);
        }

        private void ReconcileShrink(List<GameObject> list, int target)
        {
            while (list.Count > target)
            {
                var last = list[list.Count - 1];
                list.RemoveAt(list.Count - 1);
                if (last != null) Destroy(last);
            }
        }

        private Vector3 ComputeStackLocalPos(int idx)
        {
            int row = idx / columns;
            int col = idx % columns;
            float x = (col - (columns - 1) * 0.5f) * spacing;
            float y = row * rowHeight;
            return new Vector3(x, y, 0f);
        }

        /// <summary>
        /// Play a "flying fish" animation from world position `fromWorld` to the next stack slot.
        /// The disc becomes a real stack entry when it lands.
        /// </summary>
        public void SpawnFlyingFish(FishType type, Vector3 fromWorld)
        {
            if (stackRoot == null) return;
            GameObject prefab = type == FishType.Common
                ? (fishCommonPrefab != null ? fishCommonPrefab : fishDiscPrefab)
                : (fishGoldenPrefab != null ? fishGoldenPrefab : fishDiscPrefab);
            if (prefab == null) return;

            var d = Instantiate(prefab, stackRoot);
            bool usingFallback = (type == FishType.Common ? fishCommonPrefab == null : fishGoldenPrefab == null);
            if (usingFallback)
            {
                Material mat = type == FishType.Common ? commonMat : goldenMat;
                if (mat != null)
                {
                    var r = d.GetComponentInChildren<Renderer>();
                    if (r != null) r.sharedMaterial = mat;
                }
            }

            List<GameObject> list = type == FishType.Common ? commonDiscs : goldenDiscs;
            int targetIndex = list.Count; // where it will land
            Vector3 destLocal = ComputeStackLocalPos(targetIndex);
            list.Add(d); // reserve the slot so subsequent flies compute the next slot

            // Apply scale + orientation ahead of the fly animation so we see the right pose.
            d.transform.localScale = Vector3.one * fishSpawnScale;

            d.transform.position = fromWorld;
            StartCoroutine(FlyRoutine(d.transform, destLocal, targetIndex));
        }

        private IEnumerator FlyRoutine(Transform t, Vector3 destLocal, int slotIndex)
        {
            Vector3 startWorld = t.position;
            float e = 0f;
            while (e < flyDuration)
            {
                if (t == null) yield break;
                e += Time.deltaTime;
                float k = Mathf.Clamp01(e / flyDuration);
                Vector3 destWorld = stackRoot.TransformPoint(destLocal);
                Vector3 pos = Vector3.Lerp(startWorld, destWorld, k);
                pos.y += Mathf.Sin(k * Mathf.PI) * flyArc;
                t.position = pos;
                yield return null;
            }
            if (t != null)
            {
                t.SetParent(stackRoot, false);
                t.localPosition = destLocal;
                Vector3 euler = fishSpawnEuler;
                if (alternateOrientation && slotIndex % 2 == 1) euler.x = -euler.x;
                t.localRotation = Quaternion.Euler(euler);
                t.localScale = Vector3.one * fishSpawnScale;
            }
        }
    }
}
