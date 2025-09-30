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

    public interface IInventoryData<T>
    {
        T Quantity { get; }

        void Add(T amount);
        bool Check(T amount);
        bool ZeroCheck();
    }

    public interface ISaveData<T>
    {
        string Version { get; set; }


        void Initialize(string version, Dictionary<IItem, IInventoryData<T>> inventory);
        Dictionary<IItem, IInventoryData<T>> GetInventory();
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
    }

    public class DefaultItemData : IInventoryData<int>
    {
        public int Quantity { get; set; } = 0;

        public DefaultItemData()
        {

        }

        public void Add(int Amount)
        {
            Quantity += Amount;
        }

        public bool Check(int Amount)
        {
            return Quantity >= Amount;
        }

        public bool ZeroCheck()
        {
            return Quantity <= 0;
        }
    }

    /// <summary>
    /// Represents the save data for the inventory system.
    /// Contains the version of the save file and a snapshot of all item quantities.
    /// Use this class when saving or loading inventory to/from JSON files.
    /// </summary>
    public class IntInventorySaveData : ISaveData<int>
    {
        /// <summary>
        /// The version of the save data.
        /// </summary>
        public string Version { get; set; } = Constants.BaseVersion;

        /// <summary>
        /// A dictionary mapping item IDs to their quantities.
        /// </summary>
        public Dictionary<int, int> Inventory { get; set; } = new();

        public void Initialize(string version, Dictionary<IItem, IInventoryData<int>> inventory)
        {
            Version = version;
            Inventory = inventory.ToDictionary
                (
                    kvp => kvp.Key.ID,                         // Key: item ID
                    kvp => Convert.ToInt32(kvp.Value.Quantity) // Value: quantity as int
                );
        }

        public Dictionary<IItem, IInventoryData<int>> GetInventory()
        {
            return Inventory.ToDictionary(
                kvp => (IItem)new DefaultItem(kvp.Key),
                kvp => (IInventoryData<int>)new DefaultItemData { Quantity = kvp.Value }
            );
        }


        public IntInventorySaveData()
        {

        }
    }
}
