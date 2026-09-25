using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Combating.Scripts;

namespace Crafting.Scripts
{
    /// <summary>
    /// Specialized modular controller for appearance changes.
    /// Handles hiding current visuals and restoring them when removed.
    /// </summary>
    public class CostumeController : MonoBehaviour, IItemFunctional
    {
        [Header("Settings")]
        [Tooltip("Tag to find the render root in the player hierarchy")]
        public string renderTag = "Render";

        private GameObject _modelHiddenByMe;
        private bool _isEquipped = false;

        public void OnRefreshModule() { }

        public void ApplyEffect(GameObject player)
        {
            if (_isEquipped) return;

            // 1. Identificar el cuerpo original del Player (el objeto con tag Render)
            // Priorizamos el activeModel del hub si existe, si no buscamos por tag
            GameObject playerOriginalBody = FindChildWithTag(player, renderTag);

            // 2. Identificar mi propio cuerpo (el FBX del costume con tag Render)
            GameObject myCostumeBody = FindChildWithTag(gameObject, renderTag);

            if (playerOriginalBody == null)
            {
                Debug.LogWarning($"[CostumeController] No se encontró el cuerpo original con tag '{renderTag}' en el Player.");
                return;
            }

            // 3. OCULTAR el cuerpo original y guardar referencia para restaurar
            _modelHiddenByMe = playerOriginalBody;

            // --- Sincronización de Animaciones ---
            Animator oldAnim = playerOriginalBody.GetComponentInChildren<Animator>();
            Animator newAnim = (myCostumeBody != null) ? myCostumeBody.GetComponentInChildren<Animator>() : GetComponentInChildren<Animator>();

            if (oldAnim != null && newAnim != null)
            {
                // Copiar el controlador para que el nuevo FBX use la misma lógica de estados
                newAnim.runtimeAnimatorController = oldAnim.runtimeAnimatorController;

                // Sincronizar el estado actual de la capa base para que no haya salto visual
                var stateInfo = oldAnim.GetCurrentAnimatorStateInfo(0);
                newAnim.Play(stateInfo.fullPathHash, 0, stateInfo.normalizedTime);

                // Copiar parámetros básicos (Speed, Grounded, etc)
                foreach (var param in oldAnim.parameters)
                {
                    if (param.type == AnimatorControllerParameterType.Float)
                        newAnim.SetFloat(param.nameHash, oldAnim.GetFloat(param.nameHash));
                    else if (param.type == AnimatorControllerParameterType.Bool)
                        newAnim.SetBool(param.nameHash, oldAnim.GetBool(param.nameHash));
                }
            }
            // --------------------------------------

            _modelHiddenByMe.SetActive(false);

            // 4. ACOPLAR mi cuerpo al Player
            // Nos ponemos como hijos del padre del cuerpo original para mantener la jerarquía
            transform.SetParent(playerOriginalBody.transform.parent);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            // Asegurarnos de que el visual del costume esté activo
            if (myCostumeBody != null)
            {
                myCostumeBody.SetActive(true);
            }

            gameObject.SetActive(true);
            _isEquipped = true;

            // 5. Limpiar componentes de mundo (Uso de Destroy seguro)
            if (TryGetComponent<PickupController>(out var p)) Destroy(p);
            if (TryGetComponent<Rigidbody>(out var rb)) Destroy(rb);
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }

        private void OnDestroy()
        {
            // Evitar errores al cerrar el juego o si el objeto ya no es válido
            if (!_isEquipped || _modelHiddenByMe == null) return;

            // Si el objeto que escondimos aún existe y la escena sigue cargada, lo restauramos
            if (_modelHiddenByMe.gameObject != null && _modelHiddenByMe.scene.isLoaded)
            {
                _modelHiddenByMe.SetActive(true);
            }
        }

        private GameObject FindChildWithTag(GameObject parent, string tag)
        {
            return parent.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.CompareTag(tag))?.gameObject;
        }
    }
}
