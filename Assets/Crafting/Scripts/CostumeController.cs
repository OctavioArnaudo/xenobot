using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

namespace Crafting.Scripts
{
    /// <summary>
    /// Componente encargado de gestionar el cambio de apariencia (mesh) del robot.
    /// Soporta sincronización por red y fallbacks para modo offline.
    /// </summary>
    public class CostumeController : NetworkBehaviour
    {
        [Header("Configuración")]
        public Transform meshRoot;

        // Variable para sincronizar el ID del ítem que otorga el traje
        private NetworkVariable<int> _activeCostumeItemId = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private GameObject _activeCostumeInstance;
        private List<GameObject> _originalMeshes = new List<GameObject>();

        public override void OnNetworkSpawn()
        {
            _activeCostumeItemId.OnValueChanged += (oldVal, newVal) => {
                if (newVal != 0) ApplyCostumeById(newVal);
            };

            // Aplicar si ya hay uno activo al entrar
            if (_activeCostumeItemId.Value != 0)
            {
                ApplyCostumeById(_activeCostumeItemId.Value);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestCostumeChangeServerRpc(int itemId)
        {
            _activeCostumeItemId.Value = itemId;
        }

        private void ApplyCostumeById(int itemId)
        {
            // Intentar encontrar el item por ID
            var inv = GetComponent<InventoryController>();
            if (inv == null) inv = InventoryController.LocalInstance;

            ItemData item = null;
            if (inv != null)
            {
                item = inv.GetItemDataById(itemId);
            }

            if (item != null && item.worldPrefab != null)
            {
                ApplyCostumeLocal(item.worldPrefab);
            }
            else
            {
                Debug.LogWarning($"[CostumeController] No se pudo encontrar el ItemData para ID {itemId}");
            }
        }

        public void ApplyCostumeLocal(GameObject costumePrefab)
        {
            if (costumePrefab == null) return;

            Debug.Log($"[CostumeController] Aplicando costume: {costumePrefab.name}");

            // 1. Inicializar referencias si es la primera vez
            if (_originalMeshes.Count == 0)
            {
                // Buscamos todos los renderers en el objeto y sus hijos
                var renderers = GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    // Ignorar elementos de sistema (identidad, HUD flotante, etc.)
                    if (r.gameObject.name.Contains("Xenobot_")) continue;

                    _originalMeshes.Add(r.gameObject);
                    r.enabled = false; // Desactivar el renderizado del original
                }
            }
            else
            {
                // Asegurarse de que los originales sigan ocultos si se cambia de traje
                foreach(var m in _originalMeshes) if(m != null) m.SetActive(false);
            }

            // 2. Limpiar traje anterior
            if (_activeCostumeInstance != null)
            {
                Destroy(_activeCostumeInstance);
            }

            // 3. Determinar raíz de instanciación
            // Si no hay meshRoot, buscamos el Animator o el centro del objeto
            if (meshRoot == null)
            {
                Animator anim = GetComponentInChildren<Animator>();
                meshRoot = anim != null ? anim.transform : transform;
            }

            // 4. Instanciar y configurar
            _activeCostumeInstance = Instantiate(costumePrefab, meshRoot);
            _activeCostumeInstance.transform.localPosition = Vector3.zero;
            _activeCostumeInstance.transform.localRotation = Quaternion.identity;

            // Forzar escala normalizada (muchos FBX requieren esto al ser instanciados como hijos)
            _activeCostumeInstance.transform.localScale = Vector3.one;

            // Asegurar que las mallas del nuevo traje estén activas
            foreach (var r in _activeCostumeInstance.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true;
            }

            _activeCostumeInstance.SetActive(true);
        }
    }
}
