using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Put this on the camera that renders to the second monitor.
/// Shakes the camera and/or any extra Transforms / UI elements you add to the list.
/// Named SecondMonitorShake to avoid clashing with any existing ScreenShake class.
/// </summary>
public class SecondMonitorShake : MonoBehaviour
{
    [System.Serializable]
    public class ShakeTarget
    {
        [Tooltip("Any Transform or RectTransform (UI element) to shake.")]
        public Transform target;

        [Tooltip("Per-target strength. 1 = normal, 0.5 = half, 2 = double.")]
        [Min(0f)] public float strength = 1f;

        [System.NonSerialized] public Vector3 originalPos;
    }

    [Header("Intensity & Duration")]
    [Tooltip("Global multiplier on shake strength. 0 = off, 1 = normal. No upper limit.")]
    [Min(0f)] public float intensity = 1f;

    [Tooltip("Global multiplier on shake length. 1 = as requested, 2 = twice as long.")]
    [Min(0.1f)] public float durationMultiplier = 1f;

    [Header("Defaults (used by the parameterless Shake())")]
    public float defaultDuration = 0.4f;
    public float defaultMagnitude = 0.2f;

    [Header("Targets")]
    [Tooltip("Shake this object (the camera) too.")]
    public bool shakeSelf = true;

    [Tooltip("Extra objects to shake. Drag in UI elements, props, anything.")]
    public List<ShakeTarget> extraTargets = new List<ShakeTarget>();

    [Tooltip("UI is measured in pixels, the camera in world units. This scales the shake for UI targets. " +
             "Try 100 so a magnitude of 0.2 becomes ~20 pixels.")]
    [Min(0f)] public float uiMagnitudeScale = 100f;

    [Header("Shape")]
    [Tooltip("Which axes shake, and how strongly. Use (1,1,0) to keep depth steady. UI only uses X and Y.")]
    public Vector3 axisWeights = new Vector3(1f, 1f, 1f);

    [Tooltip("How the shake fades over its duration. X = 0 (start) to 1 (end), Y = strength.")]
    public AnimationCurve falloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);

    [Tooltip("Smooth Perlin noise instead of jittery random offsets.")]
    public bool smooth = false;

    [Tooltip("Speed of the Perlin noise (only used when Smooth is on).")]
    public float noiseFrequency = 25f;

    private Vector3 originalLocalPos;
    private Coroutine routine;

    void Awake()
    {
        originalLocalPos = transform.localPosition;
    }

    void OnDisable()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
            RestoreAll();
        }
    }

    /// <summary>Shake using the defaults set in the Inspector.</summary>
    public void Shake()
    {
        Shake(defaultDuration, defaultMagnitude);
    }

    /// <summary>
    /// Final strength = magnitude * intensity.
    /// Final length   = duration  * durationMultiplier.
    /// </summary>
    public void Shake(float duration, float magnitude)
    {
        float finalDuration = duration * durationMultiplier;
        float finalMagnitude = magnitude * intensity;
        if (finalDuration <= 0f || finalMagnitude <= 0f) return;

        if (routine != null)
        {
            StopCoroutine(routine);
            RestoreAll();
        }

        // Capture resting positions right before shaking so targets added at runtime work too.
        foreach (var t in extraTargets)
        {
            if (t.target != null) t.originalPos = GetPos(t.target);
        }

        routine = StartCoroutine(DoShake(finalDuration, finalMagnitude));
    }

    public void AddTarget(Transform t, float strength = 1f)
    {
        extraTargets.Add(new ShakeTarget { target = t, strength = strength });
    }

    public void RemoveTarget(Transform t)
    {
        extraTargets.RemoveAll(e => e.target == t);
    }

    public void SetIntensity(float value) { intensity = Mathf.Max(0f, value); }
    public void SetDurationMultiplier(float value) { durationMultiplier = Mathf.Max(0.1f, value); }

    IEnumerator DoShake(float duration, float magnitude)
    {
        float elapsed = 0f;
        float seed = Random.value * 1000f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime; // still works if you pause on lose
            float strength = falloff.Evaluate(elapsed / duration) * magnitude;

            if (shakeSelf)
            {
                transform.localPosition = originalLocalPos + Offset(seed, elapsed, 0) * strength;
            }

            for (int i = 0; i < extraTargets.Count; i++)
            {
                var t = extraTargets[i];
                if (t.target == null) continue;

                float scale = t.strength * (t.target is RectTransform ? uiMagnitudeScale : 1f);
                SetPos(t.target, t.originalPos + Offset(seed, elapsed, i + 1) * strength * scale);
            }

            yield return null;
        }

        RestoreAll();
        routine = null;
    }

    // Each target gets its own noise lane (index) so they don't all move in lockstep.
    Vector3 Offset(float seed, float elapsed, int index)
    {
        Vector3 o;
        if (smooth)
        {
            float s = seed + index * 37f;
            float t = elapsed * noiseFrequency;
            o = new Vector3(
                Mathf.PerlinNoise(s, t) - 0.5f,
                Mathf.PerlinNoise(s + 100f, t) - 0.5f,
                Mathf.PerlinNoise(s + 200f, t) - 0.5f) * 2f;
        }
        else
        {
            o = Random.insideUnitSphere;
        }
        return Vector3.Scale(o, axisWeights);
    }

    void RestoreAll()
    {
        if (shakeSelf) transform.localPosition = originalLocalPos;
        foreach (var t in extraTargets)
        {
            if (t.target != null) SetPos(t.target, t.originalPos);
        }
    }

    // UI elements move via anchoredPosition so they behave correctly inside Canvases.
    static Vector3 GetPos(Transform t)
    {
        return t is RectTransform rt ? (Vector3)rt.anchoredPosition : t.localPosition;
    }

    static void SetPos(Transform t, Vector3 pos)
    {
        if (t is RectTransform rt) rt.anchoredPosition = pos;
        else t.localPosition = pos;
    }
}