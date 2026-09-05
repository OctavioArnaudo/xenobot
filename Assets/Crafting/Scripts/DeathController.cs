using UnityEngine;
using Unity.Netcode;
using UnityEngine.Events;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for character Death events and destruction logic.
    /// </summary>
    public class DeathController : NetworkBehaviour
    {
        [Header("Events")]
        public UnityEvent OnDeath;

        public void Die()
        {
            OnDeath?.Invoke();

            // Check for spawn controller (loot, etc.)
            if (TryGetComponent<SpawnController>(out var sc))
            {
                sc.TriggerDeath();
            }
            else
            {
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned && IsServer)
                {
                    GetComponent<NetworkObject>().Despawn(false);
                    Destroy(gameObject);
                }
                else
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
