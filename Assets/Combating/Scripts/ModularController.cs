using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Represents the team affiliation of an entity.
    /// </summary>
    //public enum Team { Neutral, Player, Enemy }

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

        // Population Counters for Dynamic Scaling
        public static int PlayerCount { get; private set; }
        public static int EnemyCount { get; private set; }

        public Team MyTeam { get; protected set; } = Team.Neutral;

        [Header("Centralized Network State")]
        public NetworkVariable<FixedString32Bytes> playerName = new NetworkVariable<FixedString32Bytes>(new FixedString32Bytes(""), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<Color> playerColor = new NetworkVariable<Color>(Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public NetworkVariable<int> Level = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> Exp = new NetworkVariable<float>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> Attack = new NetworkVariable<float>(10, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> Defense = new NetworkVariable<float>(5, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> ExpToLevelUp = new NetworkVariable<float>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<int> currentHealth = new NetworkVariable<int>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> maxHealth = new NetworkVariable<int>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> currentFuel = new NetworkVariable<float>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> maxFuel = new NetworkVariable<float>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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
        public float HorizontalSpeed;
        public bool IsGrounded;
        [HideInInspector] public float BaseGravity = -15f;

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

        protected virtual void Awake()
        {
            DetermineMyTeam();
        }

        public override void OnNetworkSpawn()
        {
            DetermineMyTeam();
        }

        public override void OnNetworkDespawn()
        {
            if (this is PlayerController) PlayerCount--;
            else if (this is EnemyController) EnemyCount--;
        }

        protected void DetermineMyTeam()
        {
            if (this is PlayerController) { if (IsSpawned) PlayerCount++; MyTeam = Team.Player; }
            else if (this is EnemyController) { if (IsSpawned) EnemyCount++; MyTeam = Team.Enemy; }
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
            if (renderRoot == null) renderRoot = transform.Find("PlayerRender") ?? transform.Find("EnemyRender") ?? transform.Find("Render");

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

                if (activeModel == null)
                {
                    activeModel = renderRoot.gameObject.AddComponent<RenderController>();
                }

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

        // --- Centralized Actions (RPCs) ---

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ApplyHealthChangeServerRpc(int amount)
        {
            currentHealth.Value = Mathf.Clamp(currentHealth.Value + amount, 0, maxHealth.Value);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void AddExpServerRpc(float amount)
        {
            var leveling = GetModule<LevelingController>();
            if (leveling != null)
            {
                // Leveling logic is server-side authoritative over Hub variables
                Exp.Value += amount;
                while (Exp.Value >= ExpToLevelUp.Value)
                {
                    Exp.Value -= ExpToLevelUp.Value;
                    Level.Value++;
                    // Basic scaling here or call leveling module
                    Attack.Value += 2.0f;
                    Defense.Value += 1.5f;
                    ExpToLevelUp.Value *= 1.2f;

                    maxHealth.Value += 15;
                    currentHealth.Value = maxHealth.Value; // Full heal on level up

                    maxFuel.Value += 20f;
                    currentFuel.Value = maxFuel.Value;
                }
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestMeleeServerRpc()
        {
            var melee = GetModule<Testing.Scripts.MeleeController>();
            if (melee != null) melee.ExecuteMeleeServerSide();
        }

        /// <summary>
        /// Cleans up an instantiated module or equipment to prevent interference.
        /// </summary>
        public void SanitizeModuleInstance(GameObject instance)
        {
            if (instance == null) return;

            // 1. Remove World Interaction components that depend on NetworkObject first
            foreach (var p in instance.GetComponentsInChildren<PickupController>(true))
            {
                DestroyImmediate(p);
            }

            // 2. Network Safety: Embedded modules should NOT have their own NetworkObject
            // they should rely on the Hub's NetworkObject.
            var netObjs = instance.GetComponentsInChildren<NetworkObject>(true);
            foreach (var netObj in netObjs)
            {
                DestroyImmediate(netObj);
            }

            // 3. Physics Safety
            if (instance.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            foreach (var col in instance.GetComponentsInChildren<Collider>(true))
            {
                col.isTrigger = true;
            }

            // 4. Enemy Hardware Safety: Ensure modules don't bring cameras or listeners
            if (this is EnemyController)
            {
                foreach (var cam in instance.GetComponentsInChildren<Camera>(true)) DestroyImmediate(cam);
                foreach (var listener in instance.GetComponentsInChildren<AudioListener>(true)) DestroyImmediate(listener);
            }
        }
    }
}
