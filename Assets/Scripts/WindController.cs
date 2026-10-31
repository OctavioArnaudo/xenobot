using UnityEngine;
using Unity.Netcode;
using FMODUnity;
using FMOD.Studio;

namespace Runtime.Scripts.Wind
{
    public class WindController : NetworkBehaviour
    {
        [SerializeField] private EventReference windSfx;
        private EventInstance _windInstance;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _windInstance = RuntimeManager.CreateInstance(windSfx);
            _windInstance.start();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _windInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            _windInstance.release();
        }
    }
}