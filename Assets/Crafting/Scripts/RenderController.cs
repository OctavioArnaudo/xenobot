using UnityEngine;

namespace Crafting.Scripts
{
    /// <summary>
    /// Gestiona la visualización del robot y delega puntos de interés a los sistemas.
    /// Posee inteligencia para auto-descubrir huesos si no están asignados.
    /// </summary>
    public class RenderController : MonoBehaviour
    {
        [Header("Hierarchy Points")]
        public Transform headPoint;
        public Transform spinePoint;
        public Transform muzzlePoint;
        public Transform cameraLookAtPoint;

        public Animator Animator => GetComponent<Animator>() ?? GetComponentInChildren<Animator>();

        private void Awake()
        {
            // Auto-discovery de huesos críticos para que la cámara no apunte al suelo (0,0,0)
            if (headPoint == null)
            {
                var anim = Animator;
                if (anim != null && anim.isHuman) headPoint = anim.GetBoneTransform(HumanBodyBones.Head);
                if (headPoint == null) headPoint = FindDeepChild(transform, "Head") ?? FindDeepChild(transform, "Cabeza") ?? FindDeepChild(transform, "Neck");
            }

            if (spinePoint == null)
            {
                var anim = Animator;
                if (anim != null && anim.isHuman) spinePoint = anim.GetBoneTransform(HumanBodyBones.Spine);
                if (spinePoint == null) spinePoint = FindDeepChild(transform, "Spine") ?? FindDeepChild(transform, "Chest");
            }

            if (cameraLookAtPoint == null) cameraLookAtPoint = headPoint;

            // Si después de todo no hay cabeza, creamos un punto virtual a 1.6m para salvar la cámara
            if (cameraLookAtPoint == null)
            {
                GameObject vPoint = new GameObject("Virtual_Camera_Target");
                vPoint.transform.SetParent(transform);
                vPoint.transform.localPosition = new Vector3(0, 1.6f, 0);
                cameraLookAtPoint = vPoint.transform;
            }
        }

        private Transform FindDeepChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name.Contains(name, System.StringComparison.OrdinalIgnoreCase)) return child;
                Transform result = FindDeepChild(child, name);
                if (result != null) return result;
            }
            return null;
        }
    }
}
