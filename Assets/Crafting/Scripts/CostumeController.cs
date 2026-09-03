using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

namespace Crafting.Scripts
{
    public class CostumeController : NetworkBehaviour
    {
        [Header("Configuración")]
        public Transform meshRoot;

        // Sincronización mediante el hash del itemCode
        private NetworkVariable<int> _activeCostumeHash = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private int _offlineCostumeHash = 0;

        private GameObject _activeCostumeInstance;
        private List<GameObject> _originalMeshes = new List<GameObject>();

        public override void OnNetworkSpawn()
        {
            _activeCostumeHash.OnValueChanged += (oldVal, newVal) => {
                if (newVal == 0) RestoreDefaultLocal();
                else ApplyCostumeByHash(newVal);
            };

            if (_activeCostumeHash.Value != 0)
            {
                ApplyCostumeByHash(_activeCostumeHash.Value);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestCostumeChangeServerRpc(int itemHash)
        {
            _activeCostumeHash.Value = itemHash;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestRestoreDefaultServerRpc()
        {
            _activeCostumeHash.Value = 0;
        }

        private void ApplyCostumeByHash(int hash)
        {
            var inv = GetComponent<InventoryController>() ?? InventoryController.LocalInstance;
            ItemData item = inv != null ? inv.GetItemDataByHash(hash) : null;

            if (item != null && item.itemPrefab != null)
            {
                ApplyCostumeLocal(item.itemPrefab, hash);
            }
        }

        public void ApplyCostumeLocal(GameObject costumePrefab, int hash = 0)
        {
            if (costumePrefab == null) return;
            if (!IsNetworkActive) _offlineCostumeHash = hash;

            // 1. Ocultar originales
            if (_originalMeshes.Count == 0)
            {
                foreach (var r in GetComponentsInChildren<Renderer>(true))
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

            // 5. Limpieza de componentes lógicos del mesh (para evitar bugs en player)
            if (_activeCostumeInstance.TryGetComponent<PickupController>(out var p)) Destroy(p);
            if (_activeCostumeInstance.TryGetComponent<NetworkObject>(out var no)) Destroy(no);
            if (_activeCostumeInstance.TryGetComponent<Rigidbody>(out var rb)) Destroy(rb);
            foreach (var c in _activeCostumeInstance.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var r in _activeCostumeInstance.GetComponentsInChildren<Renderer>(true)) r.enabled = true;

            // 6. Sincronizar Animator
            if (_activeCostumeInstance.TryGetComponent<Animator>(out var animInstance))
            {
                animInstance.enabled = true;
                var mainAnim = GetComponentInChildren<Animator>();
                if (animInstance.runtimeAnimatorController == null && mainAnim != null)
                    animInstance.runtimeAnimatorController = mainAnim.runtimeAnimatorController;
            }

            GetComponent<Combating.Scripts.PlayerController>()?.RefreshBodyReferences();
        }

        public void RestoreDefaultLocal()
        {
            _offlineCostumeHash = 0;
            if (_activeCostumeInstance != null) Destroy(_activeCostumeInstance);
            foreach(var m in _originalMeshes) if(m != null) m.SetActive(true);
            Debug.Log("[CostumeController] Apariencia original restaurada.");
        }

        public bool IsWearing(int hash)
        {
            return IsNetworkActive ? _activeCostumeHash.Value == hash : _offlineCostumeHash == hash;
        }

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    }
}
