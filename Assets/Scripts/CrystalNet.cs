// Assets/Scripts (fuera de Editor). Debe existir UN objeto "CrystalNet" en la escena con NetworkObject + este componente (Tools/Terrain/18 lo crea).
// Servidor autoritativo, solo primitivos por la red (sin NetworkObject por fragmento):
//   cliente golpea cristal -> DamageRpc -> servidor rompe -> DropRpc (fragmentos en todos) + BreakRpc (clientes quitan el cristal)
//   jugador toca fragmento -> ClaimRpc -> el servidor adjudica al primero -> ClaimedRpc (todos lo quitan; el ganador recibe la gema)
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CrystalNet : NetworkBehaviour
{
    public static CrystalNet I { get; private set; }
    public static bool Active => I != null && I.IsSpawned;

    Crystal[] list = System.Array.Empty<Crystal>();
    readonly HashSet<int> claimed = new HashSet<int>();
    int next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => I = null;

    public override void OnNetworkSpawn()
    {
        I = this; claimed.Clear(); next = 0;
        var root = GameObject.Find("Crystals");
        list = root ? root.GetComponentsInChildren<Crystal>(true) : list;   // mismo orden en todas las maquinas (escena)
        for (int i = 0; i < list.Length; i++) list[i].id = i;
    }

    public override void OnNetworkDespawn() { if (I == this) I = null; }

    public int NextId() => ++next;   // solo servidor

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void DamageRpc(int id, float d) { if ((uint)id < (uint)list.Length && list[id]) list[id].Damage(d); }

    [Rpc(SendTo.NotServer)]
    public void BreakRpc(int id) { if ((uint)id < (uint)list.Length && list[id]) Destroy(list[id].gameObject); }

    [Rpc(SendTo.Everyone)]
    public void DropRpc(int fid, int color, Vector3 p, Quaternion r, Vector3 force) => CrystalPickup.Spawn(fid, color, p, r, force);

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ClaimRpc(int fid, RpcParams rp = default)
    {
        if (claimed.Add(fid)) ClaimedRpc(fid, rp.Receive.SenderClientId);   // el primero gana
    }

    [Rpc(SendTo.Everyone)]
    void ClaimedRpc(int fid, ulong who)
    {
        if (!CrystalPickup.ById.TryGetValue(fid, out var p)) return;
        if (NetworkManager.LocalClientId == who) p.Grant();
        p.Free();
    }
}