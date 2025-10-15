using Mirror;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoundUI : MonoBehaviour
{
    [Header("TMP References")]
    public TMP_Text timerText;
    public TMP_Text stateText;
    public TMP_Text myValueText;
    public TMP_Text overlayText;     // Center overlay (Countdown / TIME'S UP)
    public CanvasGroup overlayGroup; // For fading

    private PlayerRound localPlayer;
    private Coroutine overlayRoutine;

    private void Start()
    {
        StartCoroutine(FindPlayer());
    }

    void Awake() { RoundManager.RoundUI = this; }

    private IEnumerator FindPlayer()
    {
        while (NetworkClient.localPlayer == null)
            yield return null;

        localPlayer = NetworkClient.localPlayer.GetComponentInChildren<PlayerRound>();
        overlayGroup.alpha = 0;
        overlayText.text = "";
    }

    private void Update()
    {
        if (RoundManager.Instance != null)
        {
            var rm = RoundManager.Instance;
            stateText.text = rm.roundState.ToString();

            if (rm.roundState != RoundState.Ended) // <--- skip when finished
            {
                float remaining = rm.roundTimeRemaining;
                int minutes = Mathf.FloorToInt(remaining / 60f);
                int seconds = Mathf.FloorToInt(remaining % 60f);
                timerText.text = $"{minutes:00}:{seconds:00}";
            }
            else
            {
                timerText.text = "00:00";
            }
        }

        if (localPlayer != null)
        {
            myValueText.text = $"Value: {localPlayer.totalValue:F0}";
        }
    }

    // Called by RoundManager's Rpc hooks
    public void PlayCountdown(int seconds)
    {
        if (overlayRoutine != null) StopCoroutine(overlayRoutine);
        overlayRoutine = StartCoroutine(CountdownRoutine(seconds));
    }

    public void PlayTimesUp()
    {
        if (overlayRoutine != null) StopCoroutine(overlayRoutine);
        overlayRoutine = StartCoroutine(TimesUpRoutine());
    }

    private IEnumerator CountdownRoutine(int seconds)
    {
        overlayGroup.alpha = 1;
        for (int i = seconds; i > 0; i--)
        {
            overlayText.text = i.ToString();

            // Determine color based on countdown position (divided into thirds)
            Color numberColor;
            int yellowThreshold = Mathf.CeilToInt(seconds * 21f / 25f);
            int redThreshold = Mathf.CeilToInt(seconds * 31f / 45f);

            if (i > yellowThreshold)
                numberColor = Color.green;
            else if (i > redThreshold)
                numberColor = Color.yellow;
            else
                numberColor = Color.red;

            StartCoroutine(AnimatePulse(overlayText, numberColor, 0.7f));
            yield return new WaitForSeconds(1f);
        }
        overlayText.text = "GO!";
        yield return AnimateGoSpinOut(overlayText);
        overlayGroup.alpha = 0;
    }

    private IEnumerator TimesUpRoutine()
    {
        overlayText.text = "TIME'S UP!!!";
        overlayText.faceColor = Color.red;
        overlayGroup.alpha = 1;
        yield return AnimatePulse(overlayText, Color.red, 1.0f);
        yield return new WaitForSeconds(1f);
        for (float t = 1; t > 0; t -= Time.deltaTime)
        {
            overlayGroup.alpha = t;
            yield return null;
        }
        overlayGroup.alpha = 0;
    }

    private IEnumerator AnimatePulse(TMP_Text text, Color color, float duration)
    {
        float elapsed = 0f;
        Vector3 startScale = Vector3.one * 1.5f;
        Vector3 endScale = Vector3.one;
        text.faceColor = color;
        text.rectTransform.localScale = startScale;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float bounce = Mathf.Sin((1f - t) * Mathf.PI * 2f) * (1f - t) * 0.2f;
            float scale = Mathf.Lerp(1.5f, 1f, t) + bounce;
            text.rectTransform.localScale = Vector3.one * scale;
            yield return null;
        }

        text.rectTransform.localScale = endScale;
    }

    private IEnumerator AnimateGoSpinOut(TMP_Text text)
    {
        text.faceColor = Color.green;
        text.rectTransform.localScale = Vector3.one * 2f;
        text.rectTransform.localRotation = Quaternion.identity;

        float pulseDuration = 0.3f;
        float spinDuration = 0.7f;
        float elapsed = 0f;

        // Big pulse in
        while (elapsed < pulseDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / pulseDuration;
            float scale = Mathf.Lerp(2f, 3.5f, Mathf.Sin(t * Mathf.PI * 0.5f));
            text.rectTransform.localScale = Vector3.one * scale;
            yield return null;
        }

        // Spin out
        elapsed = 0f;
        float startScale = 3.5f;
        while (elapsed < spinDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / spinDuration;

            // Scale down with easing
            float scale = Mathf.Lerp(startScale, 0f, t * t);
            text.rectTransform.localScale = Vector3.one * scale;

            // Spin faster as it shrinks
            float rotation = t * 720f; // 2 full rotations
            text.rectTransform.localRotation = Quaternion.Euler(0, 0, rotation);

            // Fade out
            Color c = text.faceColor;
            c.a = 1f - t;
            text.faceColor = c;

            yield return null;
        }

        // Reset for next use
        text.rectTransform.localScale = Vector3.one;
        text.rectTransform.localRotation = Quaternion.identity;
        Color resetColor = text.faceColor;
        resetColor.a = 1f;
        text.faceColor = resetColor;
    }
}