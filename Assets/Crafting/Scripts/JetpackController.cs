using UnityEngine;
using Crafting.Scripts;
using System.Collections.Generic;

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

        public void ApplyEffect(GameObject player)
        {
            // Posicionar en la espalda del robot
            transform.localPosition = new Vector3(0, 2.4f, -0.35f);
            transform.localRotation = Quaternion.identity;

            Debug.Log("[JetpackController] Visuales de jetpack vinculados al jugador.");
        }

        public void GenerateJetpackMesh()
        {
            // 1. Buscar o Crear el objeto visual único
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

            // 2. Construcción de Malla Única
            Mesh mesh = new Mesh();
            mesh.name = "Jetpack_Mesh";

            List<Vector3> verts = new List<Vector3>();
            List<int> tris = new List<int>();

            // Cuerpo Central (Placa de espalda)
            AddBox(verts, tris, Vector3.zero, new Vector3(0.5f, 0.7f, 0.2f));
            // Tanque Izquierdo
            AddBox(verts, tris, new Vector3(-0.35f, 0, 0.05f), new Vector3(0.25f, 0.6f, 0.25f));
            // Tanque Derecho
            AddBox(verts, tris, new Vector3(0.35f, 0, 0.05f), new Vector3(0.25f, 0.6f, 0.25f));
            // Toberas
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
                DestroyImmediate(oldMesh);
            }

            mf.sharedMesh = mesh;

            // 3. Material (Robust URP Support)
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            if (mr.sharedMaterial == null || mr.sharedMaterial.shader != shader)
            {
                mr.sharedMaterial = new Material(shader);
                mr.sharedMaterial.name = "Jetpack_Material";
            }

            // Aplicar color de forma segura para URP y Standard
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

            // FIX: Vertex 7 was wrong (z was -h.z instead of h.z)
            verts.Add(center + new Vector3(-h.x, -h.y, -h.z)); // 0
            verts.Add(center + new Vector3(h.x, -h.y, -h.z));  // 1
            verts.Add(center + new Vector3(h.x, h.y, -h.z));   // 2
            verts.Add(center + new Vector3(-h.x, h.y, -h.z));  // 3
            verts.Add(center + new Vector3(-h.x, -h.y, h.z));  // 4
            verts.Add(center + new Vector3(h.x, -h.y, h.z));   // 5
            verts.Add(center + new Vector3(h.x, h.y, h.z));    // 6
            verts.Add(center + new Vector3(-h.x, h.y, h.z));   // 7

            int[] cubeTris = {
                0,2,1, 0,3,2, 4,5,6, 4,6,7,
                0,1,5, 0,5,4, 2,3,7, 2,7,6,
                1,2,6, 1,6,5, 3,0,4, 3,4,7
            };
            foreach (int t in cubeTris) tris.Add(vCount + t);
        }
    }
}
