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
    public class CostumeController : MonoBehaviour, IItemUseAction, IItemQuitAction, IItemDropAction
    {
        [Header("Settings")]
        [Tooltip("Tag to find the render root in the player hierarchy")]
        public string renderTag = "Render";

        private GameObject _modelHiddenByMe;
        private bool _isEquipped = false;

        public void OnQuitItem(GameObject player)
        {
            if (!_isEquipped || _modelHiddenByMe == null) return;

            if (_modelHiddenByMe.gameObject != null && _modelHiddenByMe.scene.isLoaded)
            {
                _modelHiddenByMe.SetActive(true);
            }
            _isEquipped = false;
        }

        public void OnDropItem(GameObject player) => OnQuitItem(player);

        public void OnUseItem(GameObject player)
        {
            // Si este prefab de ítem también tiene un MaskController, cancelar la ocultación de traje
            if (GetComponent<MaskController>() != null || GetComponentInChildren<MaskController>() != null ||
                gameObject.name.ToLower().Contains("mask") || gameObject.name.ToLower().Contains("mascara"))
            {
                enabled = false;
                return;
            }

            if (_isEquipped) return;

            // 1. Identificar el cuerpo original del Player (el objeto con tag Render)
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
                newAnim.runtimeAnimatorController = oldAnim.runtimeAnimatorController;

                var stateInfo = oldAnim.GetCurrentAnimatorStateInfo(0);
                newAnim.Play(stateInfo.fullPathHash, 0, stateInfo.normalizedTime);

                foreach (var param in oldAnim.parameters)
                {
                    if (param.type == AnimatorControllerParameterType.Float)
                        newAnim.SetFloat(param.nameHash, oldAnim.GetFloat(param.nameHash));
                    else if (param.type == AnimatorControllerParameterType.Bool)
                        newAnim.SetBool(param.nameHash, oldAnim.GetBool(param.nameHash));
                }
            }

            _modelHiddenByMe.SetActive(false);

            // 4. ACOPLAR mi cuerpo al Player
            if (TryGetComponent<Unity.Netcode.NetworkObject>(out var netObj))
            {
                netObj.enabled = false;
            }

            transform.SetParent(playerOriginalBody.transform.parent);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            if (myCostumeBody != null)
            {
                myCostumeBody.SetActive(true);
            }

            gameObject.SetActive(true);
            _isEquipped = true;

            // 5. Limpiar componentes de mundo
            if (TryGetComponent<PickupController>(out var p)) Destroy(p);
            if (TryGetComponent<Rigidbody>(out var rb)) Destroy(rb);
            if (netObj != null) Destroy(netObj);
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }

        private void OnDestroy()
        {
            OnQuitItem(null);
        }

        private GameObject FindChildWithTag(GameObject parent, string tag)
        {
            return parent.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.CompareTag(tag))?.gameObject;
        }
    }
}
