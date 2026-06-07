using UnityEngine;
using NGO.Gameplay.Base;
using Unity.Netcode;

namespace NGO.Gameplay.Networking
{
    /// <summary>
    /// Script de fin de herencia para el Manager del Juego.
    /// </summary>
    public class GameManagerNetworking : GameManagerBase
    {
        private void Update()
        {
            if (!IsServer || juegoTerminado.Value) return;

            // Lógica del cronómetro
            if (tiempoSincronizado.Value > 0)
            {
                tiempoSincronizado.Value -= Time.deltaTime;
            }
            else
            {
                NotifyGameEndRpc(0); // Empate
            }
        }

        public override void NotifyGameEndRpc(int winnerId)
        {
            juegoTerminado.Value = true;
            ganadorSincronizado.Value = winnerId;
            Debug.Log($"[Game] Fin de partida. Ganador: {winnerId}");
        }
    }
}
