using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

namespace Crafting.Scripts
{
    /// <summary>
    /// Componente encargado de gestionar el cambio de apariencia (mesh) del robot.
    /// </summary>
    public class CostumeController : NetworkBehaviour
    {
        [Header("Configuración")]
        public Transform meshRoot;

        private GameObject _activeCostumeInstance;
        private List<GameObject> _originalMeshes = new List<GameObject>();

        public void ApplyCostume(GameObject costumePrefab)
        {
            if (costumePrefab == null) return;

            // Ocultar mallas originales si es la primera vez
            if (_originalMeshes.Count == 0)
            {
                var renderers = GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    // No queremos ocultar elementos de UI o decorativos marcados
                    if (r.gameObject.name.Contains("Xenobot_")) continue;

                    // Solo ocultamos los que están en el mismo nivel o hijos del visual principal
                    // Por simplicidad, ocultamos todos los que no sean de sistema
                    _originalMeshes.Add(r.gameObject);
                    r.enabled = false;
                }
            }

            // Eliminar costume anterior
            if (_activeCostumeInstance != null)
            {
                Destroy(_activeCostumeInstance);
            }

            // Buscar raíz si no existe (intentar encontrar el Animator del player)
            if (meshRoot == null)
            {
                Animator anim = GetComponentInChildren<Animator>();
                meshRoot = anim != null ? anim.transform : transform;
            }

            // Instanciar nuevo mesh
            _activeCostumeInstance = Instantiate(costumePrefab, meshRoot);
            _activeCostumeInstance.transform.localPosition = Vector3.zero;
            _activeCostumeInstance.transform.localRotation = Quaternion.identity;

            // Si el robot tiene su propia escala, la respetamos
            _activeCostumeInstance.transform.localScale = Vector3.one;

            Debug.Log($"[CostumeController] Costume aplicado: {costumePrefab.name}");
        }
    }
}
