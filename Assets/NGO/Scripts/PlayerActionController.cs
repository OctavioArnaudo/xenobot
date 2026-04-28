using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PlayerActionController : NetworkBehaviour
{
    private NetworkActionManager actionManager;

    void Start()
    {
        actionManager = Object.FindFirstObjectByType<NetworkActionManager>();
    }

    void Update()
    {
        if (!IsOwner) return;

        // Re-find action manager if it was lost or not found at start
        if (actionManager == null)
        {
            actionManager = Object.FindFirstObjectByType<NetworkActionManager>();
        }

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.qKey.wasPressedThisFrame)
            {
                RequestAttack();
            }
            if (Keyboard.current.eKey.wasPressedThisFrame)
            {
                RequestHeal();
            }
        }
#else
        if (Input.GetKeyDown(KeyCode.Q))
        {
            RequestAttack();
        }
        if (Input.GetKeyDown(KeyCode.E))
        {
            RequestHeal();
        }
#endif
    }

    private void RequestAttack()
    {
        if (actionManager != null)
        {
            actionManager.RequestActionServerRpc(0, transform.position + transform.forward);
            Debug.Log("Requested Attack Action");
        }
    }

    private void RequestHeal()
    {
        if (actionManager != null)
        {
            actionManager.RequestActionServerRpc(1, transform.position);
            Debug.Log("Requested Heal Action");
        }
    }

    public override void OnNetworkSpawn()
    {
        if (actionManager != null)
        {
            actionManager.ActiveActions.OnListChanged += OnActionsChanged;
        }
    }

    private void OnActionsChanged(NetworkListEvent<NetworkActionManager.ActionData> changeEvent)
    {
        // READ: See what others are doing
        switch (changeEvent.Type)
        {
            case NetworkListEvent<NetworkActionManager.ActionData>.EventType.Add:
                Debug.Log($"Client: Player {changeEvent.Value.InstigatorId} started action {changeEvent.Value.ActionType}");
                break;
            case NetworkListEvent<NetworkActionManager.ActionData>.EventType.Remove:
                Debug.Log($"Client: Action {changeEvent.Value.ActionId} finished");
                break;
        }
    }
}
