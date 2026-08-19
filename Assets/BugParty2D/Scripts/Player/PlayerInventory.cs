using System.Collections.Generic;
using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>玩家背包。容量上限由 Config 控制（默认 2）。</summary>
    public class PlayerInventory : MonoBehaviour
    {
        readonly List<ItemDefinition> _items = new List<ItemDefinition>();

        PlayerActor _actor;
        int _capacity = 2;

        public IReadOnlyList<ItemDefinition> Items => _items;
        public int Count => _items.Count;
        public int Capacity => _capacity;
        public bool IsFull => _items.Count >= _capacity;
        public bool IsEmpty => _items.Count == 0;

        /// <summary>战利品总价值，用于结算排名。</summary>
        public int TotalValue
        {
            get
            {
                int v = 0;
                for (int i = 0; i < _items.Count; i++)
                    if (_items[i] != null) v += _items[i].lootValue;
                return v;
            }
        }

        public void Init(PlayerActor actor, int capacity)
        {
            _actor = actor;
            _capacity = Mathf.Max(1, capacity);
            _items.Clear();
        }

        public bool TryAdd(ItemDefinition item)
        {
            if (item == null || IsFull) return false;
            _items.Add(item);
            RoomEvents.RaiseInventoryChanged(_actor);
            return true;
        }

        /// <summary>移除最后拿到的一件。被肘击或坠落时调用。</summary>
        public ItemDefinition PopLatest()
        {
            if (IsEmpty) return null;
            int last = _items.Count - 1;
            var item = _items[last];
            _items.RemoveAt(last);
            RoomEvents.RaiseInventoryChanged(_actor);
            return item;
        }

        public void Clear()
        {
            if (_items.Count == 0) return;
            _items.Clear();
            RoomEvents.RaiseInventoryChanged(_actor);
        }

        public List<string> ExportIds()
        {
            var ids = new List<string>(_items.Count);
            for (int i = 0; i < _items.Count; i++)
                if (_items[i] != null) ids.Add(_items[i].itemId);
            return ids;
        }

        public string Describe()
        {
            if (_items.Count == 0) return "（空手）";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] == null) continue;
                sb.Append(_items[i].displayName);
                if (i < _items.Count - 1) sb.Append('、');
            }
            return sb.ToString();
        }
    }
}
