using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "Level_", menuName = "Levels/Level Data")]
public class LevelData : ScriptableObject
{
    [Header("Configuración del Nivel")]
    public string nombreNivel;
    public Sprite miniatura;
    public string escenaNombre;

    [Header("Estadísticas (Sesión Actual)")]
    public string mejorTiempo = "00:00";
    public List<string> jugadoresCompletados = new List<string>();

    public void ActualizarRecord(string tiempo, string jugador = "")
    {
        mejorTiempo = tiempo;
        if (!string.IsNullOrEmpty(jugador) && !jugadoresCompletados.Contains(jugador))
        {
            jugadoresCompletados.Add(jugador);
        }
    }
}
