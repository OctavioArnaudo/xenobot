using UnityEngine;
using Combating.Scripts;
using System.Collections.Generic;
using System.Linq;

namespace Crafting.Scripts
{
    /// <summary>
    /// Specialized controller for Jetpack visual representation and positioning.
    /// Generates a procedural 3D jetpack mesh.
    /// Implements IItemFunctional to handle auto-positioning when equipped.
    /// </summary>
    [ExecuteAlways]
    public class JetpackController : MonoBehaviour, IItemFunctional
    {
        [Header("Visuals (Procedural)")]
        public Color jetpackColor = new Color(0.3f, 0.3f, 0.4f);
        public float jetpackScale = 1.0f;

        void Awake()
        {
            GenerateJetpackMesh();
        }

        void OnValidate()
        {
            #if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += SafeGenerateMesh;
            }
            #endif
        }

        private void SafeGenerateMesh()
        {
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall -= SafeGenerateMesh;
            if (this == null) return;
            GenerateJetpackMesh();
            #endif
        }

        [Header("Settings")]
        public bool lockOnUnequip = true; // Si es true, se vuelve a bloquear la habilidad al quitar el item

        private PropulsionController _targetPropulsion;

        public void ApplyEffect(GameObject entity)
        {
            transform.localPosition = new Vector3(0, 2.4f, -0.35f);
            transform.localRotation = Quaternion.identity;

            // Activate Jetpack Module: Robust search for both Modular and Standard Players
            _targetPropulsion = entity.GetComponentInChildren<PropulsionController>(true) ??
                               entity.GetComponentInParent<PropulsionController>() ??
                               GameObject.FindObjectsByType<PropulsionController>(FindObjectsSortMode.None)
                               .FirstOrDefault(p => p.gameObject.transform.root == entity.transform.root);

            if (_targetPropulsion != null)
            {
                _targetPropulsion.isUnlocked = true;
                Debug.Log($"[JetpackController] Sistema de propulsión ACTIVADO en {entity.name}.");
            }
            else
            {
                Debug.LogWarning($"[JetpackController] No se encontró PropulsionController en la jerarquía de {entity.name}.");
            }
        }

        private void OnDestroy()
        {
            // Si el ítem se destruye (QUIT/DROP), volvemos a bloquear la habilidad si así está configurado
            if (lockOnUnequip && _targetPropulsion != null)
            {
                _targetPropulsion.isUnlocked = false;
                Debug.Log($"[JetpackController] Sistema de propulsión BLOQUEADO (ítem retirado).");
            }
        }

        public void GenerateJetpackMesh()
        {
            Transform renderTransform = transform.Find("JetpackRender");
            GameObject visual;

            if (renderTransform == null)
            {
                visual = new GameObject("JetpackRender");
                visual.transform.SetParent(transform, false);
            }
            else
            {
                visual = renderTransform.gameObject;
            }

            visual.transform.localScale = Vector3.one * jetpackScale;

            if (!visual.TryGetComponent<MeshFilter>(out MeshFilter mf))
                mf = visual.AddComponent<MeshFilter>();

            if (!visual.TryGetComponent<MeshRenderer>(out MeshRenderer mr))
                mr = visual.AddComponent<MeshRenderer>();

            Mesh mesh = new Mesh();
            mesh.name = "Jetpack_Mesh";

            List<Vector3> verts = new List<Vector3>();
            List<int> tris = new List<int>();

            AddBox(verts, tris, Vector3.zero, new Vector3(0.5f, 0.7f, 0.2f));
            AddBox(verts, tris, new Vector3(-0.35f, 0, 0.05f), new Vector3(0.25f, 0.6f, 0.25f));
            AddBox(verts, tris, new Vector3(0.35f, 0, 0.05f), new Vector3(0.25f, 0.6f, 0.25f));
            AddBox(verts, tris, new Vector3(-0.35f, -0.4f, 0.05f), new Vector3(0.15f, 0.2f, 0.15f));
            AddBox(verts, tris, new Vector3(0.35f, -0.4f, 0.05f), new Vector3(0.15f, 0.2f, 0.15f));

            mesh.vertices = verts.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (!Application.isPlaying && mf.sharedMesh != null)
            {
                Mesh oldMesh = mf.sharedMesh;
                mf.sharedMesh = null;
                DestroyImmediate(oldMesh, true);
            }

            mf.sharedMesh = mesh;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            if (mr.sharedMaterial == null || mr.sharedMaterial.shader != shader)
            {
                mr.sharedMaterial = new Material(shader);
                mr.sharedMaterial.name = "Jetpack_Material";
            }

            mr.sharedMaterial.color = jetpackColor;
            if (mr.sharedMaterial.HasProperty("_BaseColor"))
                mr.sharedMaterial.SetColor("_BaseColor", jetpackColor);

            if (mr.sharedMaterial.HasProperty("_Metallic")) mr.sharedMaterial.SetFloat("_Metallic", 0.7f);
            if (mr.sharedMaterial.HasProperty("_Smoothness")) mr.sharedMaterial.SetFloat("_Smoothness", 0.6f);
        }

        private void AddBox(List<Vector3> verts, List<int> tris, Vector3 center, Vector3 size)
        {
            int vCount = verts.Count;
            Vector3 h = size * 0.5f;

            verts.Add(center + new Vector3(-h.x, -h.y, -h.z));
            verts.Add(center + new Vector3(h.x, -h.y, -h.z));
            verts.Add(center + new Vector3(h.x, h.y, -h.z));
            verts.Add(center + new Vector3(-h.x, h.y, -h.z));
            verts.Add(center + new Vector3(-h.x, -h.y, h.z));
            verts.Add(center + new Vector3(h.x, -h.y, h.z));
            verts.Add(center + new Vector3(h.x, h.y, h.z));
            verts.Add(center + new Vector3(-h.x, h.y, h.z));

            int[] cubeTris = {
                0,2,1, 0,3,2, 4,5,6, 4,6,7,
                0,1,5, 0,5,4, 2,3,7, 2,7,6,
                1,2,6, 1,6,5, 3,0,4, 3,4,7
            };
            foreach (int t in cubeTris) tris.Add(vCount + t);
        }
    }
}
