using Unity.Netcode;
using UnityEngine;

public class NetworkUIAutoHider : MonoBehaviour
{
    [SerializeField] private GameObject uiToHide;

    void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            // Nos suscribimos a los eventos de inicio de red
            NetworkManager.Singleton.OnClientStarted += HideUI;
            NetworkManager.Singleton.OnServerStarted += HideUI;
        }

        // Si por alguna razón ya está iniciado al cargar (escenas persistentes)
        if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
        {
            HideUI();
        }
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientStarted -= HideUI;
            NetworkManager.Singleton.OnServerStarted -= HideUI;
        }
    }

    private void HideUI()
    {
        if (uiToHide != null)
        {
            Debug.Log("[NetworkUIAutoHider] Deactivating UI: " + uiToHide.name);
            uiToHide.SetActive(false);
        }
        else
        {
            // Si no se asignó nada, desactivamos este objeto por defecto
            gameObject.SetActive(false);
        }
    }
}
