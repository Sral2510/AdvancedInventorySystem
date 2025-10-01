# Advanced Inventory System

A fully asynchronous, event-driven inventory management system with built-in support for tagging, versioned save/load, and thread-safe updates via channels.

---

## Features

- **Asynchronous Inventory Changes**
  - All modifications to the inventory (additions/removals) are queued through a channel for sequential, thread-safe processing.

- **Unified Add/Remove Function**
  - `TryAddRemove(int itemId, int amount)` – Add (positive amount) or remove (negative amount) a single item asynchronously.  
  - `TryAddRemove(List<KeyValuePair<int, int>> changeList)` – Add/remove multiple items asynchronously.  
  **All removals are atomic:** if any removal fails, none are applied.

- **Force Operations**
  - `ForceAddRemoveItem(int itemId, int amount)` bypasses checks.

- **Pause/Resume Processing**
  - `PauseProcess()` – Temporarily halt inventory update processing.  
  - `ContinueProcess()` – Resume inventory update processing.

- **Event-Driven Updates**
  - `UpdatedInventory(HashSet<DefaultItem> changedItems)` – Triggered whenever one or more items are added, removed, or updated.  
  - `UpdatedInventoryTag(string changedTag, HashSet<DefaultItem> taggedItems)` – Triggered whenever items under a specific tag are updated.  
  Note: For tag events to work, your custom item type (like in `DefaultItem`) must implement correct `Equals`/`GetHashCode`.

- **Tagging System**
  - `SetTagLookUpTable(Dictionary<int, string> externalTagLookup)` – Associate items with tags by ID.  
  - Internally builds efficient `HashSet`s for tag-based lookups.

- **Save/Load with Version Control**
  - `Save(string filePath)` – Save the current inventory to a JSON file.  
  - `Load(string filePath)` – Load inventory from a JSON file.  
  - Legacy saves can be handled by setting a custom version control function (not implemented in DefaultIntInventory).

- **Synchronous Checks**
  (Does not consider pending channel changes.)
  - `SingleItemCheck(int id, int greaterThan = 0)`  
  - `MultiItemCheck(List<int> ids, int greaterThan = 0)`  
  - `MultiItemCheck(List<KeyValuePair<int, int>> checkPairs)`

---

## Example Usage

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
        await inventory.TryAddRemove(1001, 5);
        await inventory.TryAddRemove(new List<KeyValuePair<int, int>> {
            new(1002, 10),
            new(1003, 15)
        });

        // Remove items
        bool successSingle = await inventory.TryAddRemove(1001, -3);
        Console.WriteLine($"Single removal success: {successSingle}");

        bool successMulti = await inventory.TryAddRemove(new List<KeyValuePair<int, int>> {
            new(1002, -5),
            new(1003, -5)
        });
        Console.WriteLine($"Multiple removal success: {successMulti}");
        
        // Query quantities
        Console.WriteLine($"Item 1001 count: {inventory.GetQuantity(1001)}");

        // Save and load
        await inventory.Save("inventory.json");
        await inventory.Load("inventory.json");

        // Pause/resume processing
        inventory.PauseProcess();
        inventory.ContinueProcess();
    }
}

public class ConsoleIntInventoryDisplay : DefaultIntInventoryDisplay
{
    public ConsoleIntInventoryDisplay(DefaultIntInventory inventory) : base(inventory) { }

    public override void OnInventoryUpdated(HashSet<DefaultItem> ChangedItems)
    {
        Console.WriteLine("Inventory updated for items:");
        foreach (var item in ChangedItems)
        {
            int quantity = _inventorySystem.GetQuantity(item);
            Console.WriteLine($"- Item ID {item.ID}: Quantity = {quantity}");
        }
    }

    public override void OnInventoryTagUpdated(string ChangedTag, HashSet<DefaultItem> TaggedItems)
    {
        Console.WriteLine($"Tag '{ChangedTag}' updated. Items under this tag:");
        foreach (var item in TaggedItems)
        {
            int quantity = _inventorySystem.GetQuantity(item);
            Console.WriteLine($"- Item ID {item.ID}: Quantity = {quantity}");
        }
    }
}
