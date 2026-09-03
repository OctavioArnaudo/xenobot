using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Crafting.Scripts
{
    /// <summary>
    /// Specialized modular controller for appearance changes.
    /// Handles hiding current visuals and restoring them when removed.
    /// </summary>
    public class CostumeController : MonoBehaviour, IItemFunctional
    {
        [Header("Settings")]
        [Tooltip("Tag to find the render root in the player hierarchy")]
        public string renderTag = "PlayerRender";

        private List<Renderer> _hiddenByMe = new List<Renderer>();
        private bool _isEquipped = false;

        public void ApplyEffect(GameObject player)
        {
            if (_isEquipped) return;

            // 1. Find the target render root
            GameObject renderRoot = FindChildWithTag(player, renderTag);
            if (renderRoot == null) renderRoot = player;

            // 2. HIDE EVERYTHING that is currently visible in the player
            // This includes the original robot AND any other active costumes.
            var allRenderers = player.GetComponentsInChildren<Renderer>(true);
            foreach (var r in allRenderers)
            {
                // Don't hide our own new mesh!
                if (r.transform.IsChildOf(transform)) continue;

                if (r.enabled)
                {
                    r.enabled = false;
                    _hiddenByMe.Add(r);
                }
            }

            // 3. Attach and Show myself
            transform.SetParent(renderRoot.transform);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            SetMeshVisible(true);
            _isEquipped = true;

            // 4. Cleanup modular components
            if (TryGetComponent<PickupController>(out var p)) Destroy(p);
            if (TryGetComponent<Rigidbody>(out var rb)) Destroy(rb);
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;

            player.GetComponent<Combating.Scripts.PlayerController>()?.RefreshFunctionalComponents();
        }

        public void SetMeshVisible(bool visible)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = visible;
            }
        }

        private void OnDestroy()
        {
            if (!_isEquipped) return;

            // RESTORE exactly what this specific costume hid
            foreach (var r in _hiddenByMe)
            {
                if (r != null) r.enabled = true;
            }
            _hiddenByMe.Clear();
        }

        private GameObject FindChildWithTag(GameObject parent, string tag)
        {
            return parent.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.CompareTag(tag))?.gameObject;
        }
    }
}
