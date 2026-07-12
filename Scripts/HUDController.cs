using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class HUDController : MonoBehaviour
{
    [Header("HUD Elements")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI qaCountText;

    private float elapsedTime = 0f;
    private int qaCount = 0;
    private bool isCounting = false;

    public void StartTimer() => isCounting = true;
    public void StopTimer() => isCounting = false;

    public void AddQACount()
    {
        qaCount++;
        if (qaCountText != null)
            qaCountText.text = qaCount.ToString();

    }

    private void Update()
    {
        if (!isCounting) return;
        elapsedTime += Time.deltaTime;

        int total = Mathf.FloorToInt(elapsedTime);
        int min = total / 60;
        int sec = total % 60;

        if (timerText != null)
            timerText.text = $"{min:00}:{sec:00}";
    }
}