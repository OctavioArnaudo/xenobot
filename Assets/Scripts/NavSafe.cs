using UnityEngine;
using UnityEngine.AI;

// Coloca/activa un NavMeshAgent solo si hay NavMesh cerca. Sin asignaciones, sin spam de warnings.
public static class NavSafe
{
    // nextTry: campo float del llamador (0 al inicio). Evita reintentar cada frame.
    public static bool TryPlace(NavMeshAgent a, Vector3 pos, ref float nextTry, float range = 4f, float retry = 2f)
    {
        if (Time.time < nextTry) return false;

        var f = new NavMeshQueryFilter { agentTypeID = a.agentTypeID, areaMask = NavMesh.AllAreas }; // struct: 0 GC
        if (!NavMesh.SamplePosition(pos, out var hit, range, f))
        {
            nextTry = Time.time + retry;   // fuera del NavMesh: queda apagado y reintenta luego
            return false;
        }

        if (a.enabled && a.isOnNavMesh) return a.Warp(hit.position);

        a.transform.position = hit.position + Vector3.up * a.baseOffset;
        a.enabled = true;                  // ya hay NavMesh cerca: no genera warning
        return a.isOnNavMesh;
    }
}
