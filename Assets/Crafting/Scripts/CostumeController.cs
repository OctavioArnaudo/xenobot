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
        }

        public void RestoreDefaultLocal()
        {
            if (_activeCostumeInstance != null) Destroy(_activeCostumeInstance);
            foreach(var m in _originalMeshes) if(m != null) m.SetActive(true);
            Debug.Log("[CostumeController] Regresando a apariencia default.");
        }

        public bool IsWearing(int itemId) => _activeCostumeItemId.Value == itemId;
        public bool IsWearingAny() => _activeCostumeItemId.Value != -1;
    }
}
