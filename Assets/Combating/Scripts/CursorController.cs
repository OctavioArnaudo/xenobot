using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for Cursor state (Locked/Visible).
    /// </summary>
    public class CursorController : NetworkBehaviour, IPlayer
    {
        private PlayerController _hub;

        private void Awake()
        {
            _hub = GetComponentInParent<PlayerController>();
            if (_hub != null) Bind(_hub);
        }

        public void Bind(PlayerController hub)
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

        private void Update()
        {
            if (_hub == null || !IsOwner && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) return;

            // Allow Hub to override cursor state if needed (e.g. inventory open)
            // But basic locking is handled here based on focus
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (_hub != null && (IsOwner || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
            {
                 SetCursorState(_hub.cursorLocked);
            }
        }

        public void SetCursorState(bool newState)
        {
            Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !newState;
        }
    }
}
