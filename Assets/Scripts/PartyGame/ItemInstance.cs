using System;

namespace PartyGame
{
    [Serializable]
    public class ItemInstance
    {
        public ItemDataSO data;
        public int durability;

        public ItemInstance(ItemDataSO data)
        {
            this.data = data;
            durability = data != null ? data.startingDurability : 0;
        }

        public bool IsEmpty => data == null || durability <= 0;
    }
}
