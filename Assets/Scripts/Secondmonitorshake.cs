using System.Collections;
using UnityEngine;

/// <summary>
/// Put this on the camera that renders to the second monitor.
/// Named SecondMonitorShake to avoid clashing with any existing ScreenShake class.
/// </summary>
public class SecondMonitorShake : MonoBehaviour
{
    private Vector3 originalLocalPos;
    private Coroutine routine;

    void Awake()
    {
        originalLocalPos = transform.localPosition;
    }

    public void Shake(float duration, float magnitude)
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            transform.localPosition = originalLocalPos;
        }
        routine = StartCoroutine(DoShake(duration, magnitude));
    }

    IEnumerator DoShake(float duration, float magnitude)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime; // still works if you pause on lose
            float falloff = 1f - (elapsed / duration);
            transform.localPosition = originalLocalPos + Random.insideUnitSphere * magnitude * falloff;
            yield return null;
        }
        transform.localPosition = originalLocalPos;
        routine = null;
    }
}