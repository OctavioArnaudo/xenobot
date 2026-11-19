using UnityEngine;
using Unity.Netcode;
using Runtime.Scripts.Audio;

// Puerta entre exterior e interior en la MISMA escena (el interior esta lejos, en otra altura).
// Entrada (exit = false): trigger en el edificio. point = ReturnPoint exterior, dest = PlayerSpawn del interior.
// Salida  (exit = true):  trigger en el interior. Devuelve al ReturnPoint de la ultima entrada (point solo dibuja el gizmo).
// Requisito: el jugador tiene tag "Player" y CharacterController o Rigidbody.
[RequireComponent(typeof(BoxCollider))]
public class SceneDoor : MonoBehaviour
{
    public bool exit;
    public Transform point;
    public Transform dest;

    public static bool Inside { get; private set; }
    public static Vector3 BackPos => backPos;

    static Vector3 backPos;
    static Quaternion backRot;
    static float until;

    void OnTriggerEnter(Collider c)
    {
        var t = c.attachedRigidbody != null ? c.attachedRigidbody.transform : c.transform;
        if (Time.time < until || !t.CompareTag("Player")) return;
        var no = t.GetComponentInParent<NetworkObject>();
        if (no != null && no.IsSpawned && !no.IsOwner) return; // solo el dueño cruza: los demas jugadores no cambian mi musica ni mi cooldown
        if (!exit && dest == null) return;
        until = Time.time + 1f;
        if (exit) { Inside = false; AudioManager.PlayMusic(Music.Exterior); Move(t, backPos, backRot); }
        else
        {
            backPos = point.position;
            backRot = point.rotation;
            Inside = true;
            AudioManager.PlayMusic(Music.Lab);
            Move(t, dest.position, dest.rotation);
        }
    }

    public static void Move(Transform t, Vector3 p, Quaternion r)
    {
        bool cc = t.TryGetComponent(out CharacterController ch);
        if (cc) ch.enabled = false;
        t.SetPositionAndRotation(p, r);
        if (t.TryGetComponent(out Rigidbody rb) && !rb.isKinematic) rb.linearVelocity = Vector3.zero;
        if (cc) ch.enabled = true;
        Physics.SyncTransforms();
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!TryGetComponent(out BoxCollider b)) return;
        Color c = exit ? new Color(1f, 0.5f, 0f) : Color.cyan;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(c.r, c.g, c.b, 0.3f);
        Gizmos.DrawCube(b.center, b.size);
        Gizmos.color = c;
        Gizmos.DrawWireCube(b.center, b.size);
        if (point == null) return;
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.DrawSphere(point.position, 0.3f);
        Gizmos.DrawRay(point.position, point.forward * 1.5f);
        Gizmos.DrawLine(transform.TransformPoint(b.center), point.position);
    }
#endif
}