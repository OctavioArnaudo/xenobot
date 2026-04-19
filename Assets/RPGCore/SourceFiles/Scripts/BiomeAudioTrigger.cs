using UnityEngine;

public class BiomeAudioTrigger : MonoBehaviour
{
    public enum Bioma { BiomaA, BiomaB }

    [Header("Bioma de este trigger")]
    public Bioma bioma;

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        if (bioma == Bioma.BiomaA)
            AudioManager.Instance.Play(AudioManager.Instance.srcBiomaA);
        else
            AudioManager.Instance.Play(AudioManager.Instance.srcBiomaB);
    }
}