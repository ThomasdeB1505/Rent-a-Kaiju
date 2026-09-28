using UnityEngine;

/// <summary>
/// Put this on any destructible object that should count toward the win condition.
/// It makes the object glow (blue by default) and automatically reports itself to
/// the ObjectiveManager when destroyed.
///
/// Works out of the box as long as your existing destruction logic eventually calls
/// Destroy(gameObject) on this object — you don't need to change that code.
///
/// If instead you POOL objects (SetActive(false) rather than Destroy), call
/// ReportDestroyedOnce() yourself from your destruction script right before disabling it.
/// </summary>
[DisallowMultipleComponent]
public class ObjectiveTarget : MonoBehaviour
{
    [Header("Glow")]
    public Color glowColor = Color.blue;
    [Tooltip("HDR brightness multiplier for the emission glow.")]
    public float glowIntensity = 2.5f;
    [Tooltip("Slowly breathe the glow brightness up and down.")]
    public bool pulse = true;
    public float pulseSpeed = 2f;

    private Renderer[] renderers;
    private MaterialPropertyBlock propBlock;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private bool destroyedReported;
    private static bool appIsQuitting;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>();
        propBlock = new MaterialPropertyBlock();

        // .materials (not .sharedMaterials) instances the material per-renderer,
        // so enabling emission here doesn't affect every other object using the same material.
        foreach (var r in renderers)
        {
            if (r == null) continue;
            foreach (var mat in r.materials)
            {
                if (mat != null) mat.EnableKeyword("_EMISSION");
            }
        }

        ApplyGlow();
    }

    void Start()
    {
        if (ObjectiveManager.Instance != null)
        {
            ObjectiveManager.Instance.Register(this);
        }
        else
        {
            Debug.LogWarning($"{name}: No ObjectiveManager found in the scene — add one so this objective is tracked.", this);
        }
    }

    void Update()
    {
        if (pulse) ApplyGlow();
    }

    void ApplyGlow()
    {
        float t = pulse ? (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f : 1f; // 0..1
        float intensity = glowIntensity * Mathf.Lerp(0.4f, 1f, t);
        Color emission = glowColor * intensity;

        foreach (var r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(propBlock);
            propBlock.SetColor(EmissionColorId, emission);
            r.SetPropertyBlock(propBlock);
        }
    }

    // Called automatically when Destroy(gameObject) happens.
    void OnDestroy()
    {
        if (appIsQuitting) return;
        ReportDestroyedOnce();
    }

    // Call this manually instead if you pool/deactivate objects rather than destroying them.
    public void ReportDestroyedOnce()
    {
        if (destroyedReported) return;
        destroyedReported = true;

        if (ObjectiveManager.Instance != null)
            ObjectiveManager.Instance.ReportDestroyed(this);
    }

    void OnApplicationQuit()
    {
        appIsQuitting = true;
    }
}
