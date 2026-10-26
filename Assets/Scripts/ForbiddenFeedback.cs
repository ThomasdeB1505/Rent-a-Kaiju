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
    [Tooltip("Multiplies the explosion prefab's own scale. 1 = original size.")]
    public Vector3 explosionScale = Vector3.one;
    public GameObject smokePrefab;
    [Tooltip("Multiplies the smoke prefab's own scale. 1 = original size.")]
    public Vector3 smokeScale = Vector3.one;
    [Tooltip("If on, the effects are also scaled by the destroyed object's size (bigger object = bigger effect).")]
    public bool scaleWithObjectSize = false;
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
        Play(position, Vector3.one, message, shakeDuration, shakeMagnitude);
    }

    /// <param name="objectScale">Scale of the destroyed object, only used when Scale With Object Size is on.</param>
    public void Play(Vector3 position, Vector3 objectScale, string message, float shakeDuration, float shakeMagnitude)
    {
        Vector3 extra = scaleWithObjectSize ? objectScale : Vector3.one;

        SpawnEffect(explosionPrefab, position, explosionScale, extra);
        SpawnEffect(smokePrefab, position, smokeScale, extra);

        if (secondMonitorShake != null)
            secondMonitorShake.Shake(shakeDuration, shakeMagnitude);

        if (popupText != null)
        {
            if (popupRoutine != null) StopCoroutine(popupRoutine);
            popupRoutine = StartCoroutine(ShowPopup(message));
        }
    }

    void SpawnEffect(GameObject prefab, Vector3 position, Vector3 scale, Vector3 extra)
    {
        if (prefab == null) return;

        GameObject fx = Instantiate(prefab, position, Quaternion.identity);
        // Multiply the prefab's own scale so its original size is kept as the baseline
        fx.transform.localScale = Vector3.Scale(Vector3.Scale(fx.transform.localScale, scale), extra);
        Destroy(fx, effectLifetime);
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