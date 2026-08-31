using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using System.Collections.Generic;
using System.Linq;
using Xenobot.Movement;

public class FmodController : MonoBehaviour
{
    public static FmodController Instance { get; private set; }

    [Header("Configuración de Rol")]
    [Tooltip("Si se marca, este objeto gestiona la instancia de audio de FMOD (Solo debe haber uno en la escena).")]
    [SerializeField] private bool esControladorPrincipal = true;

    [Tooltip("Si se marca, este objeto detecta colisiones para cambiar el nivel de alerta.")]
    [SerializeField] private bool esTrigger = false;

    [Header("Ajustes de Sonido (Solo Principal)")]
    [SerializeField] private EventReference referenciaEvento;
    [SerializeField] private string nombreParametroAlerta = "Alerta";

    [Header("Ajustes de Alerta (Solo Trigger)")]
    [SerializeField] private float nivelAlerta = 50.0f;

    private EventInstance instanciaAmbiente;
    private Dictionary<int, float> alertasActivas = new Dictionary<int, float>();

    private void Awake()
    {
        // Sistema de Singleton para el controlador principal
        if (esControladorPrincipal)
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Debug.LogWarning("[FMOD] Se encontró más de un FmodController principal. Eliminando duplicado.");
                Destroy(gameObject);
                return;
            }
        }
    }

    private void Start()
    {
        if (esControladorPrincipal && !referenciaEvento.IsNull)
        {
            instanciaAmbiente = RuntimeManager.CreateInstance(referenciaEvento);
            instanciaAmbiente.start();
            ActualizarFMOD();
            Debug.Log("[FMOD] Evento de ambiente iniciado.");
        }
    }

    #region Lógica de Trigger
    private void OnTriggerEnter(Collider other)
    {
        if (esTrigger && EsJugadorLocal(other))
        {
            // Reportar la alerta al controlador principal
            if (Instance != null)
                Instance.RegistrarAlerta(gameObject.GetInstanceID(), nivelAlerta);
            else
                Debug.LogWarning("[FMOD] Trigger detectado pero no hay FmodController principal activo.");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (esTrigger && EsJugadorLocal(other))
        {
            if (Instance != null)
                Instance.EliminarAlerta(gameObject.GetInstanceID());
        }
    }

    private bool EsJugadorLocal(Collider other)
    {
        if (!other.CompareTag("Player")) return false;
        var pc = other.GetComponent<PlayerController>();
        return pc != null ? pc.IsOwner : true;
    }
    #endregion

    #region Gestión de Alertas (Solo Procesado en el Principal)
    public void RegistrarAlerta(int idFuente, float valor)
    {
        // Si este componente no es el principal, redirigir a la instancia estática
        if (!esControladorPrincipal)
        {
            if (Instance != null) Instance.RegistrarAlerta(idFuente, valor);
            return;
        }

        if (!alertasActivas.ContainsKey(idFuente))
            alertasActivas.Add(idFuente, valor);
        else
            alertasActivas[idFuente] = valor;

        ActualizarFMOD();
    }

    public void EliminarAlerta(int idFuente)
    {
        if (!esControladorPrincipal)
        {
            if (Instance != null) Instance.EliminarAlerta(idFuente);
            return;
        }

        if (alertasActivas.ContainsKey(idFuente))
        {
            alertasActivas.Remove(idFuente);
            ActualizarFMOD();
        }
    }

    private void ActualizarFMOD()
    {
        if (!esControladorPrincipal || !instanciaAmbiente.isValid()) return;

        // Tomamos el valor máximo de todas las zonas activas
        float valorFinal = alertasActivas.Count > 0 ? alertasActivas.Values.Max() : 0f;
        instanciaAmbiente.setParameterByName(nombreParametroAlerta, valorFinal);
    }
    #endregion

    private void OnDestroy()
    {
        if (esControladorPrincipal && instanciaAmbiente.isValid())
        {
            instanciaAmbiente.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            instanciaAmbiente.release();
        }
    }
}