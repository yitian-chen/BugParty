using UnityEngine;

namespace PartyGame
{
    public enum ItemKind
    {
        SmallNet,
        LargeNet,
        Knife,
        Mine,
        Hook,
        WaterGun,
    }

    public enum ItemCategory
    {
        Fishing,
        Disruption,
        Weapon,
    }

    [CreateAssetMenu(fileName = "ItemData", menuName = "PartyGame/ItemData")]
    public class ItemDataSO : ScriptableObject
    {
        public ItemKind kind;
        public ItemCategory category;
        public string displayName;
        public Sprite icon;
        [Tooltip("Initial durability granted when the item enters an inventory slot.")]
        public int startingDurability = 1;

        [Header("Fishing (only used when category == Fishing)")]
        public float fishingDuration = 5f;
        public int fishingAmount = 1;

        [Header("Mine (only used when kind == Mine)")]
        public GameObject minePrefab;
    }
}
