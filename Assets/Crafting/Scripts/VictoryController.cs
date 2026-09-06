using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;
using Menus.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for Victory Zone detection.
    /// Triggers the Victory Menu for all connected players when any player enters.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    [RequireComponent(typeof(NetworkObject))]
    public class VictoryController : NetworkBehaviour
    {
        private bool _victoryTriggered = false;

        private void Awake()
        {
            var col = GetComponent<BoxCollider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_victoryTriggered) return;

            // Detect if a Player entered the zone
            var player = other.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                // Victory Condition: Must have Energy Source in inventory
                bool hasEnergy = InventoryController.GetBag().ContainsKey("energy_source");

                if (!hasEnergy)
                {
                    Debug.Log("[VictoryController] Player entered but lacks Energy Source.");
                    return;
                }

                _victoryTriggered = true;

                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    // If online, tell the server to trigger victory for everyone
                    TriggerGlobalVictoryServerRpc();
                }
                else
                {
                    // If offline, just show it locally
                    ShowVictoryLocal();
                }
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void TriggerGlobalVictoryServerRpc()
        {
            // The server tells everyone to show the victory screen
            ShowVictoryClientRpc();
        }

        [Rpc(SendTo.Everyone)]
        private void ShowVictoryClientRpc()
        {
            ShowVictoryLocal();
        }

        private void ShowVictoryLocal()
        {
            if (VictoryMenu.Instance != null)
            {
                VictoryMenu.Instance.TriggerVictory();
                Debug.Log("[VictoryController] Victory triggered successfully.");
            }
            else
            {
                Debug.LogWarning("[VictoryController] VictoryMenu Instance not found in scene.");
            }
        }
    }
}
