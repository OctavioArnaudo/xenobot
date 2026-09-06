using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public class RespawnController : NetworkBehaviour, IModular
    {
        [System.Serializable]
        public struct SceneSpawnConfig
        {
            public string SceneName;
            public string SpawnPointTag;
        }

        [Header("Respawn & Spawning")]
        public List<SceneSpawnConfig> SceneSpawns = new List<SceneSpawnConfig>();
        public float FallThreshold = -50f;
        public AudioClip respawnSound;

        private Vector3 _startingPosition;
        private Quaternion _startingRotation;
        private ModularController _hub;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        private void Awake()
        {
            _hub = GetComponentInParent<ModularController>();
            if (_hub != null) Bind(_hub);
        }

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null) _hub.RegisterModule(this);
        }

        public void OnRefreshModule() { }

        private void Start()
        {
            if (_hub == null) _hub = Testing.Scripts.PlayerController.LocalInstance;

            if (_hub != null)
            {
                _startingPosition = _hub.transform.position;
                _startingRotation = _hub.transform.rotation;
            }

            StartCoroutine(TeleportSequence());
        }

        private IEnumerator TeleportSequence()
        {
            yield return new WaitForSeconds(0.3f);
            string sceneName = SceneManager.GetActiveScene().name;
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
            if (_hub == null) _hub = Testing.Scripts.PlayerController.LocalInstance;
            if (_hub == null) return false;

            string targetTag = "Respawn";
            var config = SceneSpawns.Find(s => s.SceneName == sceneName);
            if (!string.IsNullOrEmpty(config.SpawnPointTag)) targetTag = config.SpawnPointTag;

            GameObject spawnPoint = FindSpawnPoint(targetTag);

            if (spawnPoint != null && Vector3.Distance(spawnPoint.transform.position, Vector3.zero) > 0.1f)
            {
                if (_hub.controller != null) _hub.controller.enabled = false;
                _hub.transform.SetPositionAndRotation(spawnPoint.transform.position, spawnPoint.transform.rotation);
                Physics.SyncTransforms();

                var move = _hub.GetModule<MovementController>();
                if (move != null) move.ResetPhysics();

                _startingPosition = _hub.transform.position;
                _startingRotation = _hub.transform.rotation;

                if (_hub.controller != null) _hub.controller.enabled = true;
                return true;
            }
            return false;
        }

        private GameObject FindSpawnPoint(string tag)
        {
            GameObject[] tagged = GameObject.FindGameObjectsWithTag(tag);
            if (tagged.Length > 0)
            {
                System.Array.Sort(tagged, (a, b) =>
                    Vector3.Distance(transform.position, a.transform.position).CompareTo(
                    Vector3.Distance(transform.position, b.transform.position)));
                return tagged[0];
            }
            return null;
        }

        void Update()
        {
            bool hasAuthority = (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) ? true : IsOwner;
            if (!hasAuthority || _hub == null) return;

            if (_hub.transform.position.y < FallThreshold)
            {
                Respawn();
            }
        }

        public void Respawn()
        {
            if (_hub == null) return;
            if (_hub.controller != null) _hub.controller.enabled = false;

            _hub.transform.SetPositionAndRotation(_startingPosition, _startingRotation);
            Physics.SyncTransforms();

            if (_hub.controller != null) _hub.controller.enabled = true;

            var cam = _hub.GetModule<CameraController>();
            if (cam != null) cam.ResetCameraRotation(_startingRotation.eulerAngles.y);

            if (respawnSound != null) AudioSource.PlayClipAtPoint(respawnSound, _hub.transform.position);
        }
    }
}
