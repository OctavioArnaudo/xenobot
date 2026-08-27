using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if CINEMACHINE_3_0_OR_NEWER
using Unity.Cinemachine;
#endif

public class NetworkPlayerLocalSetup : NetworkBehaviour
{
    [Header("Local-only objects")]
    [SerializeField] Camera[] localCameras;
    [SerializeField] AudioListener[] localAudioListeners;
#if ENABLE_INPUT_SYSTEM
    [SerializeField] PlayerInput[] localPlayerInputs;
#endif
#if CINEMACHINE_3_0_OR_NEWER
    [SerializeField] CinemachineBrain[] localCinemachineBrains;
#endif

    void Awake()
    {
        CacheReferences();

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            ApplyLocalState(true);
    }

    public override void OnNetworkSpawn()
    {
        ApplyLocalState(IsOwner);
    }

    public override void OnGainedOwnership()
    {
        ApplyLocalState(true);
    }

    public override void OnLostOwnership()
    {
        ApplyLocalState(false);
    }

    void CacheReferences()
    {
        if (localCameras == null || localCameras.Length == 0)
            localCameras = GetComponentsInChildren<Camera>(true);

        if (localAudioListeners == null || localAudioListeners.Length == 0)
            localAudioListeners = GetComponentsInChildren<AudioListener>(true);

#if ENABLE_INPUT_SYSTEM
        if (localPlayerInputs == null || localPlayerInputs.Length == 0)
            localPlayerInputs = GetComponentsInChildren<PlayerInput>(true);
#endif

#if CINEMACHINE_3_0_OR_NEWER
        if (localCinemachineBrains == null || localCinemachineBrains.Length == 0)
            localCinemachineBrains = GetComponentsInChildren<CinemachineBrain>(true);
#endif
    }

    void ApplyLocalState(bool isLocalOwner)
    {
        CacheReferences();

        for (int i = 0; i < localCameras.Length; i++)
        {
            if (localCameras[i] != null)
            {
                localCameras[i].enabled = isLocalOwner;
                if (!isLocalOwner && localCameras[i].CompareTag("MainCamera"))
                    localCameras[i].tag = "Untagged";
            }
        }

        for (int i = 0; i < localAudioListeners.Length; i++)
        {
            if (localAudioListeners[i] != null)
                localAudioListeners[i].enabled = isLocalOwner;
        }

#if ENABLE_INPUT_SYSTEM
        for (int i = 0; i < localPlayerInputs.Length; i++)
        {
            if (localPlayerInputs[i] == null)
                continue;

            localPlayerInputs[i].enabled = isLocalOwner;
            if (isLocalOwner)
                localPlayerInputs[i].ActivateInput();
            else
                localPlayerInputs[i].DeactivateInput();
        }
#endif

#if CINEMACHINE_3_0_OR_NEWER
        for (int i = 0; i < localCinemachineBrains.Length; i++)
        {
            if (localCinemachineBrains[i] != null)
                localCinemachineBrains[i].enabled = isLocalOwner;
        }
#endif
    }
}
