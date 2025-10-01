#Advanced Inventory System

A fully asynchronous, event-driven inventory management system with built-in support for tagging, versioned save/load, and thread-safe updates via channels.

---

- Features:

	- Asynchronous Inventory Changes
	  - All modifications to the inventory (additions/removals) are queued through a channel for sequential, thread-safe processing.

	- Add/Remove Functions 
	  - `AddItem(int itemId, int amount)` – Add a single item.  
	  - `AddItem(List<KeyValuePair<int, int>> changeList)` – Add multiple items.  
	  - `TryRemoveItem(int itemId, int amount)` – Remove a single item asynchronously.  
	  - `TryRemoveItem(List<KeyValuePair<int, int>> changeList)` – Remove multiple items asynchronously.
	  
	  **Removal operations are atomic: if any item cannot be removed then no items are removed**

	- Pause/Resume Processing
	  - `PauseProcessing()` – Temporarily halt inventory update processing.  
	  - `ResumeProcessing()` – Resume inventory update processing.

	- Event-Driven Updates 
	  - `UpdatedInventory(HashSet<int> changedIds)` – Triggered whenever one or more items are added, removed, or updated.  
      - `UpdatedInventoryTag(string changedTag, HashSet<int> tagIds)` – Triggered whenever items under a specific tag are updated.

	- Tagging System 
	  - `SetTagLookUpTable(Dictionary<int, string> externalTagLookup)` – Associate items with tags.  
	  - `TagHashSet` – Read-only internal structure generated from `TagLookUpTable` to efficiently track items by tag.

	- Save/Load with Version Control 
	  - `Save(string filePath)` – Save the current inventory to a JSON file.  
	  - `Save(string filePath, string gameVersion)` – Save with a custom version string.  
	  - `Load(string filePath)` – Load inventory from a JSON file.  
	  - `SetVersionControlFunction(Func<InventorySaveData, Dictionary<int, int>> versionControlFunction)` – Handle legacy save formats.

	- Synchronous Checks
	  Methods to check inventory (does not take into account pending changes in the Channel Queue):
	  - `SingleItemCheck(int id, int greaterThan = 0)`  
	  - `MultiItemCheck(List<int> ids, int greaterThan = 0)`  
	  - `MultiItemCheck(List<KeyValuePair<int, int>> checkPairs)`

- Architecture Overview:

	Core Concepts:

	- InventoryChange: Represents a single inventory modification. Optionally includes a `TaskCompletionSource` to await completion.
	- AsyncBooleanGate: Controls the pause/resume of the asynchronous inventory processing loop.
	- InventorySystem: Main inventory manager handling:
	  - Queueing changes
	  - Updating inventory
	  - Triggering events
	  - Save/load operations
	- InventoryDisplay: Base class for subscribing to inventory events and implementing custom displays.

	Thread-Safety:

	- Writes: Guaranteed thread-safe because all mutations occur exclusively through the channel.
	- Reads: Direct reads from `Inventory` may be slightly stale if there are pending changes in the channel queue — this is intentional.

- Example Usage:

	```csharp
	using AIS;
	using System;
	using System.Collections.Generic;
	using System.Threading.Tasks;

	class Program
	{
		static async Task Main()
		{
			var inventory = new DefaultIntInventory();

			// Add items
			inventory.TryAddRemoveItem(1, 5);
			inventory.TryAddRemoveItem(new List<KeyValuePair<int, int>> {
				new(2, 10),
				new(3, 15)
			});

			// Remove items
			bool successSingle = await inventory.TryAddRemoveItem(1, -3);
			Console.WriteLine($"Single removal success: {successSingle}");

			bool successMulti = await inventory.TryAddRemoveItem(new List<KeyValuePair<int, int>> {
				new(2, -5),
				new(3, -5)
			});
			Console.WriteLine($"Multiple removal success: {successMulti}");
			
			// Save and load
			await inventory.Save("inventory.json");
			await inventory.Load("inventory.json");

			// Pause/resume processing
			inventory.PauseProcessing();
			inventory.ResumeProcessing();
		}
	}
	
	using AIS;
	using AIS.Display;
	using System;
	using System.Collections.Generic;

	public class ConsoleInventoryDisplay : InventoryDisplay
	{
		public ConsoleInventoryDisplay(InventorySystem inventory) : base(inventory)
		{
			// Base constructor subscribes to inventory events
		}

		protected override void OnInventoryUpdated(HashSet<int> ChangedID)
		{
			Console.WriteLine("Inventory updated for items:");
			foreach (var id in ChangedID)
			{
				int quantity = _InventorySystem.Inventory.TryGetValue(id, out var q) ? q : 0;
				Console.WriteLine($"- Item ID {id}: Quantity = {quantity}");
			}
		}

		protected override void OnInventoryTagUpdated(string ChangedTag, HashSet<int> TagHashSet)
		{
			Console.WriteLine($"Tag '{ChangedTag}' updated. Items under this tag:");
			foreach (var id in TagHashSet)
			{
				int quantity = _InventorySystem.Inventory.TryGetValue(id, out var q) ? q : 0;
				Console.WriteLine($"- Item ID {id}: Quantity = {quantity}");
			}
		}
	}