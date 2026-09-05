using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for player Respawn and scene spawning.
    /// Operates on the player root transform.
    /// </summary>
    public class RespawnController : NetworkBehaviour, IPlayerModule
    {
        [System.Serializable]
        public struct SceneSpawnConfig
        {
            public string SceneName;
            public string SpawnPointTag;
        }

        [Header("Respawn & Spawning")]
        public List<SceneSpawnConfig> SceneSpawns = new List<SceneSpawnConfig>();
        public AudioClip respawnSound;

        private Vector3 _startingPosition;
        private Quaternion _startingRotation;
        private PlayerController _hub;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        private void Awake()
        {
            _hub = GetComponentInParent<PlayerController>();
            if (_hub != null) Bind(_hub);
        }

        public void Bind(PlayerController hub)
        {
            _hub = hub;
            if (_hub != null) _hub.RegisterModule(this);
        }

        public void OnRefreshModule() { }

        private void Start()
        {
            if (_hub == null) _hub = GetComponentInParent<PlayerController>();
            if (_hub == null) _hub = PlayerController.LocalInstance;

            if (_hub != null)
            {
                _startingPosition = _hub.transform.position;
                _startingRotation = _hub.transform.rotation;
            }

            StartCoroutine(TeleportSequence());
        }

        private IEnumerator TeleportSequence()
        {
            // Esperar un frame para que todo se asiente
            yield return null;

            string sceneName = SceneManager.GetActiveScene().name;
            TeleportToSceneSpawn(sceneName);

            // Reintento si seguimos en 0,0,0
            if (_hub != null && Vector3.Distance(_hub.transform.position, Vector3.zero) < 0.1f)
            {
                for (int i = 0; i < 5; i++)
                {
                    yield return new WaitForSeconds(0.2f);
                    TeleportToSceneSpawn(sceneName);
                    if (Vector3.Distance(_hub.transform.position, Vector3.zero) > 0.1f) break;
                }
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                TeleportToSceneSpawn(SceneManager.GetActiveScene().name);
                SceneManager.sceneLoaded += OnSceneLoaded;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
                SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TeleportToSceneSpawn(scene.name);
        }

        public void TeleportToSceneSpawn(string sceneName)
        {
            if (_hub == null) _hub = PlayerController.LocalInstance;
            if (_hub == null) return;

            string targetTag = "Respawn";
            var config = SceneSpawns.Find(s => s.SceneName == sceneName);
            if (!string.IsNullOrEmpty(config.SpawnPointTag)) targetTag = config.SpawnPointTag;

            GameObject spawnPoint = FindSpawnPoint(targetTag);

            if (spawnPoint != null)
            {
                if (_hub.controller != null) _hub.controller.enabled = false;

                _hub.transform.position = spawnPoint.transform.position;
                _hub.transform.rotation = spawnPoint.transform.rotation;

                // Resetear velocidad en el MovementController
                var move = _hub.GetComponentInChildren<MovementController>();
                if (move != null) move.ResetPhysics();

                _startingPosition = _hub.transform.position;
                _startingRotation = _hub.transform.rotation;

                if (_hub.controller != null) _hub.controller.enabled = true;

                Debug.Log($"[RespawnController] EXITO: Jugador teletransportado a {spawnPoint.name} en {spawnPoint.transform.position}");
            }
            else
            {
                //Debug.LogError($"[RespawnController] ERROR: No se encontró ningún objeto con Tag o Nombre '{targetTag}' en la escena actual.");
            }
        }

        private GameObject FindSpawnPoint(string tag)
        {
            // 1. Intentar por Tag estándar (solo activos)
            GameObject[] tagged = GameObject.FindGameObjectsWithTag(tag);
            if (tagged.Length > 0) return tagged[0];

            // 2. Intentar búsqueda profunda (incluyendo inactivos)
            var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (var t in allTransforms)
            {
                // Solo objetos que estén en una escena activa (no prefabs)
                if (t.gameObject.scene.name == null) continue;

                if (t.CompareTag(tag) || t.name.Equals(tag, System.StringComparison.OrdinalIgnoreCase))
                {
                    return t.gameObject;
                }
            }

            return null;
        }

        public void Respawn()
        {
            if (_hub == null) return;
            if (_hub.controller != null) _hub.controller.enabled = false;

            _hub.transform.position = _startingPosition;
            _hub.transform.rotation = _startingRotation;

            if (_hub.controller != null) _hub.controller.enabled = true;

            var cam = _hub.GetComponentInChildren<CameraController>();
            if (cam != null) cam.ResetCameraRotation(_startingRotation.eulerAngles.y);

            if (respawnSound != null) AudioSource.PlayClipAtPoint(respawnSound, _hub.transform.position);
        }
    }
}
