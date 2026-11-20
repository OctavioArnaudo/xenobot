// Poner en el Player (mismo objeto que el CharacterController). Quitar despues de diagnosticar.
using System.Text;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class StuckDebug : MonoBehaviour
{
    CharacterController cc;
    string lastOverlap = "", lastHit = "", screen = "";
    float hitTime;
    readonly Collider[] buf = new Collider[32];

    void Awake() => cc = GetComponent<CharacterController>();

    void OnControllerColliderHit(ControllerColliderHit h)
    {
        lastHit = $"{Path(h.collider)} | normal {h.normal:F2} | ang {Vector3.Angle(h.normal, Vector3.up):F0}";
        hitTime = Time.time;
    }

    void Update()
    {
        Vector3 c = transform.TransformPoint(cc.center);
        float s = Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
        float r = cc.radius * s + 0.05f;
        float half = Mathf.Max(cc.height * transform.lossyScale.y * 0.5f - r, 0f);
        int n = Physics.OverlapCapsuleNonAlloc(c + Vector3.up * half, c - Vector3.up * half, r, buf, ~0, QueryTriggerInteraction.Collide);

        var sb = new StringBuilder();
        for (int i = 0; i < n; i++)
        {
            var o = buf[i];
            if (o == null || o == cc || o.transform.IsChildOf(transform)) continue;
            bool ignored = Physics.GetIgnoreCollision(cc, o);
            var mc = o as MeshCollider;
            sb.AppendLine($"  {Path(o)} | {o.GetType().Name}{(mc != null ? (mc.convex ? " convex" : " NO-convex") : "")} | layer {LayerMask.LayerToName(o.gameObject.layer)} | trigger={o.isTrigger} | rb={(o.attachedRigidbody != null ? (o.attachedRigidbody.isKinematic ? "kinematic" : "dinamico") : "no")} | ignoraPlayer={ignored}");
        }

        string now = sb.ToString();
        if (now != lastOverlap)
        {
            lastOverlap = now;
            if (now.Length > 0) Debug.Log("[StuckDebug] SOLAPA:\n" + now);
        }

        screen = $"Solapa:\n{now}\nUltimo hit ({Time.time - hitTime:F1}s): {lastHit}";
    }

    void OnGUI() => GUI.Label(new Rect(10, 10, 1000, 400), screen);

    static string Path(Collider c)
    {
        string p = c.name;
        for (var t = c.transform.parent; t != null; t = t.parent) p = t.name + "/" + p;
        return p;
    }
}