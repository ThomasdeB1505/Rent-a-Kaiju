using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shakes a UI element (e.g. a Button) a little bit.
/// Attach to the Button's GameObject. Call Shake() from code,
/// or tick "Shake On Click" to shake whenever the button is pressed.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ButtonShake : MonoBehaviour
{
    [Header("Shake Settings")]
    [Tooltip("How long the shake lasts, in seconds.")]
    public float duration = 0.3f;

    [Tooltip("Maximum offset in pixels.")]
    public float strength = 8f;

    [Tooltip("How many shakes per second.")]
    public float frequency = 30f;

    [Tooltip("Also wobble the rotation a bit (degrees). 0 = off.")]
    public float rotationStrength = 3f;

    [Tooltip("Use unscaled time so it still works when the game is paused (Time.timeScale = 0).")]
    public bool useUnscaledTime = true;

    [Tooltip("Automatically shake when the attached Button is clicked.")]
    public bool shakeOnClick = false;

    private RectTransform rect;
    private Vector2 originalPos;
    private Quaternion originalRot;
    private Coroutine shakeRoutine;

    void Awake()
    {
        rect = GetComponent<RectTransform>();
        originalPos = rect.anchoredPosition;
        originalRot = rect.localRotation;

        if (shakeOnClick)
        {
            var button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(Shake);
        }
    }

    /// <summary>Start (or restart) the shake.</summary>
    public void Shake()
    {
        if (shakeRoutine != null)
        {
            StopCoroutine(shakeRoutine);
            ResetTransform();
        }
        shakeRoutine = StartCoroutine(ShakeRoutine());
    }

    private IEnumerator ShakeRoutine()
    {
        float elapsed = 0f;
        float seed = Random.value * 100f;

        while (elapsed < duration)
        {
            elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

            // Fade out over time so it settles smoothly
            float damper = 1f - Mathf.Clamp01(elapsed / duration);
            float t = elapsed * frequency;

            // Perlin noise gives a smoother, more natural jiggle than pure random
            float x = (Mathf.PerlinNoise(seed, t) * 2f - 1f) * strength * damper;
            float y = (Mathf.PerlinNoise(seed + 10f, t) * 2f - 1f) * strength * damper;
            float r = (Mathf.PerlinNoise(seed + 20f, t) * 2f - 1f) * rotationStrength * damper;

            rect.anchoredPosition = originalPos + new Vector2(x, y);
            rect.localRotation = originalRot * Quaternion.Euler(0f, 0f, r);

            yield return null;
        }

        ResetTransform();
        shakeRoutine = null;
    }

    private void ResetTransform()
    {
        rect.anchoredPosition = originalPos;
        rect.localRotation = originalRot;
    }

    void OnDisable()
    {
        // Make sure the button never gets stuck offset
        if (shakeRoutine != null)
        {
            StopCoroutine(shakeRoutine);
            shakeRoutine = null;
        }
        if (rect != null) ResetTransform();
    }
}
