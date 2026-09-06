using UnityEngine;
using Unity.Netcode;
using UnityEngine.Events;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public class DamageController : MonoBehaviour, IModular
    {
        [Header("Visual Feedback")]
        public Renderer[] visualsToFlash;
        public Color flashColor = Color.white;
        public float flashDuration = 0.15f;

        [Header("Events")]
        public UnityEvent<int> OnTakeDamage;

        private HealthController _health;
        private ModularController _hub;
        private float _damageFlashTimer;

        void Awake()
        {
            if (_hub == null) _hub = GetComponentInParent<ModularController>();

            if (visualsToFlash == null || visualsToFlash.Length == 0)
                visualsToFlash = GetComponentsInChildren<Renderer>();
        }

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null)
            {
                _hub.RegisterModule(this);
                OnRefreshModule();
            }
        }

        public void OnRefreshModule()
        {
            if (_hub != null)
            {
                _health = _hub.GetModule<HealthController>();
                visualsToFlash = _hub.renderRoot?.GetComponentsInChildren<Renderer>() ?? GetComponentsInChildren<Renderer>();
            }
        }

        void Update()
        {
            if (_damageFlashTimer > 0) _damageFlashTimer -= Time.deltaTime;
        }

        public void TakeDamage(int damage, Team attackerTeam = Team.Neutral)
        {
            if (damage <= 0 || _health == null || _hub == null) return;

            // Friendly fire protection
            if (attackerTeam != Team.Neutral && _hub.MyTeam == attackerTeam) return;

            int finalDamage = damage;
            finalDamage = Mathf.RoundToInt(damage * (10f / (10f + _hub.Defense.Value)));
            if (finalDamage < 1) finalDamage = 1;

            _health.ApplyDirectHealthChange(-finalDamage);

            if (_hub.IsOwner && _hub.MyTeam == Team.Player) _damageFlashTimer = 0.6f;
            PlayHitFlash();

            OnTakeDamage?.Invoke(finalDamage);
        }

        private void PlayHitFlash()
        {
            if (visualsToFlash != null && visualsToFlash.Length > 0)
            {
                foreach (var r in visualsToFlash)
                {
                    if (r == null) continue;
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetColor("_EmissionColor", flashColor * 2f);
                    r.SetPropertyBlock(mpb);
                }
                Invoke(nameof(ResetFlash), flashDuration);
            }
        }

        private void ResetFlash()
        {
            if (visualsToFlash != null)
            {
                foreach (var r in visualsToFlash)
                {
                    if (r != null) r.SetPropertyBlock(null);
                }
            }
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_hub == null || !_hub.IsOwner || _hub.MyTeam != Team.Player) return;

            if (_damageFlashTimer > 0)
            {
                GUI.color = new Color(1, 0, 0, _damageFlashTimer * 0.8f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }
    }
}
