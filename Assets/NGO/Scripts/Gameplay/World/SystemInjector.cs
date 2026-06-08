using UnityEngine;
using NGO.Gameplay.Networking;
using System.Linq;

namespace NGO.Gameplay.World
{
    /// <summary>
    /// Componente de mundo que "inyecta" nuevas capacidades en el SystemsHub del jugador.
    /// Puede ser un prefab independiente o el mismo objeto que contiene el script.
    /// </summary>
    public class SystemInjector : MonoBehaviour
    {
        public enum ContainerType { Data, Action, Visual }

        [Header("Configuración de Inyección")]
        [Tooltip("Si está vacío, se inyectará este mismo objeto (Auto-Inyección)")]
        [SerializeField] private GameObject prefabToInstall;

        [Tooltip("En qué pila del SystemsHub se instalará")]
        [SerializeField] private ContainerType targetContainer;

        [Header("Ajustes")]
        [SerializeField] private bool destroyOnUse = false;
        [SerializeField] private bool refreshHubAfterInstall = true;

        [Header("Filtros de Activación")]
        [SerializeField] private string requiredTagOnSelf = "Ground";
        [SerializeField] private bool checkTagOnSelf = false;

        private void OnTriggerEnter(Collider other)
        {
            // Debug para confirmar que el collider funciona
            Debug.Log($"[SystemInjector] Contacto con: {other.name}. ¿Tag correcto?: {gameObject.CompareTag(requiredTagOnSelf)}");

            if (checkTagOnSelf)
            {
                bool selfHasTag = gameObject.CompareTag(requiredTagOnSelf);
                bool parentHasTag = transform.parent != null && transform.parent.CompareTag(requiredTagOnSelf);

                if (!selfHasTag && !parentHasTag)
                {
                    Debug.LogWarning($"[SystemInjector] Tag '{requiredTagOnSelf}' no encontrado en {gameObject.name} ni en su padre.");
                    return;
                }
            }

            // Buscamos el Hub en el jugador local
            var hub = other.GetComponentInChildren<PlayerSystemHub>();

            if (hub != null && hub.IsOwner)
            {
                Install(hub);
            }
        }

        private void Install(PlayerSystemHub hub)
        {
            Debug.Log($"[SystemInjector] Iniciando instalación en Hub de {hub.OwnerClientId}...");

            // Decidimos qué objeto clonar dentro del jugador
            GameObject source = prefabToInstall != null ? prefabToInstall : gameObject;

            Transform parent = null;
            switch (targetContainer)
            {
                case ContainerType.Data: parent = hub.DataContainer; break;
                case ContainerType.Action: parent = hub.ActionContainer; break;
                case ContainerType.Visual: parent = hub.VisualContainer; break;
            }

            if (parent != null)
            {
                Debug.Log($"[SystemInjector] Contenedor '{targetContainer}' encontrado. Instalando módulo...");

                // Evitar duplicados
                if (parent.Find(source.name) != null)
                {
                    Debug.Log($"[SystemInjector] El módulo {source.name} ya está instalado. Cancelando.");
                    return;
                }

                // 1. Instanciar el módulo en el jugador
                GameObject newModule = Instantiate(source, parent);
                newModule.name = source.name;

                // 2. LIMPIEZA
                if (newModule.TryGetComponent<Collider>(out var col)) col.enabled = false;
                if (newModule.TryGetComponent<SystemInjector>(out var oldInjector)) Destroy(oldInjector);

                // 3. Inicializar y Refrescar
                if (refreshHubAfterInstall) hub.RefreshAllModules();

                Debug.Log($"[World] ★ SISTEMA INSTALADO: {newModule.name} en el jugador {hub.OwnerClientId}");

                if (destroyOnUse) Destroy(gameObject);
            }
            else
            {
                Debug.LogError($"[SystemInjector] ¡ERROR! No se encontró el contenedor '{targetContainer}' en el SystemsHub. Revisa los nombres de los hijos.");
            }
        }
    }
}
