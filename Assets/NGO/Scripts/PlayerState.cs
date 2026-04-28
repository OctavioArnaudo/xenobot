using Unity.Netcode;
using UnityEngine;

public class PlayerState : NetworkBehaviour
{
    public NetworkVariable<int> Health = new NetworkVariable<int>(100);
    public NetworkVariable<int> Score = new NetworkVariable<int>(0);
    public NetworkVariable<bool> IsDead = new NetworkVariable<bool>(false);

    public override void OnNetworkSpawn()
    {
        if (IsServer) Health.Value = 100;
    }
}
