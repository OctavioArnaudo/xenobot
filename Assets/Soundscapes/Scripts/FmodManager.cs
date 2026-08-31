using UnityEngine;
using FMODUnity;

public class FmodManager : MonoBehaviour {
    [Header("Componente de FMOD")]
    [SerializeField] private StudioEventEmitter musicaEmitter;

    [Header("Configuración del Parámetro")]
    [Tooltip("El nombre exacto del parámetro creado en FMOD Studio")]
    [SerializeField] private string nombreParametro = "Zona trigger";

    // En parámetros etiquetados de FMOD, la primera etiqueta vale 0f y la segunda 1f
    private float indiceChill = 0f;
    private float indiceAccion = 1f;

    private void OnTriggerEnter(Collider other) {
        // Detecta cuando el jugador entra a la zona de peligro
        if (other.CompareTag("Player")) {
            CambiarParametroFMOD(indiceAccion);
        }
    }

    private void OnTriggerExit(Collider other) {
        // Detecta cuando el jugador sale de la zona de peligro y vuelve a estar a salvo
        if (other.CompareTag("Player")) {
            CambiarParametroFMOD(indiceChill);
        }
    }

    private void CambiarParametroFMOD(float valorIndice) {
        if (musicaEmitter != null) {
            // Usamos el método nativo SetParameter enviando el índice de la etiqueta
            musicaEmitter.SetParameter(nombreParametro, valorIndice);
        }
        else {
            Debug.LogWarning("Falta asignar el StudioEventEmitter en el script del Trigger.");
        }
    }
}