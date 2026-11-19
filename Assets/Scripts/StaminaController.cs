using UnityEngine;
using Unity.Netcode;
using Runtime.Scripts.Items;
using Runtime.Scripts.Pickup;
using Runtime.Scripts.Player;
using Runtime.Scripts.Optional;
using Runtime.Scripts.Effective;
using System.Collections;

namespace Runtime.Scripts.Stamina
{
    [RequireComponent(typeof(NetworkObject))]
    public class StaminaController : NetworkBehaviour, IItemUseAction, IItemPickupAction
    {
        private const float DEFAULT_SPEED_BOOST = 1.5f;
        private const float DEFAULT_JUMP_BOOST = 1.4f;
        private const float DEFAULT_DURATION = 10.0f;
        private const float DEFAULT_SCALE = 1.5f;

        public float SpeedBoostMultiplier = 1.5f;
        public Optional<float> speedBoostMultiplier;
        public float JumpBoostMultiplier = 1.4f;
        public Optional<float> jumpBoostMultiplier;
        public float BoostDuration = 10.0f;
        public Optional<float> boostDuration;
        public float RenderScale = 1.5f;
        public Optional<float> renderScale;

        public float EffectiveSpeedBoost => speedBoostMultiplier.Get(SpeedBoostMultiplier, DEFAULT_SPEED_BOOST);
        public float EffectiveJumpBoost => jumpBoostMultiplier.Get(JumpBoostMultiplier, DEFAULT_JUMP_BOOST);
        public float EffectiveDuration => boostDuration.Get(BoostDuration, DEFAULT_DURATION);
        public float EffectiveScale => renderScale.Get(RenderScale, DEFAULT_SCALE);

        void Awake()
        {
            SetupPickup();
            GenerateBottleVisuals();
        }

        private void SetupPickup()
        {
            var pickup = GetComponent<PickupController>();
            if (pickup != null && pickup.item == null)
            {
#if UNITY_EDITOR
                string path = "Assets/Resources/Crafting/Items/Data/Item_Stamina.asset";
                pickup.item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemsData>(path);
#endif
            }
        }

        public void OnUseItem(GameObject entity) => ApplyEffect(entity);
        public void OnPickupItem(GameObject entity) { }

        public void ApplyEffect(GameObject entity)
        {
            var player = entity.GetComponent<PlayerController>() ?? entity.GetComponentInParent<PlayerController>() ?? entity.GetComponentInChildren<PlayerController>();
            if (player != null)
            {
                player.StartCoroutine(BoostRoutine(player));
            }
        }

        private IEnumerator BoostRoutine(PlayerController player)
        {
            float origMove = player.MoveSpeed > 0 ? player.MoveSpeed : 2.0f;
            float origSprint = player.SprintSpeed > 0 ? player.SprintSpeed : 5.335f;
            float origJump = player.JumpHeight > 0 ? player.JumpHeight : 1.2f;
            float origDoubleJump = player.DoubleJumpHeight > 0 ? player.DoubleJumpHeight : 1.2f;

            player.MoveSpeed = origMove * EffectiveSpeedBoost;
            player.SprintSpeed = origSprint * EffectiveSpeedBoost;
            player.JumpHeight = origJump * EffectiveJumpBoost;
            player.DoubleJumpHeight = origDoubleJump * EffectiveJumpBoost;

            yield return new WaitForSeconds(EffectiveDuration);

            player.MoveSpeed = origMove;
            player.SprintSpeed = origSprint;
            player.JumpHeight = origJump;
            player.DoubleJumpHeight = origDoubleJump;
        }

        [ContextMenu("Generate Bottle Visuals")]
        public void GenerateBottleVisuals()
        {
            foreach (Transform child in transform)
            {
                if (child.name == "BottleRender")
                {
                    if (Application.isPlaying) Destroy(child.gameObject);
                    else DestroyImmediate(child.gameObject);
                }
            }

            GameObject root = new GameObject("BottleRender");
            root.transform.SetParent(transform, false);
            root.transform.localScale = Vector3.one * EffectiveScale;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "BottleBody";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.4f, 0.5f, 0.4f);
            DestroyColliders(body);

            GameObject neck = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            neck.name = "BottleNeck";
            neck.transform.SetParent(body.transform, false);
            neck.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            neck.transform.localScale = new Vector3(0.4f, 0.3f, 0.4f);
            DestroyColliders(neck);

            GameObject cork = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cork.name = "BottleCork";
            cork.transform.SetParent(neck.transform, false);
            cork.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            cork.transform.localScale = new Vector3(0.8f, 0.3f, 0.8f);
            DestroyColliders(cork);

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var glassMat = new Material(shader);
            glassMat.color = new Color(0.2f, 0.9f, 0.5f, 0.6f);
            body.GetComponent<MeshRenderer>().sharedMaterial = glassMat;

            Light lt = root.AddComponent<Light>();
            lt.type = LightType.Point;
            lt.color = new Color(0.1f, 0.8f, 0.9f);
            lt.intensity = 2.0f;
            lt.range = 3.0f;
        }

        private void DestroyColliders(GameObject go)
        {
            if (go.TryGetComponent<Collider>(out var c))
            {
                if (Application.isPlaying) Destroy(c);
                else DestroyImmediate(c, true);
            }
        }
    }
}

