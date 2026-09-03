using UnityEngine;
using System.Collections.Generic;

namespace Crafting.Scripts
{
    /// <summary>
    /// Specialized modular controller for appearance changes.
    /// Implements IItemFunctional to swap the player's mesh.
    /// This script should be on the costume prefab.
    /// </summary>
    public class CostumeController : MonoBehaviour, IItemFunctional
    {
        [Header("Settings")]
        [Tooltip("Tag to find the render root in the player hierarchy")]
        public string renderTag = "PlayerRender";

        private static List<GameObject> _hiddenMeshes = new List<GameObject>();
        private bool _isEquipped = false;

        public void ApplyEffect(GameObject player)
        {
            if (_isEquipped) return;

            // 1. Find the target render root using the tag
            GameObject renderRoot = FindChildWithTag(player, renderTag);

            if (renderRoot == null)
            {
                Debug.LogWarning($"[CostumeController] No se encontró un objeto con el tag '{renderTag}' en el jugador. Usando la raíz.");
                renderRoot = player;
            }

            // 2. Hide existing meshes in the player (only once)
            // We search in the player, not just the root, to be thorough.
            var allRenderers = player.GetComponentsInChildren<Renderer>(true);
            foreach (var r in allRenderers)
            {
                // Don't hide our own mesh if we're already a child
                if (r.transform.IsChildOf(transform)) continue;

                if (r.gameObject.activeSelf)
                {
                    _hiddenMeshes.Add(r.gameObject);
                    r.gameObject.SetActive(false);
                }
            }

            // 3. Attach myself to the render root
            transform.SetParent(renderRoot.transform);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            // 4. Clean up modular components to avoid bugs
            if (TryGetComponent<PickupController>(out var p)) Destroy(p);
            if (TryGetComponent<Rigidbody>(out var rb)) Destroy(rb);
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;

            _isEquipped = true;
            Debug.Log($"[CostumeController] Chasis '{gameObject.name}' acoplado al tag '{renderTag}'.");

            // 5. Refresh Player (Animators/Camera)
            player.GetComponent<Combating.Scripts.PlayerController>()?.RefreshFunctionalComponents();
        }

        private void OnDestroy()
        {
            if (_isEquipped)
            {
                RestoreOriginals();
            }
        }

        public void RestoreOriginals()
        {
            foreach (var mesh in _hiddenMeshes)
            {
                if (mesh != null) mesh.SetActive(true);
            }
            _hiddenMeshes.Clear();
            _isEquipped = false;
        }

        private GameObject FindChildWithTag(GameObject parent, string tag)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.CompareTag(tag)) return child.gameObject;
            }
            return null;
        }
    }
}
