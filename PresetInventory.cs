using AIS.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIS
{
    /// <summary>
    /// Default Implementation of an InventorySystem that has a Dictionary where the (int)ID of the item stored gets asigned a (int)quantity.
    /// </summary>
    public class DefaultIntInventory : IInventorySystemWrapper<DefaultItem, DefaultItemData, int, IntInventorySaveData>
    {
        private readonly InventorySystem<DefaultItem, DefaultItemData, int, IntInventorySaveData> _inventorySystem;

        public DefaultInventory()
        {
            _inventorySystem = new InventorySystem<DefaultItem, DefaultItemData, int, IntInventorySaveData>();
        }

        /// <summary>
        /// Attempts to change a single item from the inventory.
        /// </summary>
        /// <param name="ID">The item id to be changed.</param>
        /// <param name="Amount">The amount to add/remove.</param>
        /// <returns>True if change succeeded</returns>
        public Task<bool> TryAddRemove(int ID, int Amount)
        {
            return _inventorySystem.TryAddRemoveItem(new DefaultItem(ID), Amount);
        }
        
        /// <summary>
        /// Attempts to remove multiple items from the inventory asynchronously.
        /// Does not partially change inventory data, if the check on an item fails no inventory data gets changed.
        /// </summary>
        /// <param name="ChangeList">List of item id and amount pairs.</param>
        /// <returns>True if all changes succeeded</returns>
        public Task<bool> TryAddRemoveItem(List<KeyValuePair<int, int>> ChangeList)
        {
            var convertedChangeList = ChangeList
                .Select(kvp => new KeyValuePair<DefaultItem, int>(new DefaultItem(kvp.Key), kvp.Value))
                .ToList();
            
            return _inventorySystem.TryAddRemoveItem(convertedChangeList)
        }

        /// <summary>
        /// Retrieve the quantity of an item stored in the inventory.
        /// </summary>
        /// <param name="ID">item id to be retrieved</param>
        /// <returns>Value of quantity</returns>
        public int GetQuantity(int ID)
        {
            return _inventorySystem.Inventory.TryGetValue(item, out var data) ? data.Quantity : 0;
        }

        /// <summary>
        /// Retrieve a snapshot of the Inventory as a Dictionary with (int)id key and (int)quantity value
        /// </summary>
        /// <returns>Dicitionary with int key and int value</returns>
        public Dictionary<int, int> GetInventorySnapshot()
        {
            var snapshot = new Dictionary<int, int>();
            foreach (var kvp in _inventorySystem.Inventory)
            {
                snapshot[kvp.Key.ID] = kvp.Value.Quantity;
            }
            return snapshot;
        }
        
        public void SetTagLookUpTable(Dictionary<int, string> ExternalTagLookUpTable) 
        {
            var changedExternalTagLookUpTable = ExternalTagLookUpTable.ToDictionary(
                kvp => new DefaultItem(kvp.Key),
                kvp => kvp.Value
            )
            _inventorySystem.SetTagLookUpTable(changedExternalTagLookUpTable);
        }
        
        /// <summary>
        /// Saves the inventory to a JSON file asynchronously.
        /// </summary>
        /// <param name="FilePath">The path to the save file.</param>
        public Task Save(string path)
        {
            return _inventorySystem.Save(path);
        }

        /// <summary>
        /// Loads the inventory from a JSON file asynchronously.
        /// </summary>
        /// <param name="FilePath">The path to the save file.</param>
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
    
    public class DefaultIntInventoryDisplay : IInventoryDisplaySystemWrapper<DefaultItem, DefaultItemData, int, IntInventorySaveData>
    {
        protected readonly InventorySystem<TKey, TValue, TAmount, TSave> _inventorySystem;

        public InventoryDisplay(InventorySystem<TKey, TValue, TAmount, TSave> inventory)
        {
            _inventorySystem = inventory;

            _inventorySystem.UpdatedInventory += OnInventoryUpdated;
            _inventorySystem.UpdatedInventoryTag += OnInventoryTagUpdated;
        }

        /// <summary>
        /// Called whenever the inventory is updated.
        /// </summary>
        /// <param name="ChangedItem">The set of items that were changed.</param>
        protected virtual void OnInventoryUpdated(HashSet<TKey> ChangedItem)
        {

        }

        /// <summary>
        /// Called whenever an Item with a tag in the inventory is updated.
        /// </summary>
        /// <param name="ChangedTag">The tag that was updated.</param>
        /// <param name="TagHashSet">The set of all items under the tag.</param>
        protected virtual void OnInventoryTagUpdated(string ChangedTag, HashSet<TKey> TagHashSet)
        {

        }
    }
}
