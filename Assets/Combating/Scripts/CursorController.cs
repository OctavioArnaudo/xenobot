using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public class CursorController : NetworkBehaviour, IModular
    {
        private ModularController _hub;

        private void Awake()
        {
            _hub = GetComponentInParent<ModularController>();
            if (_hub != null) Bind(_hub);
        }

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null) _hub.RegisterModule(this);
        }

        public void OnRefreshModule() { }

        private void Start()
        {
            if (IsOwner || (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening))
            {
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "BiomaScene")
                    SetCursorState(true);
                else
                    SetCursorState(false);
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (_hub != null && _hub is PlayerController player && (IsOwner || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
            {
                 SetCursorState(player.cursorLocked);
            }
        }

        public void SetCursorState(bool newState)
        {
            Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !newState;
        }
    }
}
