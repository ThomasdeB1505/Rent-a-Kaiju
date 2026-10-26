using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Scene-wide handler for "you destroyed a forbidden object" feedback:
/// second-monitor screen shake, popup text, and explosion + smoke at the object.
/// </summary>
public class ForbiddenFeedback : MonoBehaviour
{
    public static ForbiddenFeedback Instance { get; private set; }

    [Header("Second Monitor")]
    [Tooltip("Turns on Display 2 in builds (the Editor uses the Game view's Display dropdown).")]
    public bool activateSecondDisplay = true;
    [Tooltip("SecondMonitorShake component on the camera that renders to the second monitor.")]
    public SecondMonitorShake secondMonitorShake;
    [Tooltip("Text on a Canvas whose Target Display is Display 2.")]
    public TMP_Text popupText;
    public float popupDuration = 3f;
    public float popupFadeTime = 0.5f;

    [Header("World Effects")]
    public GameObject explosionPrefab;
    public GameObject smokePrefab;
    [Tooltip("Spawned effects are destroyed after this many seconds.")]
    public float effectLifetime = 6f;

    private Coroutine popupRoutine;

    void Awake()
    {
        Instance = this;

        if (activateSecondDisplay && Display.displays.Length > 1)
            Display.displays[1].Activate();

        if (popupText != null) popupText.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Play(Vector3 position, string message, float shakeDuration, float shakeMagnitude)
    {
        if (explosionPrefab != null)
            Destroy(Instantiate(explosionPrefab, position, Quaternion.identity), effectLifetime);

        if (smokePrefab != null)
            Destroy(Instantiate(smokePrefab, position, Quaternion.identity), effectLifetime);

        if (secondMonitorShake != null)
            secondMonitorShake.Shake(shakeDuration, shakeMagnitude);

        if (popupText != null)
        {
            if (popupRoutine != null) StopCoroutine(popupRoutine);
            popupRoutine = StartCoroutine(ShowPopup(message));
        }
    }

    IEnumerator ShowPopup(string message)
    {
        popupText.text = message;
        popupText.gameObject.SetActive(true);
        SetAlpha(1f);

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, popupDuration - popupFadeTime));

        float t = 0f;
        while (t < popupFadeTime)
        {
            t += Time.unscaledDeltaTime;
            SetAlpha(1f - t / popupFadeTime);
            yield return null;
        }

        popupText.gameObject.SetActive(false);
        popupRoutine = null;
    }

    void SetAlpha(float a)
    {
        Color c = popupText.color;
        c.a = a;
        popupText.color = c;
    }
}