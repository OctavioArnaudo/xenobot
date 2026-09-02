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

        // Variable para sincronizar el ID del ítem que otorga el traje (-1 = Ninguno/Default)
        private NetworkVariable<int> _activeCostumeItemId = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private GameObject _activeCostumeInstance;
        private List<GameObject> _originalMeshes = new List<GameObject>();

        public override void OnNetworkSpawn()
        {
            _activeCostumeItemId.OnValueChanged += (oldVal, newVal) => {
                if (newVal == -1) RestoreDefaultLocal();
                else ApplyCostumeById(newVal);
            };

            if (_activeCostumeItemId.Value != -1)
            {
                ApplyCostumeById(_activeCostumeItemId.Value);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestCostumeChangeServerRpc(int itemId)
        {
            _activeCostumeItemId.Value = itemId;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestRestoreDefaultServerRpc()
        {
            _activeCostumeItemId.Value = -1;
        }

        private void ApplyCostumeById(int itemId)
        {
            var inv = GetComponent<InventoryController>();
            if (inv == null) inv = InventoryController.LocalInstance;

            ItemData item = null;
            if (inv != null) item = inv.GetItemDataById(itemId);

            if (item != null && item.worldPrefab != null)
            {
                ApplyCostumeLocal(item.worldPrefab);
            }
        }

        public void ApplyCostumeLocal(GameObject costumePrefab)
        {
            if (costumePrefab == null) return;

            // 1. Ocultar originales
            if (_originalMeshes.Count == 0)
            {
                var renderers = GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    if (r.gameObject.name.Contains("Xenobot_")) continue;
                    _originalMeshes.Add(r.gameObject);
                }
            }

            foreach(var m in _originalMeshes) if(m != null) m.SetActive(false);

            // 2. Limpiar anterior
            if (_activeCostumeInstance != null) Destroy(_activeCostumeInstance);

            // 3. Raíz
            if (meshRoot == null)
            {
                Animator anim = GetComponentInChildren<Animator>();
                meshRoot = anim != null ? anim.transform : transform;
            }

            // 4. Instanciar
            _activeCostumeInstance = Instantiate(costumePrefab, meshRoot);
            _activeCostumeInstance.transform.localPosition = Vector3.zero;
            _activeCostumeInstance.transform.localRotation = Quaternion.identity;
            _activeCostumeInstance.transform.localScale = Vector3.one;
            _activeCostumeInstance.SetActive(true);

            // CRITICAL FIX: Limpiar componentes de lógica de mundo del nuevo mesh
            // Si el prefab tiene PickupController, Rigidbody o Colliders, se activarán
            // en el jugador causando bugs de colisión o auto-recolección (duplicación).

            var pickup = _activeCostumeInstance.GetComponent<PickupController>();
            if (pickup != null) Destroy(pickup);

            var netObj = _activeCostumeInstance.GetComponent<NetworkObject>();
            if (netObj != null) Destroy(netObj);

            var rb = _activeCostumeInstance.GetComponent<Rigidbody>();
            if (rb != null) Destroy(rb);

            // Desactivar colliders para que no interfieran con el movimiento del player
            foreach (var c in _activeCostumeInstance.GetComponentsInChildren<Collider>(true))
            {
                c.enabled = false;
            }

            // Asegurar visibilidad
            foreach (var r in _activeCostumeInstance.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true;
            }

            if (_activeCostumeInstance.TryGetComponent<Animator>(out var animInstance))
            {
                animInstance.enabled = false;
            }
        }

        public void RestoreDefaultLocal()
        {
            if (_activeCostumeInstance != null) Destroy(_activeCostumeInstance);
            foreach(var m in _originalMeshes) if(m != null) m.SetActive(true);
            Debug.Log("[CostumeController] Regresando a apariencia default.");
        }

        public bool IsWearing(int itemId) => _activeCostumeItemId.Value == itemId;
    }
}
