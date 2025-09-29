using AIS.SaveLoad;
using System.Text.Json;
using System.Threading.Channels;

internal readonly record struct InventoryChange(
    IReadOnlyList<KeyValuePair<int, int>> ChangeList,
    TaskCompletionSource<bool>? Response = null
);

internal class AsyncBooleanGate
{
    private volatile TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Wait() => _tcs.Task;

    public void Open()
    {
        var tcs = _tcs;
        Task.Run(() => tcs.TrySetResult(true));
    }

    public void Close()
    {
        while (true)
        {
            var tcs = _tcs;
            if (!tcs.Task.IsCompleted || Interlocked.CompareExchange(ref _tcs, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously), tcs) == tcs)
                return;
        }
    }
}

namespace AIS.SaveLoad
{
    /// <summary>
    /// Represents the save data for the inventory system.
    /// Contains the version of the save file and a snapshot of all item quantities.
    /// Use this class when saving or loading inventory to/from JSON files.
    /// </summary>
    public class InventorySaveData
    {
        internal const string BaseVersion = "V1.0";

        /// <summary>
        /// The version of the save data.
        /// </summary>
        public string Version = BaseVersion;

        /// <summary>
        /// A dictionary mapping item IDs to their quantities.
        /// </summary>
        public Dictionary<int, int> Inventory { get; set; } = new();
    }
}


public delegate void UpdatedInventoryHandler(HashSet<int> ChangedID);
public delegate void UpdatedInventoryTagHandler(string ChangedTag, HashSet<int> TagHashSet);

namespace AIS
{
    /// <summary>
    /// The Inventory System handeling the functionality of the Inventory.
    /// Changes updated concurrently via a Channel allowing for async Add and Remove.
    /// private set only allowing modifications with Add/Remove Functions.
    /// public get will not include changes still awaiting to be processed.
    /// </summary>
    public class InventorySystem
    {
        /// <summary>
        /// Event triggered whenever the inventory is updated.
        /// </summary>
        ///  /// <param name="ChangedID">The set of item IDs that were added, removed, or updated.</param>
        public event UpdatedInventoryHandler UpdatedInventory = delegate { };

        /// <summary>
        /// Event triggered whenever items under a specific tag are updated.
        /// </summary>
        /// <param name="ChangedTag">The tag whose items were updated.</param>
        /// <param name="TagHashSet">The set of all item IDs that belong to the updated tag.</param>
        public event UpdatedInventoryTagHandler UpdatedInventoryTag = delegate { };

        internal Func<InventorySaveData, Dictionary<int, int>>? VersionControlFunction;
        private readonly AsyncBooleanGate _UpdateLoopGate = new();
        private readonly SemaphoreSlim _SaveLoadSemaphore = new(1, 1);

        /// <summary>
        /// The current inventory mapping item IDs to their quantities.
        /// </summary>
        public Dictionary<int, int> Inventory { get; private set; } = new();

        internal Dictionary<int, string> TagLookUpTable = new();
        internal Dictionary<string, HashSet<int>> TagHashSet = new();
        private Channel<InventoryChange> _InventoryChannel = Channel.CreateUnbounded<InventoryChange>();

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

            _InventoryChannel.Writer.TryWrite(new InventoryChange(
                ChangeList: Array.Empty<KeyValuePair<int, int>>(),
                Response: tcs
            ));

            await tcs.Task;
        }

        private async Task ChannelProcessLoop()
        {
            while (await _InventoryChannel.Reader.WaitToReadAsync())
            {
                await _UpdateLoopGate.Wait();

                while (_InventoryChannel.Reader.TryRead(out InventoryChange Change))
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

        private bool ItemRemoveCheck(InventoryChange Change)
        {
            bool SuccessfulCheckFlag = true;

            if (Change.Response != null)    // Bypass for add function that cannot remove items
            {
                foreach (var kvp in Change.ChangeList)
                {
                    int CurrentAmount = Inventory.TryGetValue(kvp.Key, out var dictionarydata) ? dictionarydata : 0;
                    int NewAmount = CurrentAmount + kvp.Value;

                    if (NewAmount < 0)
                    {
                        SuccessfulCheckFlag = false;
                        break;
                    }
                }
            }

            return SuccessfulCheckFlag;
        }

        private void UpdateValueOfInventory(InventoryChange Change)
        {
            HashSet<int> ChangedID = new();
            HashSet<string> ChangedTag = new();

            foreach (var kvp in Change.ChangeList)
            {
                ChangedID.Add(kvp.Key);
                if (TagLookUpTable.TryGetValue(kvp.Key, out var tagdictionaryvalue)) { ChangedTag.Add(tagdictionaryvalue); }

                int CurrentAmount = Inventory.TryGetValue(kvp.Key, out var dictionarydata) ? dictionarydata : 0;
                int NewAmount;
                try
                {
                    NewAmount = checked(CurrentAmount + kvp.Value);
                }
                catch (OverflowException)
                {
                    NewAmount = int.MaxValue;
                }

                if (NewAmount <= 0)
                {
                    Inventory.Remove(kvp.Key);
                }
                else
                {
                    Inventory[kvp.Key] = NewAmount;
                }
            }

            UpdatedInventory?.Invoke(ChangedID);
            foreach (string Tag in ChangedTag) 
            {
                UpdatedInventoryTag?.Invoke(Tag, TagHashSet[Tag]);
            }
        }

        /// <summary>
        /// Checks if an item exists in the inventory.
        /// Does not consider changes still in the InventoryChannel queue.
        /// </summary>
        /// <param name="ID">The item ID to check.</param>
        /// <returns>True if the item exists, false otherwise</returns>
        public bool SingleItemCheck(int ID)
        {
            return SingleItemCheck(ID, 0);
        }

        /// <summary>
        /// Checks if an item exists and its quantity is greater than a given amount.
        /// Does not consider changes still in the InventoryChannel queue.
        /// </summary>
        /// <param name="ID">The item ID to check.</param>
        /// <param name="GreaterThan">Minimum quantity required.</param>
        /// <returns>True if the quantity is greater than the specified amount.</returns>
        public bool SingleItemCheck(int ID, int GreaterThan)
        {
            int CurrentAmount = Inventory.TryGetValue(ID, out var dictionarydata) ? dictionarydata : 0;
            return CurrentAmount > GreaterThan;
        }

        /// <summary>
        /// Checks if all specified items exist in the inventory.
        /// Does not consider changes still in the InventoryChannel queue.
        /// </summary>
        /// <param name="ID">List of item IDs to check.</param>
        /// <returns>True if all items exist, false otherwise.</returns>
        public bool MultiItemCheck(List<int> ID)
        {
            return MultiItemCheck(ID, 0);
        }

        /// <summary>
        /// Checks if all specified items exist and exceed the given quantity.
        /// Does not consider changes still in the InventoryChannel queue.
        /// </summary>
        /// <param name="ID">List of item IDs to check.</param>
        /// <param name="GreaterThan">Minimum quantity required for each item.</param>
        /// <returns>True if all items meet the quantity requirement.</returns>
        public bool MultiItemCheck(List<int> ID, int GreaterThan)
        {
            return ID.All(_ID => Inventory.TryGetValue(_ID, out var dictionarydata) && dictionarydata > GreaterThan);
        }

        /// <summary>
        /// Checks if multiple items meet their respective minimum quantities.
        /// Does not consider changes still in the InventoryChannel queue.
        /// </summary>
        /// <param name="CheckPair">List of item ID and quantity pairs.</param>
        /// <returns>True if all items meet the required quantity.</returns>
        public bool MultiItemCheck(List<KeyValuePair<int, int>> CheckPair) 
        {
            foreach (var kvp in CheckPair) 
            {
                int CurrentAmount = Inventory.TryGetValue(kvp.Key, out var dictionarydata) ? dictionarydata : 0;
                if (CurrentAmount < kvp.Value) 
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Adds a single item to the inventory.
        /// </summary>
        /// <param name="ItemID">The ID of the item to add.</param>
        /// <param name="Amount">The amount to add (must be positive).</param>
        public void AddItem(int ItemID, int Amount)
        {
            var ChangeList = new List<KeyValuePair<int, int>>
            {
                new(ItemID, Amount)
            };

            AddItem(ChangeList);
        }

        /// <summary>
        /// Adds multiple items to the inventory.
        /// </summary>
        /// <param name="Change_List">List of item ID and amount pairs (amounts must be positive).</param>
        public void AddItem(List<KeyValuePair<int, int>> ChangeList) 
        {
            if (ChangeList.Any(kvp => kvp.Value <= 0))
                throw new ArgumentOutOfRangeException("AddItem only accepts positive Values, use TryRemoveItem for removing Items from the Inventory.");
        

            _InventoryChannel.Writer.TryWrite(new InventoryChange(ChangeList));
        }

        /// <summary>
        /// Attempts to remove a single item from the inventory asynchronously.
        /// </summary>
        /// <param name="ItemID">The item ID to remove.</param>
        /// <param name="Amount">The amount to remove (must be positive).</param>
        /// <returns>True if removal succeeded, false if not enough items exist.</returns>
        public Task<bool> TryRemoveItem(int ItemID, int Amount)
        {
            var ChangeList = new List<KeyValuePair<int, int>>
            {
                new(ItemID, Amount)
            };

            return TryRemoveItem(ChangeList);
        }

        /// <summary>
        /// Attempts to remove multiple items from the inventory asynchronously.
        /// Does not partially remove items, if any item isnt avialable no items get removed.
        /// </summary>
        /// <param name="Change_List">List of item ID and amount pairs (amounts must be positive).</param>
        /// <returns>True if all removals succeeded, false if any item could not be removed.</returns>
        public Task<bool> TryRemoveItem(List<KeyValuePair<int, int>> ChangeList)
        {
            if (ChangeList.Any(kvp => kvp.Value <= 0))
                throw new ArgumentOutOfRangeException("TryRemoveItem only accepts positive Values, use AddItem for adding Items to the Inventory.");

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var negatedChangeList = ChangeList
                .Select(kvp => new KeyValuePair<int, int>(kvp.Key, -kvp.Value))
                .ToList();

            if(!_InventoryChannel.Writer.TryWrite(new InventoryChange(negatedChangeList, tcs)))
            {
                tcs.SetException(new InvalidOperationException("Channel is closed."));
            }

            return tcs.Task;
        }

        /// <summary>
        /// Sets a custom version control function to convert old save files to the current format.
        /// </summary>
        /// <param name="versionControlFunction">A function taking an old save and returning a current inventory dictionary.</param>
        public void SetVersionControlFunction(Func<InventorySaveData, Dictionary<int, int>> versionControlFunction)
        {
            VersionControlFunction = versionControlFunction;
        }

        /// <summary>
        /// Sets the tag lookup table for items.
        /// </summary>
        /// <param name="ExternalTagLookUpTable">Dictionary mapping item IDs to tags.</param>
        public void SetTagLookUpTable(Dictionary<int, string> ExternalTagLookUpTable) 
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
            await Save(FilePath, InventorySaveData.BaseVersion);
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

            Dictionary<int, int> inventorySnapshot;

            lock (Inventory)
            {
                inventorySnapshot = new Dictionary<int, int>(Inventory);
            }

            var saveData = new InventorySaveData
            {
                Version = GameVersion,
                Inventory = inventorySnapshot
            };

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
            var saveData = JsonSerializer.Deserialize<InventorySaveData>(json);
            if (saveData == null) return;

            await FlushChannel();

            lock (Inventory)
            {
                switch (saveData.Version)
                {
                    case InventorySaveData.BaseVersion:
                        Inventory = new Dictionary<int, int>(saveData.Inventory);
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
    public class InventoryDisplay 
    {
        protected readonly InventorySystem _InventorySystem;

        public InventoryDisplay(InventorySystem inventory)
        {
            _InventorySystem = inventory;

            _InventorySystem.UpdatedInventory += OnInventoryUpdated;
            _InventorySystem.UpdatedInventoryTag += OnInventoryTagUpdated;
        }

        /// <summary>
        /// Called whenever the inventory is updated.
        /// </summary>
        /// <param name="ChangedID">The set of item IDs that were added, removed, or modified.</param>
        protected virtual void OnInventoryUpdated(HashSet<int> ChangedID)
        {

        }

        /// <summary>
        /// Called whenever an Item with a tag in the inventory is updated.
        /// </summary>
        /// <param name="ChangedTag">The tag that was updated.</param>
        /// <param name="TagHashSet">The set of all item IDs under the tag.</param>
        protected virtual void OnInventoryTagUpdated(string ChangedTag, HashSet<int> TagHashSet)
        {

        }
    }
}