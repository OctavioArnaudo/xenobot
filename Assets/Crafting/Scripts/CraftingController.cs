using UnityEngine;
using Combating.Scripts;

namespace Crafting.Scripts
{
    /// <summary>
    /// Attached to a Crafting Station object.
    /// Detects when a player enters its trigger zone to open the Crafting UI.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class CraftingController : MonoBehaviour
    {
        private void Awake()
        {
            // Ensure the collider is a trigger
            var col = GetComponent<BoxCollider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            // Try to find a PlayerController in the object or its parents
            var player = other.GetComponentInParent<PlayerController>();

            // We only want to open the UI for the local player (the one who actually entered on this machine)
            if (player != null && player == PlayerController.LocalInstance)
            {
                if (CraftingManager.Instance != null)
                {
                    CraftingManager.Instance.SetOpen(true);
                    Debug.Log($"[CraftingController] Player entered crafting zone. Opening UI.");
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            var player = other.GetComponentInParent<PlayerController>();

            if (player != null && player == PlayerController.LocalInstance)
            {
                if (CraftingManager.Instance != null)
                {
                    CraftingManager.Instance.SetOpen(false);
                    Debug.Log($"[CraftingController] Player exited crafting zone. Closing UI.");
                }
            }
        }
    }
}
