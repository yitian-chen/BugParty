using System.Collections.Generic;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Renders one round disc per carried fish on the raft, at fixed slot positions.
    /// Discs are colored by fish type (blue = common, gold = golden).
    /// </summary>
    public class RaftFishVisual : MonoBehaviour
    {
        [SerializeField] private PartyPlayer owner;
        [SerializeField] private Transform slotsRoot;
        [SerializeField] private GameObject fishDiscPrefab;
        [Tooltip("Optional: use this prefab for common fish (overrides fishDiscPrefab).")]
        [SerializeField] private GameObject fishCommonPrefab;
        [Tooltip("Optional: use this prefab for golden fish (overrides fishDiscPrefab).")]
        [SerializeField] private GameObject fishGoldenPrefab;
        [SerializeField] private Material commonMat;
        [SerializeField] private Material goldenMat;
        [Tooltip("Local positions of each fish slot on the raft (up to raftFishCapacity).")]
        [SerializeField] private Vector3[] slotLocalPositions = new Vector3[]{
            new Vector3(-0.3f, 0.55f, -0.2f),
            new Vector3( 0.3f, 0.55f, -0.2f),
        };
        [Tooltip("Uniform scale applied to spawned fish (relative to the prefab).")]
        [SerializeField] private float fishSpawnScale = 0.01f;
        [Tooltip("Local euler rotation applied to spawned fish. Default: head points to +Z (ship bow), body horizontal.")]
        [SerializeField] private Vector3 fishSpawnEuler = new Vector3(90f, -90f, 0f);
        [Tooltip("If true, alternates fish roll per slot (one flank up, the other flank up). Keeps head direction the same.")]
        [SerializeField] private bool alternateOrientation = false;

        private readonly List<GameObject> discs = new List<GameObject>();

        private void OnEnable()
        {
            if (owner != null) owner.OnCarriedFishChanged += Refresh;
            Refresh(null, null);
        }

        private void OnDisable()
        {
            if (owner != null) owner.OnCarriedFishChanged -= Refresh;
        }

        public void Bind(PartyPlayer p)
        {
            if (owner != null) owner.OnCarriedFishChanged -= Refresh;
            owner = p;
            if (owner != null) owner.OnCarriedFishChanged += Refresh;
            Refresh(null, null);
        }

        private void Refresh(object sender, System.EventArgs e)
        {
            if (owner == null || slotsRoot == null) return;

            // Rebuild discs (simple and cheap given raft capacity ~2).
            for (int i = discs.Count - 1; i >= 0; i--)
            {
                if (discs[i] != null) Destroy(discs[i]);
            }
            discs.Clear();

            int idx = 0;
            for (int i = 0; i < owner.CarriedCommon && idx < slotLocalPositions.Length; i++, idx++)
            {
                SpawnDisc(idx, FishType.Common);
            }
            for (int i = 0; i < owner.CarriedGolden && idx < slotLocalPositions.Length; i++, idx++)
            {
                SpawnDisc(idx, FishType.Golden);
            }
        }

        private void SpawnDisc(int slotIndex, FishType type)
        {
            GameObject prefab = type == FishType.Common
                ? (fishCommonPrefab != null ? fishCommonPrefab : fishDiscPrefab)
                : (fishGoldenPrefab != null ? fishGoldenPrefab : fishDiscPrefab);
            if (prefab == null) return;

            var d = Instantiate(prefab, slotsRoot);
            d.transform.localPosition = slotLocalPositions[slotIndex];
            // Lay fish flat (long axis horizontal, belly/back up-or-down).
            // Alternate the roll direction per slot so one shows left flank, the next right flank.
            Vector3 euler = fishSpawnEuler;
            if (alternateOrientation && slotIndex % 2 == 1) euler.x = -euler.x;
            d.transform.localRotation = Quaternion.Euler(euler);
            d.transform.localScale = Vector3.one * fishSpawnScale;

            // Only override material for the fallback disc (custom fish prefabs bring their own material).
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
            discs.Add(d);
        }
    }
}
