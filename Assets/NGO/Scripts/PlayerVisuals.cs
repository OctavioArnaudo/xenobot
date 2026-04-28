using Unity.Netcode;
using UnityEngine;

public class PlayerVisuals : NetworkBehaviour
{
    [SerializeField] private ParticleSystem hitFX;
    [SerializeField] private ParticleSystem healFX;

    [Rpc(SendTo.Everyone)]
    public void PlayEffectRpc(int type)
    {
        if (type == 0 && hitFX) hitFX.Play();
        if (type == 1 && healFX) healFX.Play();

        Debug.Log($"Visual effect {type} played on Player {OwnerClientId}");
    }
}
