using UnityEngine;

public class FmodTrigger : MonoBehaviour
{
    [SerializeField] private FmodController controlador;
    [SerializeField] private float alerta = 50.0f; // Valor de alerta para esta zona

    private void Start()
    {
        // Busca automáticamente el controlador si no está asignado
        if (controlador == null)
        {
            controlador = Object.FindAnyObjectByType<FmodController>();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Verifica si el objeto que entra es el Jugador (Xeno-bot)
        if (other.CompareTag("Player") && controlador != null)
        {
            controlador.CambiarNivelAlerta(alerta);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        // Regresa el ambiente al valor base (0) al salir de la zona
        if (other.CompareTag("Player") && controlador != null)
        {
            controlador.CambiarNivelAlerta(0.0f);
        }
    }
}