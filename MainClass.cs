using System.Text.Json;
using System.Threading.Channels;
using AIS;
using AIS.Interface;

public delegate void UpdatedInventoryHandler<TKey>(HashSet<TKey> ChangedItem);
public delegate void UpdatedInventoryTagHandler<TKey>(string ChangedTag, HashSet<TKey> TagHashSet);

namespace AIS
{
    /// <summary>
    /// The Inventory System handling the functionality of the Inventory.
    /// Changes are updated concurrently via a Channel allowing for async Add and Remove.
    /// The inventory can only be modified through Add/Remove functions.
    /// </summary>
    /// <typeparam name="TKey">Typeparameter to identify the item to be stored (Key of Inventory Dictionary), must implement <see cref="IItem"/>.</typeparam>
    /// <typeparam name="TValue">Typeparameter to store data in the inventory (Value of Inventory Dictionary), must implement <see cref="IInventoryData{TAmount}"/> and have a parameterless constructor.</typeparam>
    /// <typeparam name="TAmount">Typeparameter for the quantity of items, must be a value type.</typeparam>
    /// <typeparam name="TSave">Typeparameter to define the data to be saved, must implement <see cref="ISaveData{TAmount}"/> and have a parameterless constructor.</typeparam>
    public class InventorySystem<TKey, TValue, TAmount, TSave>
        where TKey : IItem
        where TValue : IInventoryData<TAmount>, new()
        where TAmount : struct
        where TSave : ISaveData<TAmount>, new()
    {

        /// <summary>
        /// Event triggered whenever the inventory is updated.
        /// </summary>
        /// <param name="ChangedItem">The set of items that were changed.</param>
        public event UpdatedInventoryHandler<TKey> UpdatedInventory = delegate { };

        /// <summary>
        /// Event triggered whenever items under a specific tag are updated.
        /// </summary>
        /// <param name="ChangedTag">The tag whose items were updated.</param>
        /// <param name="TagHashSet">The set of all items that belong to the updated tag.</param>
        public event UpdatedInventoryTagHandler<TKey> UpdatedInventoryTag = delegate { };

        internal Func<TSave, Dictionary<TKey, TValue>>? VersionControlFunction;
        private readonly AsyncBooleanGate _UpdateLoopGate = new();
        private readonly SemaphoreSlim _SaveLoadSemaphore = new(1, 1);

        /// <summary>
        /// The current inventory mapping items to their values including quantity.
        /// </summary>
        public Dictionary<TKey, TValue> Inventory { get; private set; } = new();

        internal Dictionary<TKey, string> TagLookUpTable = new();
        internal Dictionary<string, HashSet<TKey>> TagHashSet = new();
        private Channel<InventoryChange<TKey, TAmount>> _InventoryChannel = Channel.CreateUnbounded<InventoryChange<TKey, TAmount>>();

        public InventorySystem()
        {
            _ = Task.Run
            (async () => {
                try
                {
                    await ChannelProcessLoop();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Inventory loop error: {ex}");
                }
            });
        }

        private async Task FlushChannel()
        {
            while (_InventoryChannel.Reader.TryRead(out _)) { }

            while (await _InventoryChannel.Reader.WaitToReadAsync())
            {
                while (_InventoryChannel.Reader.TryRead(out _)) { }
            }
        }

        private async Task AwaitCurrentChannelQueue()
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            _InventoryChannel.Writer.TryWrite(new InventoryChange<TKey, TAmount>(
                ChangeList: Array.Empty<KeyValuePair<TKey, TAmount>>(),
                Response: tcs
            ));

            await tcs.Task;
        }

        private async Task ChannelProcessLoop()
        {
            while (await _InventoryChannel.Reader.WaitToReadAsync())
            {
                await _UpdateLoopGate.Wait();

                while (_InventoryChannel.Reader.TryRead(out InventoryChange<TKey, TAmount> Change))
                {
                    bool SuccessfullInventoryChange = false;
                    bool SuccessfullCheckFlag = ItemRemoveCheck(Change);

                    if (SuccessfullCheckFlag)
                    {
                        UpdateValueOfInventory(Change);
                        SuccessfullInventoryChange = true;
                    }

                    Change.Response?.SetResult(SuccessfullInventoryChange);
                }
            }
        }

        private bool ItemRemoveCheck(InventoryChange<TKey, TAmount> Change)
        {
            bool SuccessfulCheckFlag = true;

            if (Change.Response != null)    // Bypass for ForcedAddRemoveItem
            {
                foreach (var kvp in Change.ChangeList)
                {
                    if
                    (
                        Inventory.TryGetValue(kvp.Key, out var dictionarydata)
                    )
                    {
                        if(!dictionarydata.Check(kvp.Value))
                        {
                            SuccessfulCheckFlag = false;
                            break;
                        }
                    }
                    else
                    {
                        SuccessfulCheckFlag = false;
                        break;
                    }
                }
            }

            return SuccessfulCheckFlag;
        }

        private void UpdateValueOfInventory(InventoryChange<TKey, TAmount> Change)
        {
            HashSet<TKey> ChangedItem = new();
            HashSet<string> ChangedTag = new();

            foreach (var kvp in Change.ChangeList)
            {
                ChangedItem.Add(kvp.Key);
                if (TagLookUpTable.TryGetValue(kvp.Key, out var tagdictionaryvalue)) { ChangedTag.Add(tagdictionaryvalue); }
                
                if(!Inventory.ContainsKey(kvp.Key))
                    { Inventory[kvp.Key] = new(); }
                
                Inventory[kvp.Key].Add(kvp.Value);
                if(Inventory[kvp.Key].ZeroCheck())
                    { Inventory.Remove(kvp.Key); }
            }

            UpdatedInventory?.Invoke(ChangedItem);
            foreach (string Tag in ChangedTag) 
            {
                UpdatedInventoryTag?.Invoke(Tag, TagHashSet[Tag]);
            }
        }

        /// <summary>
        /// Checks if an item exists in the inventory.
        /// Does not consider changes still in the InventoryChannel queue.
        /// </summary>
        /// <param name="item">The item to check.</param>
        /// <returns>True if the item exists, false otherwise</returns>
        public bool SingleItemCheck(TKey item)
        {
            return SingleItemCheck(item, default);
        }

        /// <summary>
        /// Checks if an item exists and its quantity is greater than a given amount.
        /// Does not consider changes still in the InventoryChannel queue.
        /// </summary>
        /// <param name="item">The item ID to check.</param>
        /// <param name="CompareAmount">Minimum quantity required.</param>
        /// <returns>True if the quantity is greater than the specified amount.</returns>
        public bool SingleItemCheck(TKey item, TAmount CompareAmount)
        {
            if(Inventory.TryGetValue(item, out var dictionarydata))
            {
                return dictionarydata.Check(CompareAmount);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if all specified items exist in the inventory.
        /// Does not consider changes still in the InventoryChannel queue.
        /// </summary>
        /// <param name="items">List of items to check.</param>
        /// <returns>True if all items exist.</returns>
        public bool MultiItemCheck(List<TKey> item)
        {
            return MultiItemCheck(item, default);
        }

        /// <summary>
        /// Checks if all specified items exist and exceed the given quantity.
        /// Does not consider changes still in the InventoryChannel queue.
        /// </summary>
        /// <param name="item">List of items to check.</param>
        /// <param name="CompareAmount">Minimum quantity required for each item.</param>
        /// <returns>True if all items meet the quantity requirement.</returns>
        public bool MultiItemCheck(List<TKey> item, TAmount CompareAmount)
        {
            return item.All(_item => Inventory.TryGetValue(_item, out var dictionarydata) && dictionarydata.Check(CompareAmount));
        }

        /// <summary>
        /// Checks if multiple items meet their respective minimum quantities.
        /// Does not consider changes still in the InventoryChannel queue.
        /// </summary>
        /// <param name="CheckPair">List of item and quantity pairs.</param>
        /// <returns>True if all items meet the required quantity.</returns>
        public bool MultiItemCheck(List<KeyValuePair<TKey, TAmount>> CheckPair) 
        {
            foreach (var kvp in CheckPair) 
            {
                if(Inventory.TryGetValue(kvp.Key, out var dictionarydata))
                { 
                    if (!dictionarydata.Check(kvp.Value)) 
                        { return false; }
                }
            }
            return true;
        }

        /// <summary>
        /// Adds a single item to the inventory.
        /// Bypasses any Check on if the inventory accepts the change.
        /// </summary>
        /// <param name="ChangeItem">The Item to be changed</param>
        /// <param name="Amount">The amount to add/remove.</param>
        public void ForceAddRemoveItem(TKey ChangeItem, TAmount Amount)
        {
            var ChangeList = new List<KeyValuePair<TKey, TAmount>>
            {
                new(ChangeItem, Amount)
            };

            AddRemoveItem(ChangeList);
        }

        /// <summary>
        /// Adds multiple items to the inventory.
        /// Bypasses any Check on if the inventory accepts the change.
        /// </summary>
        /// <param name="ChangeList">List of item and amount pairs.</param>
        public void AddRemoveItem(List<KeyValuePair<TKey, TAmount>> ChangeList) 
        {
            _InventoryChannel.Writer.TryWrite(new InventoryChange<TKey, TAmount>(ChangeList));
        }

        /// <summary>
        /// Attempts to change a single item from the inventory.
        /// </summary>
        /// <param name="ChangeItem">The item to be changed.</param>
        /// <param name="Amount">The amount to add/remove.</param>
        /// <returns>True if change succeeded</returns>
        public Task<bool> TryAddRemoveItem(TKey ChangeItem, TAmount Amount)
        {
            var ChangeList = new List<KeyValuePair<TKey, TAmount>>
            {
                new(ChangeItem, Amount)
            };

            return TryAddRemoveItem(ChangeList);
        }

        /// <summary>
        /// Attempts to remove multiple items from the inventory asynchronously.
        /// Does not partially change inventory data, if the check on an item fails no inventory data gets changed.
        /// </summary>
        /// <param name="ChangeList">List of item and amount pairs.</param>
        /// <returns>True if all changes succeeded</returns>
        public Task<bool> TryAddRemoveItem(List<KeyValuePair<TKey, TAmount>> ChangeList)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            if(!_InventoryChannel.Writer.TryWrite(new InventoryChange<TKey, TAmount>(ChangeList, tcs)))
            {
                tcs.SetException(new InvalidOperationException("Channel is closed."));
            }

            return tcs.Task;
        }

        /// <summary>
        /// Sets a custom version control function to handle non default save files and/or update old saves to new format.
        /// </summary>
        /// <param name="versionControlFunction">A function taking an old save and returning a current inventory dictionary.</param>
        public void SetVersionControlFunction(Func<TSave, Dictionary<TKey, TValue>> versionControlFunction)
        {
            VersionControlFunction = versionControlFunction;
        }

        /// <summary>
        /// Sets the tag lookup table for items.
        /// </summary>
        /// <param name="ExternalTagLookUpTable">Dictionary mapping items to tags.</param>
        public void SetTagLookUpTable(Dictionary<TKey, string> ExternalTagLookUpTable) 
        {
            TagLookUpTable = ExternalTagLookUpTable;
            SetTagHashSet();
        }

        private void SetTagHashSet() 
        {
            TagHashSet = TagLookUpTable
                .GroupBy(kvp => kvp.Value)
                .ToDictionary(
                    grouped => grouped.Key,
                    grouped => grouped.Select(kvp => kvp.Key).ToHashSet()
                );
        }

        /// <summary>
        /// Saves the inventory to a JSON file asynchronously.
        /// </summary>
        /// <param name="FilePath">The path to the save file.</param>
        public async Task Save(string FilePath)
        {
            await Save(FilePath, Constants.BaseVersion);
        }

        /// <summary>
        /// Saves the inventory to a JSON file with a custom version asynchronously.
        /// </summary>
        /// <param name="FilePath">The path to the save file.</param>
        /// <param name="GameVersion">The version string to save.</param>
        public async Task Save(string FilePath, string GameVersion)
        {
            await _SaveLoadSemaphore.WaitAsync();
            try
            {
                await SaveSubroutine(FilePath, GameVersion);
            }
            finally
            {
                _SaveLoadSemaphore.Release();
            }
        }

        private async Task SaveSubroutine(string FilePath, string GameVersion) 
        {
            await AwaitCurrentChannelQueue();

            Dictionary<IItem, IInventoryData<TAmount>> inventorySnapshot;

            lock (Inventory)
            {
                inventorySnapshot = new Dictionary<IItem, IInventoryData<TAmount>>
                    (Inventory.ToDictionary(
                        kvp => (IItem)kvp.Key,
                        kvp => (IInventoryData<TAmount>)kvp.Value
                    ));
            }
            
            TSave saveData = new();
            saveData.Initialize(GameVersion, inventorySnapshot);

            string TempFilePath = $"{FilePath}.tmp";
            string BackupFilePath = $"{FilePath}.bak";

            var json = JsonSerializer.Serialize(saveData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(TempFilePath, json);
            File.Replace(TempFilePath, FilePath, BackupFilePath);
        }

        /// <summary>
        /// Loads the inventory from a JSON file asynchronously.
        /// </summary>
        /// <param name="FilePath">The path to the save file.</param>
        public async Task Load(string FilePath)
        {
            await _SaveLoadSemaphore.WaitAsync();
            try 
            {
                await LoadSubroutine(FilePath);
            }
            finally 
            {
                _SaveLoadSemaphore.Release();
            }            
        }

        private async Task LoadSubroutine(string FilePath) 
        {
            if (!File.Exists(FilePath))
                throw new FileNotFoundException("Save file not found.", FilePath);

            var json = await File.ReadAllTextAsync(FilePath);
            var saveData = JsonSerializer.Deserialize<TSave>(json);
            if (saveData == null) return;

            await FlushChannel();

            lock (Inventory)
            {
                switch (saveData.Version)
                {
                    case Constants.BaseVersion:
                        Inventory = new Dictionary<TKey, TValue>(saveData.GetInventory()
                            .ToDictionary(
                                kvp => (TKey)kvp.Key,
                                kvp => (TValue)kvp.Value
                            ));
                        break;

                    default:
                        if (VersionControlFunction != null)
                        {
                            Inventory = VersionControlFunction(saveData);
                            break;
                        }
                        throw new NotSupportedException($"Save file version {saveData.Version} is not supported.");
                }
            }
        }

        /// <summary>
        /// Pauses processing of inventory changes in the InventoryChannel.
        /// </summary>
        public void PauseProcessing()
        {
            _UpdateLoopGate.Close();
        }

        /// <summary>
        /// Resumes processing of inventory changes in the InventoryChannel.
        /// </summary>
        public void ResumeProcessing()
        {
            _UpdateLoopGate.Open();
        }
    }
}

namespace AIS.Display 
{
    /// <summary>
    /// Provides a base class for displaying inventory data.
    /// Subscribes to an InventorySystem and exposes virtual methods to react to:
    /// <list type="bullet">
    /// <item><description>Inventory updates</description></item>
    /// <item><description>Tag-specific updates</description></item>
    /// </list>
    /// Derive from this class to implement custom inventory displays.
    /// </summary>
    public class InventoryDisplay<TKey, TValue, TAmount, TSave>
        where TKey : IItem, new()
        where TValue : IInventoryData<TAmount>, new()
        where TAmount : struct
        where TSave : ISaveData<TAmount>, new()
    {
        protected readonly InventorySystem<TKey, TValue, TAmount, TSave> _InventorySystem;

        public InventoryDisplay(InventorySystem<TKey, TValue, TAmount, TSave> inventory)
        {
            _InventorySystem = inventory;

            _InventorySystem.UpdatedInventory += OnInventoryUpdated;
            _InventorySystem.UpdatedInventoryTag += OnInventoryTagUpdated;
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