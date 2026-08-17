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
        [SerializeField] private Material commonMat;
        [SerializeField] private Material goldenMat;
        [Tooltip("Local positions of each fish slot on the raft (up to raftFishCapacity).")]
        [SerializeField] private Vector3[] slotLocalPositions = new Vector3[]{
            new Vector3(-0.3f, 0.55f, -0.2f),
            new Vector3( 0.3f, 0.55f, -0.2f),
        };

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
            if (owner == null || slotsRoot == null || fishDiscPrefab == null) return;

            // Rebuild discs (simple and cheap given raft capacity ~2).
            for (int i = discs.Count - 1; i >= 0; i--)
            {
                if (discs[i] != null) Destroy(discs[i]);
            }
            discs.Clear();

            int idx = 0;
            for (int i = 0; i < owner.CarriedCommon && idx < slotLocalPositions.Length; i++, idx++)
            {
                SpawnDisc(idx, commonMat);
            }
            for (int i = 0; i < owner.CarriedGolden && idx < slotLocalPositions.Length; i++, idx++)
            {
                SpawnDisc(idx, goldenMat);
            }
        }

        private void SpawnDisc(int slotIndex, Material mat)
        {
            var d = Instantiate(fishDiscPrefab, slotsRoot);
            d.transform.localPosition = slotLocalPositions[slotIndex];
            d.transform.localRotation = Quaternion.identity;
            if (mat != null)
            {
                var r = d.GetComponentInChildren<Renderer>();
                if (r != null) r.sharedMaterial = mat;
            }
            discs.Add(d);
        }
    }
}
