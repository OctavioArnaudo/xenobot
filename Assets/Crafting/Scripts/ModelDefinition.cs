using UnityEngine;

namespace Crafting.Scripts
{
    /// <summary>
    /// Define la estructura visual de un modelo del jugador.
    /// Permite al Hub encontrar puntos de interés sin depender de nombres de huesos específicos.
    /// </summary>
    public class ModelDefinition : MonoBehaviour
    {
        [Header("Key Points")]
        public Transform headPoint;
        public Transform spinePoint;
        public Transform muzzlePoint;

        [Header("Config")]
        public float modelHeight = 1.8f;
        public float cameraVerticalOffset = 0.5f;

        private Animator _animator;
        public Animator Animator
        {
            get
            {
                if (_animator == null) _animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
                return _animator;
            }
        }

        private void Awake()
        {
            // Auto-discovery de huesos si no están asignados (basado en Humanoid)
            var anim = Animator;
            if (anim != null && anim.isHuman)
            {
                if (headPoint == null) headPoint = anim.GetBoneTransform(HumanBodyBones.Head);
                if (spinePoint == null) spinePoint = anim.GetBoneTransform(HumanBodyBones.Spine);
            }

            // Fallback si sigue nulo
            if (headPoint == null) headPoint = transform;
            if (spinePoint == null) spinePoint = transform;
            if (muzzlePoint == null) muzzlePoint = transform;
        }
    }
}
