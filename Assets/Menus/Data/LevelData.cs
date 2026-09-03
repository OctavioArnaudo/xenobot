using UnityEngine;
using System.Collections.Generic;

namespace Levels.Data
{
    [CreateAssetMenu(fileName = "Level_", menuName = "Levels/Level Data")]
    public class LevelData : ScriptableObject
    {
        [Header("Configuración del Nivel")]
        public string nombreNivel;
        public Sprite miniatura;
        public string escenaNombre;

        [Header("Estadísticas (Sesión Actual)")]
        public string mejorTiempo = "00:00";
        public float mejorTiempoSegundos = float.MaxValue;
        public bool mejorEsVictoria = false;
        public List<string> jugadoresCompletados = new List<string>();

        public void ActualizarRecord(string tiempoFormateado, float tiempoSegundos, bool esVictoria, string jugador = "")
        {
            bool actualizar = false;

            // 1. Si no hay record previo (es el primero)
            if (mejorTiempoSegundos == float.MaxValue || mejorTiempo == "00:00")
            {
                actualizar = true;
            }
            // 2. Si es victoria y el record anterior era derrota -> Sobreescribe siempre
            else if (esVictoria && !mejorEsVictoria)
            {
                actualizar = true;
            }
            // 3. Si ambos son del mismo tipo (ambos victoria o ambos derrota) -> El menor tiempo gana
            else if (esVictoria == mejorEsVictoria)
            {
                if (tiempoSegundos < mejorTiempoSegundos)
                {
                    actualizar = true;
                }
            }
            // Nota: Si es derrota pero ya hay un record de victoria, no se hace nada.

            if (actualizar)
            {
                mejorTiempo = tiempoFormateado;
                mejorTiempoSegundos = tiempoSegundos;
                mejorEsVictoria = esVictoria;
                Debug.Log($"[LevelData] Nuevo record para {nombreNivel}: {tiempoFormateado} (Es Victoria: {esVictoria})");
            }

            // Registrar jugador si fue victoria
            if (esVictoria && !string.IsNullOrEmpty(jugador))
            {
                if (!jugadoresCompletados.Contains(jugador))
                {
                    jugadoresCompletados.Add(jugador);
                }
            }
        }
    }
}