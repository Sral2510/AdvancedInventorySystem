using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AIS.Interface;

namespace AIS.Interface
{
    public interface IItem
    {
        int ID { get; }
    }

    public interface IInventoryData<TAmount>
    {
        TAmount Quantity { get; }

        void Add(TAmount amount);
        bool Check(TAmount amount);
        bool ZeroCheck();
    }

    public interface ISaveData<TAmount>
    {
        string Version { get; set; }


        void Initialize(string version, Dictionary<IItem, IInventoryData<TAmount>> inventory);
        Dictionary<IItem, IInventoryData<TAmount>> GetInventory();
    }

    public interface IInventorySystemWrapper<TKey, TValue, TAmount, TSave>
        where TKey : IItem
        where TValue : IInventoryData<TAmount>, new()
        where TAmount : struct
        where TSave : ISaveData<TAmount>, new()
    {
        Task<bool> TryAddRemove(TKey Item, TAmount Amount);
        TAmount GetQuantity(TKey Item);
        Dictionary<TKey, TAmount> GetInventorySnapshot();
        Task Save(string Path);
        Task Load(string Path);
        void PauseProcess();
        void ContinueProcess();
    }
    
    public interface IInventoryDisplaySystemWrapper<TKey, TValue, TAmount, TSave>
        where TKey : IItem
        where TValue : IInventoryData<TAmount>, new()
        where TAmount : struct
        where TSave : ISaveData<TAmount>, new()
    {
        void OnInventoryUpdated(HashSet<TKey> ChangedItem);
        void OnInventoryTagUpdated(string Tag, HashSet<TKey> TagHashSet);
    }
}

namespace AIS
{
    public class DefaultItem : IItem
    {
        public int ID { get; private set; }

        public DefaultItem(int id)
        {
            ID = id;
        }

        public bool Equals(DefaultItem? other)
        {
            if (other is null) return false;
            return ID == other.ID;
        }

        public override bool Equals(object? obj) => Equals(obj as DefaultItem);

        public override int GetHashCode() => ID.GetHashCode();

        public override string ToString() => $"Item({ID})";
    }

    public class DefaultItemData<TAmount> : IInventoryData<TAmount>
        where TAmount : struct
    {
        public TAmount Quantity { get; set; } = default;

        public DefaultItemData()
        {

        }

        public void Add(TAmount Amount)
        {
            Quantity = (dynamic)Quantity + (dynamic)Amount;
        }

        public bool Check(TAmount Amount)
        {
            return (dynamic)Quantity >= (dynamic)Amount;
        }

        public bool ZeroCheck()
        {
            return (dynamic)Quantity <= default(TAmount);
        }
    }

    /// <summary>
    /// Represents the save data for the inventory system.
    /// Contains the version of the save file and a snapshot of all item quantities.
    /// Use this class when saving or loading inventory to/from JSON files.
    /// </summary>
    public class DefaultInventorySaveData<TAmount> : ISaveData<TAmount>
        where TAmount : struct
    {
        /// <summary>
        /// The version of the save data.
        /// </summary>
        public string Version { get; set; } = Constants.BaseVersion;

        /// <summary>
        /// A dictionary mapping item IDs to their quantities.
        /// </summary>
        public Dictionary<int, TAmount> Inventory { get; set; } = new();

        public void Initialize(string version, Dictionary<IItem, IInventoryData<TAmount>> inventory)
        {
            Version = version;
            Inventory = inventory.ToDictionary
                (
                    kvp => kvp.Key.ID,
                    kvp => (TAmount)kvp.Value.Quantity
                );
        }

        public Dictionary<IItem, IInventoryData<TAmount>> GetInventory()
        {
            return Inventory.ToDictionary(
                kvp => (IItem)new DefaultItem(kvp.Key),
                kvp => (IInventoryData<TAmount>)new DefaultItemData<TAmount> { Quantity = kvp.Value }
            );
        }


        public DefaultInventorySaveData()
        {

        }
    }
}
