using UnityEngine;

/// <summary>
/// Put this on any destructible object that should count toward the LOSE condition.
/// It makes the object glow red by default and automatically reports itself to
/// the LoseManager when destroyed.
///
/// Works out of the box as long as your existing destruction logic eventually calls
/// Destroy(gameObject) on this object.
///
/// If instead you POOL objects (SetActive(false) rather than Destroy), call
/// ReportDestroyedOnce() yourself from your destruction script right before disabling it.
/// </summary>
[DisallowMultipleComponent]
public class ForbiddenTarget : MonoBehaviour
{
    [Header("Glow")]
    public Color glowColor = Color.red;
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
        if (LoseManager.Instance == null)
        {
            Debug.LogWarning($"{name}: No LoseManager found in the scene — add one so this counts toward losing.", this);
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

        if (LoseManager.Instance != null)
            LoseManager.Instance.ReportDestroyed(this);
    }

    void OnApplicationQuit()
    {
        appIsQuitting = true;
    }
}
