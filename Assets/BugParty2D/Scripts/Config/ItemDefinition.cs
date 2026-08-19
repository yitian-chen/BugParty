using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 道具定义。Assets ▸ Create ▸ BugParty2D ▸ Item Definition
    /// </summary>
    [CreateAssetMenu(fileName = "Item_", menuName = "BugParty2D/Item Definition", order = 10)]
    public class ItemDefinition : ScriptableObject
    {
        [Header("身份")]
        public string displayName = "新道具";
        public string itemId = "new_item";
        public ItemCategory category = ItemCategory.Fishing;

        [Header("价值")]
        [Tooltip("战利品评分，用于结算排名")]
        [Min(0)] public int lootValue = 100;

        [Header("外观")]
        public GameObject worldPrefab;
        public Color placeholderColor = Color.white;
        public Vector3 placeholderSize = new Vector3(0.4f, 0.4f, 0.4f);

        [Header("刷新")]
        [Min(0.01f)] public float spawnWeight = 1f;
        public bool isRare = false;

        [Header("下一关效果说明")]
        [TextArea(2, 4)] public string effectSummary = "";
    }
}
