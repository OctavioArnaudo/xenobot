using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public class HealController : MonoBehaviour, IModular
    {
        [Header("Visual Feedback")]
        public Renderer[] visualsToFlash;
        public Color healFlashColor = new Color(0, 1, 0.2f, 1); // Bright Green
        public float flashDuration = 0.2f;

        private HealthController _health;
        private ModularController _hub;

        void Awake()
        {
            if (_hub == null) _hub = GetComponentInParent<ModularController>();
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

        public void Heal(int amount)
        {
            if (amount <= 0 || _health == null) return;

            if (_health.CurrentHP >= _health.maxHealth) return;

            _health.ApplyDirectHealthChange(amount);
            PlayHealFlash();
        }

        private void PlayHealFlash()
        {
            if (visualsToFlash != null && visualsToFlash.Length > 0)
            {
                foreach (var r in visualsToFlash)
                {
                    if (r == null) continue;
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetColor("_EmissionColor", healFlashColor * 2.5f);
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
    }
}
