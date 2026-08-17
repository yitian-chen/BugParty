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
        [SerializeField] private Material commonMat;
        [SerializeField] private Material goldenMat;

        [Tooltip("Grid dimensions of the fish stack on the platform.")]
        [SerializeField] private int columns = 4;
        [SerializeField] private float spacing = 0.35f;
        [SerializeField] private float rowHeight = 0.12f;
        [SerializeField] private float flyDuration = 0.6f;
        [SerializeField] private float flyArc = 1.5f;

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
            if (fishDiscPrefab == null || stackRoot == null) return;
            Material mat = type == FishType.Common ? commonMat : goldenMat;

            var d = Instantiate(fishDiscPrefab, stackRoot);
            if (mat != null)
            {
                var r = d.GetComponentInChildren<Renderer>();
                if (r != null) r.sharedMaterial = mat;
            }

            List<GameObject> list = type == FishType.Common ? commonDiscs : goldenDiscs;
            int targetIndex = list.Count; // where it will land
            Vector3 destLocal = ComputeStackLocalPos(targetIndex);
            list.Add(d); // reserve the slot so subsequent flies compute the next slot

            d.transform.position = fromWorld;
            StartCoroutine(FlyRoutine(d.transform, destLocal));
        }

        private IEnumerator FlyRoutine(Transform t, Vector3 destLocal)
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
                t.localRotation = Quaternion.identity;
            }
        }
    }
}
