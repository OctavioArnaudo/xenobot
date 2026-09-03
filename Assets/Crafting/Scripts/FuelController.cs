using UnityEngine;
using Combating.Scripts;

namespace Crafting.Scripts
{
    /// <summary>
    /// Specialized controller for Fuel logic and visual representation.
    /// Implements IItemFunctional to add fuel to the player's jetpack.
    /// </summary>
    public class FuelController : MonoBehaviour, IItemFunctional
    {
        [Header("Functional Settings")]
        public float fuelAmount = 50f;

        public void ApplyEffect(GameObject player)
        {
            HealthController health = player.GetComponent<HealthController>();
            if (health != null)
            {
                health.AddFuel(fuelAmount);
                Debug.Log($"[FuelController] Añadido {fuelAmount} de combustible al jugador.");
            }
        }
    }
}
