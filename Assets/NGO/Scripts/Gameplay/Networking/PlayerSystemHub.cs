using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;
using NGO.Gameplay.Base;

namespace NGO.Gameplay.Networking
{
    /// <summary>
    /// Ubicado en el objeto hijo "SystemsHub".
    /// Centraliza la gestión de módulos de datos, acciones y visuales.
    /// </summary>
    public class PlayerSystemHub : NetworkBehaviour
    {
        [Header("Configuración de Pilas")]
        [Tooltip("Si se dejan vacíos, se buscarán hijos con nombres estándar")]
        [SerializeField] private Transform dataSystemsContainer;
        [SerializeField] private Transform actionSystemsContainer;
        [SerializeField] private Transform visualSystemsContainer;

        // Propiedades públicas para que otros scripts accedan a los contenedores
        public Transform DataContainer => dataSystemsContainer;
        public Transform ActionContainer => actionSystemsContainer;
        public Transform VisualContainer => visualSystemsContainer;

        private List<PlayerActionController> m_ActionModules = new List<PlayerActionController>();
        private List<PlayerDataController> m_DataModules = new List<PlayerDataController>();

        public override void OnNetworkSpawn()
        {
            // Auto-asignación por nombre si están vacíos
            ValidateContainers();
            RefreshAllModules();
        }

        private void ValidateContainers()
        {
            if (dataSystemsContainer == null) dataSystemsContainer = transform.Find("DataSystems");
            if (actionSystemsContainer == null) actionSystemsContainer = transform.Find("ActionSystems");
            if (visualSystemsContainer == null) visualSystemsContainer = transform.Find("VisualSystems");
        }

        public void RefreshAllModules()
        {
            m_DataModules.Clear();
            m_ActionModules.Clear();

            // Buscamos en toda la jerarquía de hijos de este objeto y del Root
            var dataMods = transform.parent.GetComponentsInChildren<PlayerDataController>(true);
            var actionMods = transform.parent.GetComponentsInChildren<PlayerActionController>(true);

            foreach (var mod in dataMods)
            {
                mod.Initialize(NetworkObject);
                m_DataModules.Add(mod);
            }

            foreach (var mod in actionMods)
            {
                mod.Initialize(NetworkObject);
                m_ActionModules.Add(mod);
            }

            Debug.Log($"[SystemsHub] {OwnerClientId} inicializado: {m_DataModules.Count} Datos, {m_ActionModules.Count} Acciones.");
        }

        public void TriggerAction()
        {
            if (!IsOwner) return;
            foreach (var mod in m_ActionModules)
            {
                if (mod.isEnabled) mod.OnActionTriggered();
            }
        }

        private void Update()
        {
            if (!IsOwner) return;

            // Ejecutamos el Tick de todos los módulos de acción
            foreach (var mod in m_ActionModules)
            {
                if (mod.isEnabled) mod.OnTick();
            }
        }

        /// <summary>
        /// Busca un módulo específico por tipo en las pilas de sistemas.
        /// </summary>
        public T GetModule<T>() where T : Component
        {
            return GetComponentInChildren<T>();
        }

        /// <summary>
        /// Elimina un módulo por nombre y refresca el hub.
        /// Útil para zonas de "limpieza" o pérdida de poderes.
        /// </summary>
        public void RemoveModule(string moduleName)
        {
            // Buscamos entre todos los hijos (incluyendo sub-contenedores)
            var modules = GetComponentsInChildren<Transform>(true);
            var target = modules.FirstOrDefault(t => t.name == moduleName);

            if (target != null && target != transform)
            {
                Destroy(target.gameObject);
                // Usamos un pequeño delay o esperamos al final del frame para refrescar
                // o simplemente refrescamos después de Destroy
                RefreshAllModules();
                Debug.Log($"[SystemsHub] Módulo '{moduleName}' eliminado.");
            }
        }

        // Método de utilidad para obtener el contenedor visual desde cualquier parte
        public Transform GetVisualContainer() => visualSystemsContainer;
    }
}