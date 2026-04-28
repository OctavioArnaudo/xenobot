using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

using Unity.Multiplayer.Center.NetcodeForGameObjectsExample;

public class PlayerMovement : ClientAuthoritativeMovement
{
    public new float Speed = 5;

    void Update()
    {
        // IsOwner will also work in a distributed-authoritative scenario as the owner 
        // has the Authority to update the object.
        if (!IsOwner || !IsSpawned) return;

        var multiplier = Speed * Time.deltaTime;

#if ENABLE_INPUT_SYSTEM
            // New input system backends are enabled.
            if (Keyboard.current.aKey.isPressed)
            {
                transform.position += new Vector3(-multiplier, 0, 0);
            }
            else if (Keyboard.current.dKey.isPressed)
            {
                transform.position += new Vector3(multiplier, 0, 0);
            }
            else if (Keyboard.current.wKey.isPressed)
            {
                transform.position += new Vector3(0, 0, multiplier);
            }
            else if (Keyboard.current.sKey.isPressed)
            {
                transform.position += new Vector3(0, 0, -multiplier);
            }
#else
        // Old input backends are enabled.
        if (Input.GetKey(KeyCode.A))
        {
            transform.position += new Vector3(-multiplier, 0, 0);
        }
        else if (Input.GetKey(KeyCode.D))
        {
            transform.position += new Vector3(multiplier, 0, 0);
        }
        else if (Input.GetKey(KeyCode.W))
        {
            transform.position += new Vector3(0, 0, multiplier);
        }
        else if (Input.GetKey(KeyCode.S))
        {
            transform.position += new Vector3(0, 0, -multiplier);
        }
#endif
    }
}