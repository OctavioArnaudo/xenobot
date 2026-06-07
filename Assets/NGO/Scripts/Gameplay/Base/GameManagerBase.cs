using Unity.Netcode;
using UnityEngine;

namespace NGO.Gameplay.Base
{
    /// <summary>
    /// Script hipotético/base que define la estructura del juego.
    /// Contiene las variables de red pero sin la lógica de actualización pesada.
    /// </summary>
    public abstract class GameManagerBase : NetworkBehaviour
    {
        public NetworkVariable<float> tiempoSincronizado = new NetworkVariable<float>(180f);
        public NetworkVariable<bool> juegoTerminado = new NetworkVariable<bool>(false);
        public NetworkVariable<int> ganadorSincronizado = new NetworkVariable<int>(0);

        [Rpc(SendTo.Everyone)]
        public virtual void NotifyGameEndRpc(int winnerId) { }
    }
}
