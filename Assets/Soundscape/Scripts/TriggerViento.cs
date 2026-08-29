using UnityEngine;
using FMODUnity;
using FMOD.Studio;

public class TriggerViento : MonoBehaviour
{
    [SerializeField] EventReference sfxViento;
    EventInstance _viento;

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _viento = RuntimeManager.CreateInstance(sfxViento);
        _viento.start();
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _viento.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        _viento.release();
    }
}