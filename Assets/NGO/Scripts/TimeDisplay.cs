using UnityEngine;
using TMPro;

public class TimeDisplay : MonoBehaviour
{
    public TextMeshProUGUI timeText;

    void Update()
    {
        if (timeText != null)
        {
            timeText.text = "Time: " + Time.time.ToString("F2");
        }
    }
}
