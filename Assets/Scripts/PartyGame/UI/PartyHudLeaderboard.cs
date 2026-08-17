using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PartyGame.UI
{
    /// <summary>Right-bottom leaderboard: one row per island with score updated live.</summary>
    public class PartyHudLeaderboard : MonoBehaviour
    {
        [System.Serializable]
        public class Row
        {
            public TextMeshProUGUI label;
            public Color playerColor = Color.white;
        }

        [SerializeField] private Row[] rows;
        private readonly List<Island> islands = new List<Island>();

        private void Start()
        {
            RefreshIslandList();
            SubscribeAll();
            Redraw();
        }

        private void OnDestroy()
        {
            UnsubscribeAll();
        }

        private void RefreshIslandList()
        {
            islands.Clear();
            if (PartyGameManager.Instance != null)
            {
                foreach (Island i in PartyGameManager.Instance.Islands)
                {
                    if (i != null) islands.Add(i);
                }
            }
            islands.Sort((a, b) => a.OwnerPlayerIndex.CompareTo(b.OwnerPlayerIndex));
        }

        private void SubscribeAll()
        {
            foreach (Island i in islands)
            {
                if (i != null) i.OnFishCountChanged += HandleChanged;
            }
        }

        private void UnsubscribeAll()
        {
            foreach (Island i in islands)
            {
                if (i != null) i.OnFishCountChanged -= HandleChanged;
            }
        }

        private void HandleChanged(object sender, System.EventArgs e) => Redraw();

        private void Redraw()
        {
            if (rows == null) return;
            PartyGameConfig cfg = PartyGameManager.Instance != null ? PartyGameManager.Instance.Config : null;
            for (int i = 0; i < rows.Length; i++)
            {
                Row row = rows[i];
                if (row == null || row.label == null) continue;
                if (i < islands.Count)
                {
                    Island isl = islands[i];
                    row.label.text = $"P{isl.OwnerPlayerIndex + 1}  {isl.GetScore(cfg)}  (C{isl.CommonFishCount} G{isl.GoldenFishCount})";
                    row.label.color = row.playerColor;
                }
                else
                {
                    row.label.text = "";
                }
            }
        }
    }
}
