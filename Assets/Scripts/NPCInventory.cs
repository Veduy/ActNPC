using System;
using System.Collections.Generic;
using UnityEngine;

public class NPCInventory : MonoBehaviour
{
    [SerializeField] private List<InventoryItem> items = new List<InventoryItem>();

    public IReadOnlyList<InventoryItem> Items => items;

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

        InventoryItem existingItem = FindItemById(itemId);
        if (existingItem != null)
        {
            existingItem.count++;
            return existingItem;
        }

        InventoryItem inventoryItem = new InventoryItem
        {
            itemId = itemId,
            itemName = string.IsNullOrWhiteSpace(item.itemName) ? item.gameObject.name : item.itemName.Trim(),
            count = 1
        };

        items.Add(inventoryItem);
        return inventoryItem;
    }

    public bool ContainsItem(int itemId)
    {
        return FindItemById(itemId) != null;
    }

    public bool ContainsObject(string objectId)
    {
        return TryParseItemId(objectId, out int itemId) && ContainsItem(itemId);
    }

    public InventoryItem[] Snapshot()
    {
        return items.ToArray();
    }

    private InventoryItem FindItemById(int itemId)
    {
        if (itemId <= 0)
        {
            return null;
        }

        foreach (InventoryItem item in items)
        {
            if (item != null && item.itemId == itemId)
            {
                return item;
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

    [Serializable]
    public class InventoryItem
    {
        public int itemId;
        public string itemName;
        public int count;
    }
}
