using UnityEngine;
using FMODUnity;
using FMOD.Studio;

public class FmodController : MonoBehaviour
{
    // Permite seleccionar el evento de los bancos de FMOD desde el Inspector
    [SerializeField] private EventReference referenciaEvento;

    // Valor actual de alerta, visible en el Inspector
    [SerializeField] private float alertaActual = 0f;

    // Instancia interna que se guarda en la memoria de la escena
    private EventInstance instanciaAmbiente;

    private void Start()
    {
        // Verifica si se asignó un evento válido en el Inspector
        if (!referenciaEvento.IsNull)
        {
            // Instancia el evento a partir de la referencia de los bancos
            instanciaAmbiente = RuntimeManager.CreateInstance(referenciaEvento);

            // Inicia la reproducción en loop del ambiente
            instanciaAmbiente.start();

            // Sincroniza el valor inicial
            CambiarNivelAlerta(alertaActual);

            Debug.Log("FMOD Ambient Event successfully instantiated and started.");
        }
        else
        {
            Debug.LogWarning("FMOD Event Reference is missing in FmodController!");
        }
    }

    // Método para cambiar la mezcla vertical desde otros scripts o triggers
    public void CambiarNivelAlerta(float nuevoValor)
    {
        alertaActual = nuevoValor;

        // Verifica que la instancia en memoria sea válida antes de operar
        if (instanciaAmbiente.isValid())
        {
            // Modifica el parámetro directamente en la instancia activa
            instanciaAmbiente.setParameterByName("Alerta", alertaActual);
            Debug.Log("FMOD Instance Parameter 'Alerta' updated to: " + alertaActual);
        }
    }

    private void OnDestroy()
    {
        // Limpieza de memoria: detiene y libera el evento al cerrar la escena
        if (instanciaAmbiente.isValid())
        {
            instanciaAmbiente.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            instanciaAmbiente.release();
        }
    }
}