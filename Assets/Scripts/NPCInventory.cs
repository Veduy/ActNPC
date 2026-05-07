using System;
using System.Collections.Generic;
using UnityEngine;

public class NPCInventory : MonoBehaviour
{
    private readonly List<InventoryStack> itemStacks = new List<InventoryStack>();

    public InventoryItem AddItem(Item item, string fallbackObjectId)
    {
        if (item == null)
        {
            return null;
        }

        int itemId = ResolveItemId(item, fallbackObjectId);
        if (itemId <= 0)
        {
            return null;
        }

        string itemName = string.IsNullOrWhiteSpace(item.itemName) ? item.gameObject.name : item.itemName.Trim();
        InventoryStack stack = FindStackById(itemId);
        if (stack == null)
        {
            stack = new InventoryStack
            {
                itemId = itemId,
                itemName = itemName
            };
            itemStacks.Add(stack);
        }

        stack.items.Add(item);
        return stack.ToSnapshot();
    }

    public bool ContainsItem(int itemId)
    {
        InventoryStack stack = FindStackById(itemId);
        return stack != null && stack.Count > 0;
    }

    public bool ContainsObject(string objectId)
    {
        return TryParseItemId(objectId, out int itemId) && ContainsItem(itemId);
    }

    public bool TryTakeItem(string target, out Item item)
    {
        item = null;

        InventoryStack stack = FindStack(target);
        if (stack == null || stack.Count <= 0)
        {
            return false;
        }

        int lastIndex = stack.items.Count - 1;
        item = stack.items[lastIndex];
        stack.items.RemoveAt(lastIndex);

        if (stack.Count <= 0)
        {
            itemStacks.Remove(stack);
        }

        return item != null;
    }

    public InventoryItem[] Snapshot()
    {
        List<InventoryItem> snapshots = new List<InventoryItem>();
        foreach (InventoryStack stack in itemStacks)
        {
            if (stack != null && stack.Count > 0)
            {
                snapshots.Add(stack.ToSnapshot());
            }
        }

        return snapshots.ToArray();
    }

    private InventoryStack FindStack(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        if (TryParseItemId(target, out int itemId))
        {
            return FindStackById(itemId);
        }

        string normalizedTarget = target.Trim();
        foreach (InventoryStack stack in itemStacks)
        {
            if (stack != null
                && !string.IsNullOrWhiteSpace(stack.itemName)
                && string.Equals(stack.itemName.Trim(), normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                return stack;
            }
        }

        return null;
    }

    private InventoryStack FindStackById(int itemId)
    {
        if (itemId <= 0)
        {
            return null;
        }

        foreach (InventoryStack stack in itemStacks)
        {
            if (stack != null && stack.itemId == itemId)
            {
                return stack;
            }
        }

        return null;
    }

    private static int ResolveItemId(Item item, string fallbackObjectId)
    {
        if (item.itemId > 0)
        {
            return item.itemId;
        }

        if (TryParseItemId(fallbackObjectId, out int fallbackItemId))
        {
            return fallbackItemId;
        }

        return 0;
    }

    private static bool TryParseItemId(string value, out int itemId)
    {
        itemId = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return int.TryParse(value.Trim(), out itemId) && itemId > 0;
    }

    private class InventoryStack
    {
        public int itemId;
        public string itemName;
        public readonly List<Item> items = new List<Item>();

        public int Count => items.Count;

        public InventoryItem ToSnapshot()
        {
            return new InventoryItem
            {
                itemId = itemId,
                itemName = itemName,
                count = Count
            };
        }
    }

    [Serializable]
    public class InventoryItem
    {
        public int itemId;
        public string itemName;
        public int count;
    }
}
