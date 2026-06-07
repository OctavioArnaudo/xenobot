using Unity.Netcode;
using UnityEngine;

namespace NGO.Gameplay.Base
{
    /// <summary>
    /// Script hipotético/base que define la estructura de salud.
    /// Solo mantiene la sincronización de la variable.
    /// </summary>
    public abstract class HealthBase : NetworkBehaviour
    {
        public NetworkVariable<int> health = new NetworkVariable<int>(100);

        [Rpc(SendTo.Server)]
        public virtual void ModifyHealthRpc(int amount) { }
    }
}
