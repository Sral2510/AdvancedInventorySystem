using AIS.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AIS
{
    public class DefaultInventory<TAmount> : IInventorySystemWrapper<DefaultItem, DefaultItemData<TAmount>, TAmount, DefaultInventorySaveData<TAmount>>
        where TAmount : struct
    {
        private readonly InventorySystem<DefaultItem, DefaultItemData<TAmount>, TAmount, DefaultInventorySaveData<TAmount>> _inventorySystem;

        public event UpdatedInventoryHandler<DefaultItem> UpdatedInventory = delegate { };
        public event UpdatedInventoryTagHandler<DefaultItem> UpdatedInventoryTag = delegate { };

        public DefaultInventory()
        {
            _inventorySystem = new InventorySystem<DefaultItem, DefaultItemData<TAmount>, TAmount, DefaultInventorySaveData<TAmount>>();

            _inventorySystem.UpdatedInventory += (changed) => UpdatedInventory(changed);
            _inventorySystem.UpdatedInventoryTag += (tag, set) => UpdatedInventoryTag(tag, set);
        }

        public Task<bool> TryAddRemove(DefaultItem item, TAmount Amount)
        {
            return _inventorySystem.TryAddRemoveItem(item, Amount);
        }

        /// <summary>
        /// Attempts to change a single item from the inventory.
        /// </summary>
        /// <param name="ID">The item id to be changed.</param>
        /// <param name="Amount">The amount to add/remove.</param>
        /// <returns>True if change succeeded</returns>
        public Task<bool> TryAddRemove(int ID, TAmount Amount)
        {
            return _inventorySystem.TryAddRemoveItem(new DefaultItem(ID), Amount);
        }
        
        /// <summary>
        /// Attempts to remove multiple items from the inventory asynchronously.
        /// Does not partially change inventory data, if the check on an item fails no inventory data gets changed.
        /// </summary>
        /// <param name="ChangeList">List of item id and amount pairs.</param>
        /// <returns>True if all changes succeeded</returns>
        public Task<bool> TryAddRemove(List<KeyValuePair<int, TAmount>> ChangeList)
        {
            var convertedChangeList = ChangeList
                .Select(kvp => new KeyValuePair<DefaultItem, TAmount>(new DefaultItem(kvp.Key), kvp.Value))
                .ToList();

            return _inventorySystem.TryAddRemoveItem(convertedChangeList);
        }

        public TAmount GetQuantity(DefaultItem item)
        {
            return _inventorySystem.Inventory.TryGetValue(item, out var data) ? data.Quantity : default;
        }

        /// <summary>
        /// Retrieve the quantity of an item stored in the inventory.
        /// </summary>
        /// <param name="ID">item id to be retrieved</param>
        /// <returns>Value of quantity</returns>
        public TAmount GetQuantity(int ID)
        {
            return _inventorySystem.Inventory.TryGetValue(new DefaultItem(ID), out var data) ? data.Quantity : default;
        }

        /// <summary>
        /// Retrieve a snapshot of the Inventory as a Dictionary with (int)id key and (int)quantity value
        /// </summary>
        /// <returns>Dicitionary with int key and int value</returns>
        public Dictionary<int, TAmount> GetIntInventorySnapshot()
        {
            var snapshot = new Dictionary<int, TAmount>();
            foreach (var kvp in _inventorySystem.Inventory)
            {
                snapshot[kvp.Key.ID] = kvp.Value.Quantity;
            }
            return snapshot;
        }

        public Dictionary<DefaultItem, TAmount> GetInventorySnapshot()
        {
            var snapshot = new Dictionary<DefaultItem, TAmount>();
            foreach (var kvp in _inventorySystem.Inventory)
            {
                snapshot[kvp.Key] = kvp.Value.Quantity;
            }
            return snapshot;
        }

        public void SetTagLookUpTable(Dictionary<int, string> ExternalTagLookUpTable) 
        {
            var changedExternalTagLookUpTable = ExternalTagLookUpTable.ToDictionary(
                kvp => new DefaultItem(kvp.Key),
                kvp => kvp.Value
            );
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
        
        public bool SingleItemCheck(int ID, TAmount compareAmount = default)
        {
            return _inventorySystem.SingleItemCheck(new DefaultItem(ID), compareAmount);
        }
        
        public bool MultiItemCheck(List<int> IDs, TAmount compareAmount = default)
        {
            var converted = IDs.Select(id => new DefaultItem(id)).ToList();
            return _inventorySystem.MultiItemCheck(converted, compareAmount);
        }
    
        public bool MultiItemCheck(List<KeyValuePair<int, TAmount>> checkPairs)
        {
            var converted = checkPairs
                .Select(kvp => new KeyValuePair<DefaultItem, TAmount>(new DefaultItem(kvp.Key), kvp.Value))
                .ToList();
    
            return _inventorySystem.MultiItemCheck(converted);
        }
    }
    
    /// <summary>
    /// Default Implementation of an InventorySystem that has a Dictionary where the (int)ID of the item stored gets asigned a (int)quantity.
    /// </summary>
    public class DefaultIntInventory : DefaultInventory<int>
    {
        
    }
    
    public class DefaultFloatInventory : DefaultInventory<float>
    {
        
    }
    
    public class DefaultInventoryDisplay<TAmount> : IInventoryDisplaySystemWrapper<DefaultItem, DefaultItemData<TAmount>, TAmount, DefaultInventorySaveData<TAmount>>
        where TAmount : struct
    {
        protected readonly DefaultInventory<TAmount> _inventorySystem;

        public DefaultInventoryDisplay(DefaultInventory<TAmount> inventory)
        {
            _inventorySystem = inventory;

            _inventorySystem.UpdatedInventory += OnInventoryUpdated;
            _inventorySystem.UpdatedInventoryTag += OnInventoryTagUpdated;
        }

        /// <summary>
        /// Called whenever the inventory is updated.
        /// </summary>
        /// <param name="ChangedItem">The set of items that were changed.</param>
        public virtual void OnInventoryUpdated(HashSet<DefaultItem> ChangedItem)
        {

        }

        /// <summary>
        /// Called whenever an Item with a tag in the inventory is updated.
        /// </summary>
        /// <param name="ChangedTag">The tag that was updated.</param>
        /// <param name="TagHashSet">The set of all items under the tag.</param>
        public virtual void OnInventoryTagUpdated(string ChangedTag, HashSet<DefaultItem> TagHashSet)
        {

        }
    }
    
    public class DefaultIntInventoryDisplay : DefaultInventoryDisplay<int>
    {
        public DefaultIntInventoryDisplay(DefaultIntInventory inventory)
            : base(inventory)
        {
        }
    }
    
    public class DefaultIntInventoryDisplay : DefaultInventoryDisplay<float>
    {
        public DefaultIntInventoryDisplay(DefaultFloatInventory inventory)
            : base(inventory)
        {
        }
    }
}
    