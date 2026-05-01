using Unity.Multiplayer.Center.NetcodeForGameObjectsExample;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class MultiplayerCanvas : MonoBehaviour {

    [SerializeField]
    Button m_StartHostButton;
    [SerializeField]
    Button m_StartClientButton;

    void Awake()
    {
        EventSystem existingEventSystem = FindAnyObjectByType<EventSystem>();

        if (existingEventSystem == null)
        {
            var eventSystemGO = new GameObject("EventSystem");
            existingEventSystem = eventSystemGO.AddComponent<EventSystem>();
            eventSystemGO.transform.SetParent(transform);
        }

        // Force replacement of StandaloneInputModule if we are in the new system
#if ENABLE_INPUT_SYSTEM
        StandaloneInputModule oldModule = existingEventSystem.GetComponent<StandaloneInputModule>();
        if (oldModule != null)
        {
            DestroyImmediate(oldModule);
        }

        if (existingEventSystem.GetComponent<InputSystemUIInputModule>() == null)
        {
            existingEventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        }
#else
        if (existingEventSystem.GetComponent<StandaloneInputModule>() == null)
        {
            existingEventSystem.gameObject.AddComponent<StandaloneInputModule>();
        }
#endif
    }

    // Start is called before the first frame update
    void Start()
    {
        m_StartHostButton.onClick.AddListener(StartHost);
        m_StartClientButton.onClick.AddListener(StartClient);
    }

    void StartClient()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager not found in scene!");
            return;
        }

        bool success = NetworkManager.Singleton.StartClient();
        Debug.Log($"StartClient result: {success}");
        if (success) DeactivateButtons();
    }

    void StartHost()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager not found in scene!");
            return;
        }

        bool success = NetworkManager.Singleton.StartHost();
        Debug.Log($"StartHost result: {success}");
        if (success) DeactivateButtons();
        else Debug.LogError("Failed to start Host. Check if Transport is configured and Port is available.");
    }

    void DeactivateButtons()
    {
        m_StartHostButton.interactable = false;
        m_StartClientButton.interactable = false;
    }
}
