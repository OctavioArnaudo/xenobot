using UnityEngine;
using Dialogs.Scripts;

namespace Dialogs.Scripts
{
    [AddComponentMenu("Dialogs/Dialog Trigger")]
    public class DialogTrigger : MonoBehaviour
    {
        public string dialogId;
        public bool onlyOnce = true;

        private bool _used = false;

        private void OnTriggerEnter(Collider other)
        {
            if (_used && onlyOnce) return;
            if (!other.CompareTag("Player")) return;

            if (DialogManager.Instance == null)
            {
                Debug.LogWarning("[DialogTrigger] No se encontró DialogManager en la escena.");
                return;
            }

            DialogManager.Instance.ShowDialog(dialogId);
            _used = true;
        }
    }
}