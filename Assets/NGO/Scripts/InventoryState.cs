using Unity.Netcode;
using UnityEngine;
using System;

public struct NetworkItem : INetworkSerializable, IEquatable<NetworkItem>
{
    public int ItemID;
    public int Quantity;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ItemID);
        serializer.SerializeValue(ref Quantity);
    }

    public bool Equals(NetworkItem other) => ItemID == other.ItemID;
}

public class InventoryState : NetworkBehaviour
{
    // Lista sincronizada de items
    public NetworkList<NetworkItem> Items;

    void Awake()
    {
        Items = new NetworkList<NetworkItem>();
    }

    // Métodos utilitarios para el servidor
    public void AddItem(int id, int qty)
    {
        if (!IsServer) return;

        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].ItemID == id)
            {
                var item = Items[i];
                item.Quantity += qty;
                Items[i] = item;
                return;
            }
        }
        Items.Add(new NetworkItem { ItemID = id, Quantity = qty });
    }

    public bool HasItems(int id, int qty)
    {
        foreach (var item in Items)
        {
            if (item.ItemID == id && item.Quantity >= qty) return true;
        }
        return false;
    }

    public void RemoveItem(int id, int qty)
    {
        if (!IsServer) return;

        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].ItemID == id)
            {
                var item = Items[i];
                item.Quantity -= qty;
                if (item.Quantity <= 0) Items.RemoveAt(i);
                else Items[i] = item;
                return;
            }
        }
    }
}
