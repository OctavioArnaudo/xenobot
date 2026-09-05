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
        public string renderTag = "Render";

        private List<Renderer> _hiddenByMe = new List<Renderer>();
        private bool _isEquipped = false;

        public void ApplyEffect(GameObject player)
        {
            if (_isEquipped) return;

            // 1. Find the target render root
            GameObject renderRoot = FindChildWithTag(player, renderTag);
            if (renderRoot == null)
            {
                Debug.LogWarning($"[CostumeController] Render root with tag '{renderTag}' not found on {player.name}. Using player root.");
                renderRoot = player;
            }

            // 2. HIDE renderers specifically in the target root
            // Special Rule: We also look for other "PlayerRoot" tagged objects to hide them
            var myRenderers = new HashSet<Renderer>(GetComponentsInChildren<Renderer>(true));
            var targetTransforms = renderRoot.GetComponentsInChildren<Transform>(true);

            foreach (var t in targetTransforms)
            {
                // Hide any previous PlayerRoot or objects with renderers
                if (t.CompareTag("Root") && !t.IsChildOf(transform))
                {
                    foreach(var r in t.GetComponentsInChildren<Renderer>())
                    {
                         if (myRenderers.Contains(r)) continue;
                         r.enabled = false;
                         _hiddenByMe.Add(r);
                    }
                }

                if (t.TryGetComponent<Renderer>(out var rend))
                {
                    if (myRenderers.Contains(rend)) continue;
                    if (rend.enabled)
                    {
                        rend.enabled = false;
                        _hiddenByMe.Add(rend);
                    }
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

            player.GetComponent<PlayerController>()?.RefreshBodyReferences();
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
