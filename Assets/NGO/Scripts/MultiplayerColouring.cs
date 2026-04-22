using UnityEngine;
using Unity.Multiplayer.Center.NetcodeForGameObjectsExample;

[RequireComponent(typeof(Renderer))]
public class MultiplayerColouring : SetColorBasedOnOwnerId
{
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        SetColorBasedOnOwner();
    }

    protected override void OnOwnershipChanged(ulong previous, ulong current)
    {
        SetColorBasedOnOwner();
    }

    void SetColorBasedOnOwner()
    {
        UnityEngine.Random.InitState((int)OwnerClientId);
        GetComponent<Renderer>().material.color = UnityEngine.Random.ColorHSV();
    }
}