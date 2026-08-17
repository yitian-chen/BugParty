using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PartyGame.UI
{
    /// <summary>Result panel shown at match end. Ranks islands by score, common fish is tiebreaker (higher wins).</summary>
    public class PartyResultUI : MonoBehaviour
    {
        [System.Serializable]
        public class ResultRow
        {
            public TextMeshProUGUI rankLabel;
            public TextMeshProUGUI infoLabel;
        }

        [SerializeField] private GameObject root;
        [SerializeField] private ResultRow[] rows;

        private void Awake()
        {
            if (root != null) root.SetActive(false);
        }

        private void OnEnable()
        {
            if (PartyGameManager.Instance != null)
                PartyGameManager.Instance.OnStateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (PartyGameManager.Instance != null)
                PartyGameManager.Instance.OnStateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(object sender, System.EventArgs e)
        {
            if (PartyGameManager.Instance.IsGameOver()) Show();
        }

        public void Show()
        {
            if (root != null) root.SetActive(true);
            Populate();
        }

        private void Populate()
        {
            if (rows == null) return;
            PartyGameConfig cfg = PartyGameManager.Instance != null ? PartyGameManager.Instance.Config : null;
            List<Island> list = new List<Island>();
            foreach (Island i in PartyGameManager.Instance.Islands)
            {
                if (i != null) list.Add(i);
            }
            list.Sort((a, b) =>
            {
                int sa = a.GetScore(cfg), sb = b.GetScore(cfg);
                if (sa != sb) return sb.CompareTo(sa);
                return b.GoldenFishCount.CompareTo(a.GoldenFishCount);
            });

            for (int i = 0; i < rows.Length; i++)
            {
                ResultRow row = rows[i];
                if (row == null) continue;
                if (i < list.Count)
                {
                    Island isl = list[i];
                    if (row.rankLabel != null) row.rankLabel.text = $"#{i + 1}";
                    if (row.infoLabel != null)
                        row.infoLabel.text = $"P{isl.OwnerPlayerIndex + 1}   Score {isl.GetScore(cfg)}   (Common {isl.CommonFishCount}  Golden {isl.GoldenFishCount})";
                }
                else
                {
                    if (row.rankLabel != null) row.rankLabel.text = "";
                    if (row.infoLabel != null) row.infoLabel.text = "";
                }
            }
        }
    }
}
