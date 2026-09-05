using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Base interface for all modules that are part of the Modular Ecosystem.
    /// Allows explicit binding with a central ModularController (Hub).
    /// </summary>
    public interface IModular
    {
        /// <summary>
        /// Binds the module with the central ModularController.
        /// </summary>
        void Bind(ModularController hub);

        /// <summary>
        /// Called when the Hub detects a major hierarchy change.
        /// </summary>
        void OnRefreshModule();
    }

    /// <summary>
    /// Base class for all entity hubs (Players and Enemies).
    /// Manages hardware distribution and module registry.
    /// </summary>
    public abstract class ModularController : NetworkBehaviour
    {
        protected Dictionary<System.Type, MonoBehaviour> _registeredModules = new();

        [Header("Core References (Hardware)")]
        public CharacterController controller;
        public Animator animator;
        public GameObject mainCamera;

        [Header("Hierarchy Articulation")]
        public Transform renderRoot;
        public GameObject cameraTarget;
        public RenderController activeModel;

        // Dynamic properties that always point to the active model's bones
        public Transform HeadPoint { get { if (activeModel != null) activeModel.EnsurePoints(); return activeModel != null ? activeModel.headPoint : null; } }
        public Transform SpinePoint { get { if (activeModel != null) activeModel.EnsurePoints(); return activeModel != null ? activeModel.spinePoint : null; } }
        public Transform MuzzlePoint { get { if (activeModel != null) activeModel.EnsurePoints(); return activeModel != null ? activeModel.muzzlePoint : null; } }
        public Transform CameraLookAtPoint { get { if (activeModel != null) activeModel.EnsurePoints(); return activeModel != null ? activeModel.cameraLookAtPoint : null; } }

        [Header("Shared Physical State")]
        public float VerticalVelocity;
        public bool IsGrounded;
        public float BaseGravity = -35f;

        public T GetModule<T>() where T : MonoBehaviour
        {
            if (_registeredModules.TryGetValue(typeof(T), out var module))
                return module as T;

            var found = GetComponentInChildren<T>();
            if (found != null) _registeredModules[typeof(T)] = found;
            return found;
        }

        public void RegisterModule(MonoBehaviour module)
        {
            var type = module.GetType();
            if (!_registeredModules.ContainsKey(type))
            {
                _registeredModules[type] = module;
            }
        }

        public virtual void RefreshBodyReferences()
        {
            // 1. Auto-discovery of Render Root via Tag "Render"
            if (renderRoot == null)
            {
                var taggedRender = GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t != transform && t.CompareTag("Render"));

                if (taggedRender != null) renderRoot = taggedRender;
            }

            // Legacy Fallbacks
            if (renderRoot == null) renderRoot = transform.Find("PlayerRender") ?? transform.Find("Render");

            // Auto-discovery of Camera Target
            if (cameraTarget == null) cameraTarget = transform.Find("PlayerTarget")?.gameObject ?? transform.Find("Target")?.gameObject;

            if (renderRoot != null)
            {
                // Force base alignment: The render root must be at the feet of the entity
                renderRoot.localPosition = Vector3.zero;
                renderRoot.localRotation = Quaternion.identity;

                // Intelligent Model Discovery: The RenderController is usually on the renderRoot or its children
                activeModel = renderRoot.GetComponentsInChildren<RenderController>(true)
                    .FirstOrDefault(rc => rc.transform != renderRoot)
                    ?? renderRoot.GetComponent<RenderController>();

                if (activeModel != null)
                {
                    activeModel.gameObject.SetActive(true);
                    animator = activeModel.Animator;
                    if (animator != null)
                    {
                        animator.enabled = true;
                        animator.Rebind();
                        animator.Update(0);
                    }
                }
            }

            if (cameraTarget != null && activeModel != null)
            {
                Transform lookPoint = CameraLookAtPoint ?? HeadPoint ?? activeModel.transform;
                cameraTarget.transform.position = lookPoint.position;
            }

            NotifyModulesRefresh();
        }

        public void NotifyModulesRefresh()
        {
            foreach (var module in _registeredModules.Values)
            {
                if (module is IModular modularModule)
                {
                    modularModule.OnRefreshModule();
                }
            }
        }

        protected bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
        protected bool CanExecuteLocalLogic => !IsNetworkActive || IsOwner;

        public abstract GameObject GetPrefabFromList(string prefabName);

        /// <summary>
        /// Cleans up an instantiated module or equipment to prevent interference.
        /// </summary>
        public void SanitizeModuleInstance(GameObject instance)
        {
            if (instance == null) return;

            // 1. Network Safety
            if (instance.TryGetComponent<NetworkObject>(out var netObj))
            {
                netObj.enabled = false;
            }

            // 2. Physics Safety
            if (instance.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            foreach (var col in instance.GetComponentsInChildren<Collider>(true))
            {
                col.isTrigger = true;
            }
        }
    }
}
