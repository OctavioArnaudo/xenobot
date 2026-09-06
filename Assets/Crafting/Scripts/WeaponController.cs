using UnityEngine;
using Combating.Scripts;
using System.Collections.Generic;

namespace Crafting.Scripts
{
    /// <summary>
    /// Specialized controller for Weapon visual representation.
    /// Optimized for a closer, more natural shooting position.
    /// </summary>
    [ExecuteAlways]
    public class WeaponController : MonoBehaviour, IItemFunctional
    {
        [Header("Visuals (Procedural)")]
        public Color weaponColor = new Color(0.2f, 0.2f, 0.25f);
        public float weaponScale = 1.0f;

        [Header("Runtime Info")]
        public Transform muzzlePoint;

        void Awake()
        {
            GenerateWeaponMesh();
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
            GenerateWeaponMesh();
            #endif
        }

        public void ApplyEffect(GameObject entity)
        {
            transform.localPosition = new Vector3(0.4f, 1.2f, 0.2f);
            transform.localRotation = Quaternion.identity;

            // Activate Shoot Module permanently
            ModularController hub = entity.GetComponent<ModularController>() ?? entity.GetComponentInParent<ModularController>();
            if (hub != null)
            {
                var shooter = hub.GetModule<ShootController>();
                if (shooter != null) shooter.enabled = true;
            }

            Debug.Log($"[WeaponController] Sistema de disparo activado en {entity.name}.");
        }

        public void GenerateWeaponMesh()
        {
            Transform renderTransform = transform.Find("WeaponRender");
            GameObject visual = (renderTransform != null) ? renderTransform.gameObject : new GameObject("WeaponRender");
            if (renderTransform == null) visual.transform.SetParent(transform, false);

            visual.transform.localScale = Vector3.one * weaponScale;

            if (!visual.TryGetComponent<MeshFilter>(out MeshFilter mf)) mf = visual.AddComponent<MeshFilter>();
            if (!visual.TryGetComponent<MeshRenderer>(out MeshRenderer mr)) mr = visual.AddComponent<MeshRenderer>();

            Mesh mesh = new Mesh();
            mesh.name = "Weapon_Mesh";
            List<Vector3> verts = new List<Vector3>();
            List<int> tris = new List<int>();

            AddBox(verts, tris, Vector3.zero, new Vector3(0.15f, 0.25f, 0.5f));
            AddBox(verts, tris, new Vector3(0, 0.05f, 0.4f), new Vector3(0.1f, 0.1f, 0.4f));
            AddBox(verts, tris, new Vector3(0, -0.2f, 0.1f), new Vector3(0.12f, 0.3f, 0.12f));

            mesh.vertices = verts.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();

            if (!Application.isPlaying && mf.sharedMesh != null) DestroyImmediate(mf.sharedMesh);
            mf.sharedMesh = mesh;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (mr.sharedMaterial == null || mr.sharedMaterial.shader != shader) mr.sharedMaterial = new Material(shader);
            mr.sharedMaterial.color = weaponColor;

            Transform muzTransform = visual.transform.Find("MuzzlePoint");
            if (muzTransform == null)
            {
                GameObject muz = new GameObject("MuzzlePoint");
                muz.transform.SetParent(visual.transform, false);
                muz.transform.localPosition = new Vector3(0, 0.05f, 0.6f);
                muzzlePoint = muz.transform;
            }
            else muzzlePoint = muzTransform;
        }

        private void AddBox(List<Vector3> verts, List<int> tris, Vector3 center, Vector3 size)
        {
            int vCount = verts.Count;
            Vector3 h = size * 0.5f;
            verts.Add(center + new Vector3(-h.x, -h.y, -h.z)); verts.Add(center + new Vector3(h.x, -h.y, -h.z));
            verts.Add(center + new Vector3(h.x, h.y, -h.z));   verts.Add(center + new Vector3(-h.x, h.y, -h.z));
            verts.Add(center + new Vector3(-h.x, -h.y, h.z));  verts.Add(center + new Vector3(h.x, -h.y, h.z));
            verts.Add(center + new Vector3(h.x, h.y, h.z));    verts.Add(center + new Vector3(-h.x, h.y, h.z));
            int[] cubeTris = { 0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4, 2,3,7, 2,7,6, 1,2,6, 1,6,5, 3,0,4, 3,4,7 };
            foreach (int t in cubeTris) tris.Add(vCount + t);
        }
    }
}
