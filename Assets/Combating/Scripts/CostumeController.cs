using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Crafting.Scripts
{
    /// <summary>
    /// Specialized modular controller for appearance changes.
    /// Handles hiding current visuals and restoring them when removed.
    /// </summary>
    public class CostumeController : MonoBehaviour, IItemFunctional, IPlayer
    {
        [Header("Settings")]
        [Tooltip("Tag to find the render root in the player hierarchy")]
        public string renderTag = "Render";

        private GameObject _modelHiddenByMe;
        private bool _isEquipped = false;
        private PlayerController _hub;

        public void Bind(PlayerController hub)
        {
            _hub = hub;
            if (_hub != null) _hub.RegisterModule(this);
        }

        public void OnRefreshModule() { }

        public void ApplyEffect(GameObject player)
        {
            if (_isEquipped) return;

            if (_hub == null) _hub = player.GetComponent<PlayerController>();

            // 1. Find the target render root
            GameObject renderRoot = (_hub != null && _hub.renderRoot != null)
                ? _hub.renderRoot.gameObject
                : FindChildWithTag(player, renderTag);

            if (renderRoot == null)
            {
                Debug.LogWarning($"[CostumeController] Render root with tag '{renderTag}' not found on {player.name}. Using player root.");
                renderRoot = player;
            }

            // 2. HIDE the previous model if it exists
            if (_hub != null && _hub.activeModel != null)
            {
                _modelHiddenByMe = _hub.activeModel.gameObject;
                _modelHiddenByMe.SetActive(false);
            }

            // 3. Attach and Show myself
            transform.SetParent(renderRoot.transform);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            gameObject.SetActive(true);
            _isEquipped = true;

            // 4. Cleanup modular components
            if (TryGetComponent<PickupController>(out var p)) Destroy(p);
            if (TryGetComponent<Rigidbody>(out var rb)) Destroy(rb);
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;

            if (_hub != null) _hub.RefreshBodyReferences();
        }

        private void OnDestroy()
        {
            if (!_isEquipped) return;

            // RESTORE exactly what this specific costume hid
            if (_modelHiddenByMe != null)
            {
                _modelHiddenByMe.SetActive(true);
            }
        }

        private GameObject FindChildWithTag(GameObject parent, string tag)
        {
            return parent.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.CompareTag(tag))?.gameObject;
        }
    }
}
