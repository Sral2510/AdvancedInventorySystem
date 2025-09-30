using AIS.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIS
{
    public class DefaultInventory : IInventorySystemWrapper<DefaultItem, DefaultItemData, int, IntInventorySaveData>
    {
        private readonly InventorySystem<DefaultItem, DefaultItemData, int, IntInventorySaveData> _inventorySystem;

        public DefaultInventory()
        {
            _inventorySystem = new InventorySystem<DefaultItem, DefaultItemData, int, IntInventorySaveData>();
        }

        public Task<bool> TryAddRemove(DefaultItem item, int amount)
        {
            return _inventorySystem.TryAddRemoveItem(item, amount);
        }

        public int GetQuantity(DefaultItem item)
        {
            return _inventorySystem.Inventory.TryGetValue(item, out var data) ? data.Quantity : 0;
        }

        public Dictionary<DefaultItem, int> GetInventorySnapshot()
        {
            var snapshot = new Dictionary<DefaultItem, int>();
            foreach (var kvp in _inventorySystem.Inventory)
            {
                snapshot[kvp.Key] = kvp.Value.Quantity;
            }
            return snapshot;
        }

        public Task Save(string path)
        {
            return _inventorySystem.Save(path);
        }

        public Task Load(string path)
        {
            return _inventorySystem.Load(path);
        }

        public void PauseProcess()
        {
            _inventorySystem.PauseProcessing();
        }

        public void ContinueProcess()
        {
            _inventorySystem.ResumeProcessing();
        }
    }
}
