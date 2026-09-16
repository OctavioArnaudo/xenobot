using UnityEngine;

public class PlayerFXController : MonoBehaviour {
    // Arrastra aquí tu sistema de partículas desde el inspector
    public ParticleSystem chispasFX;

    // Este método será llamado por la animación
    public void PlayChispas() {
        if (chispasFX != null) {
            chispasFX.Play();
        }
    }
}