using UnityEngine;
using FMODUnity;
using FMOD.Studio;

namespace Runtime.Scripts.Wind
{
    public class WindController : MonoBehaviour
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