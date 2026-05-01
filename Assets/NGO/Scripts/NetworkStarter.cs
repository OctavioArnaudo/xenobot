using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class NetworkStarter : MonoBehaviour
{
    void Update()
    {
        // No verificamos IsOwner porque este script suele vivir en la escena,
        // no en un objeto de red del jugador.

        if (NetworkManager.Singleton == null) return;

        // Si ya estamos conectados como servidor, host o cliente, no hacemos nada
        if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient) return;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            // Z para iniciar como HOST (Servidor + Cliente local)
            if (Keyboard.current.zKey.wasPressedThisFrame)
            {
                StartHost();
            }

            // X para iniciar como CLIENTE
            if (Keyboard.current.xKey.wasPressedThisFrame)
            {
                StartClient();
            }
        }
#else
        if (Input.GetKeyDown(KeyCode.Z))
        {
            StartHost();
        }

        if (Input.GetKeyDown(KeyCode.X))
        {
            StartClient();
        }
#endif
    }

    private void StartHost()
    {
        bool success = NetworkManager.Singleton.StartHost();
        Debug.Log($"[NetworkStarter] Start Host attempt: {(success ? "SUCCESS" : "FAILED")}");
    }

    private void StartClient()
    {
        bool success = NetworkManager.Singleton.StartClient();
        Debug.Log($"[NetworkStarter] Start Client attempt: {(success ? "SUCCESS" : "FAILED")}");
    }
}
