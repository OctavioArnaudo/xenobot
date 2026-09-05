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
    public class RespawnController : NetworkBehaviour, IPlayer
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
            // 1. Esperar a que la escena se estabilice un poco más
            yield return new WaitForSeconds(0.3f);

            string sceneName = SceneManager.GetActiveScene().name;

            // 2. Intentar spawnear hasta 15 veces (útil para escenas pesadas en red)
            bool success = false;
            for (int i = 0; i < 15; i++)
            {
                success = TeleportToSceneSpawn(sceneName);
                if (success) break;
                yield return new WaitForSeconds(0.2f);
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

        public bool TeleportToSceneSpawn(string sceneName)
        {
            if (_hub == null) _hub = PlayerController.LocalInstance;
            if (_hub == null) return false;

            string targetTag = "Respawn";
            var config = SceneSpawns.Find(s => s.SceneName == sceneName);
            if (!string.IsNullOrEmpty(config.SpawnPointTag)) targetTag = config.SpawnPointTag;

            GameObject spawnPoint = FindSpawnPoint(targetTag);

            if (spawnPoint != null && Vector3.Distance(spawnPoint.transform.position, Vector3.zero) > 0.1f)
            {
                if (_hub.controller != null) _hub.controller.enabled = false;

                // Forzar posición y rotación
                _hub.transform.SetPositionAndRotation(spawnPoint.transform.position, spawnPoint.transform.rotation);

                // CRÍTICO: Avisar al sistema de físicas que el objeto se movió manualmente
                Physics.SyncTransforms();

                // Resetear velocidad en el MovementController
                var move = _hub.GetComponentInChildren<MovementController>();
                if (move != null) move.ResetPhysics();

                // Solo guardamos como punto de respawn si no es el origen
                _startingPosition = _hub.transform.position;
                _startingRotation = _hub.transform.rotation;

                if (_hub.controller != null) _hub.controller.enabled = true;

                Debug.Log($"[RespawnController] EXITO: Jugador teletransportado a {spawnPoint.name} (Tag: {targetTag}) en {spawnPoint.transform.position}");
                return true;
            }
            return false;
        }

        private GameObject FindSpawnPoint(string tag)
        {
            // 1. Intentar encontrar un spawn point que sea hijo del gestor de misiones o bioma (más específico)
            GameObject[] tagged = GameObject.FindGameObjectsWithTag(tag);
            if (tagged.Length > 0)
            {
                // Ordenar por cercanía a la posición actual del objeto (útil para checkpoints)
                System.Array.Sort(tagged, (a, b) =>
                    Vector3.Distance(transform.position, a.transform.position).CompareTo(
                    Vector3.Distance(transform.position, b.transform.position)));

                return tagged[0];
            }

            // 2. Intentar búsqueda profunda por nombre si el Tag falló
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

            _hub.transform.SetPositionAndRotation(_startingPosition, _startingRotation);
            Physics.SyncTransforms();

            if (_hub.controller != null) _hub.controller.enabled = true;

            var cam = _hub.GetComponentInChildren<CameraController>();
            if (cam != null) cam.ResetCameraRotation(_startingRotation.eulerAngles.y);

            if (respawnSound != null) AudioSource.PlayClipAtPoint(respawnSound, _hub.transform.position);
        }
    }
}
