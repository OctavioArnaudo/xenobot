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

        private bool _isInitialized = false;

        private void Awake() => EnsurePoints();

        public void EnsurePoints()
        {
            if (_isInitialized) return;

            // Force visual alignment with the physical root
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            // 1. Limpieza de colisionadores antiguos y setup de MeshColliders
            SetupPreciseColliders();

            // 2. Auto-discovery de huesos críticos
            if (headPoint == null)
            {
                var anim = Animator;
                if (anim != null && anim.isHuman) headPoint = anim.GetBoneTransform(HumanBodyBones.Head);
                if (headPoint == null) headPoint = FindDeepChild(transform, "Head") ?? FindDeepChild(transform, "Cabeza") ?? FindDeepChild(transform, "Neck") ?? FindDeepChild(transform, "Joint_Head");
            }

            if (spinePoint == null)
            {
                var anim = Animator;
                if (anim != null && anim.isHuman) spinePoint = anim.GetBoneTransform(HumanBodyBones.Spine);
                if (spinePoint == null) spinePoint = FindDeepChild(transform, "Spine") ?? FindDeepChild(transform, "Chest") ?? FindDeepChild(transform, "Cuerpo");
            }

            if (cameraLookAtPoint == null) cameraLookAtPoint = headPoint;

            // Si después de todo no hay cabeza, creamos un punto virtual a 1.6m para salvar la cámara
            if (cameraLookAtPoint == null)
            {
                Transform existingVirtual = transform.Find("Virtual_Camera_Target");
                if (existingVirtual != null)
                {
                    cameraLookAtPoint = existingVirtual;
                }
                else
                {
                    GameObject vPoint = new GameObject("Virtual_Camera_Target");
                    vPoint.transform.SetParent(transform);
                    vPoint.transform.localPosition = new Vector3(0, 1.6f, 0);
                    cameraLookAtPoint = vPoint.transform;
                }
            }

            _isInitialized = true;
        }

        private Transform FindDeepChild(Transform parent, string name)
        {
            // Búsqueda exhaustiva recursiva
            foreach (Transform child in parent)
            {
                if (child.name.Equals(name, System.StringComparison.OrdinalIgnoreCase) ||
                    child.name.Contains(name, System.StringComparison.OrdinalIgnoreCase))
                    return child;

                Transform result = FindDeepChild(child, name);
                if (result != null) return result;
            }
            return null;
        }

        /// <summary>
        /// Reemplaza CapsuleColliders por MeshColliders precisos o BoxColliders según la complejidad.
        /// </summary>
        private void SetupPreciseColliders()
        {
            foreach (var cap in GetComponentsInChildren<CapsuleCollider>(true))
            {
                if (Application.isPlaying) Destroy(cap);
                else DestroyImmediate(cap);
            }

            // Procesar SkinnedMeshRenderers (Partes animadas)
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.gameObject.GetComponent<Collider>() != null) continue;
                if (smr.sharedMesh == null) continue;

                // Si la malla es muy compleja (> 250 vértices), usamos un BoxCollider por rendimiento y estabilidad
                if (smr.sharedMesh.vertexCount > 250)
                {
                    var bc = smr.gameObject.AddComponent<BoxCollider>();
                    bc.isTrigger = true;
                }
                else
                {
                    var mc = smr.gameObject.AddComponent<MeshCollider>();
                    mc.convex = true;
                    mc.isTrigger = true;
                    mc.sharedMesh = smr.sharedMesh;
                }
            }

            // Procesar MeshRenderers (Partes estáticas/accesorios)
            foreach (var mr in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.gameObject.GetComponent<Collider>() != null) continue;
                if (!mr.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null) continue;

                if (mf.sharedMesh.vertexCount > 250)
                {
                    var bc = mr.gameObject.AddComponent<BoxCollider>();
                    bc.isTrigger = true;
                }
                else
                {
                    var mc = mr.gameObject.AddComponent<MeshCollider>();
                    mc.convex = true;
                    mc.isTrigger = true;
                    mc.sharedMesh = mf.sharedMesh;
                }
            }

            Debug.Log($"[RenderController] Optimized Colliders generated for {gameObject.name}");
        }
    }
}
