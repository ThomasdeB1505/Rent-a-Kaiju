using UnityEngine;

/// <summary>
/// Put this on any destructible object that should count toward the LOSE condition.
/// It makes the object glow red by default and, when destroyed, triggers the
/// forbidden feedback (second-monitor screen shake, popup text, explosion + smoke)
/// and reports itself to the LoseManager.
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

    [Header("On Destroyed Feedback")]
    [Tooltip("Text shown on the second monitor when this object is destroyed.")]
    public string popupMessage = "!!!DANGER!!!";
    public float shakeDuration = 0.6f;
    public float shakeMagnitude = 0.3f;

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
        // Skip when quitting or when the scene is unloading (restart / scene change),
        // otherwise we'd spawn effects and report losses during teardown.
        if (appIsQuitting || !gameObject.scene.isLoaded) return;
        ReportDestroyedOnce();
    }

    // Call this manually instead if you pool/deactivate objects rather than destroying them.
    public void ReportDestroyedOnce()
    {
        if (destroyedReported) return;
        destroyedReported = true;

        // Feedback first, then the lose logic (which may pause or change scenes)
        if (ForbiddenFeedback.Instance != null)
            ForbiddenFeedback.Instance.Play(transform.position, popupMessage, shakeDuration, shakeMagnitude);

        if (LoseManager.Instance != null)
            LoseManager.Instance.ReportDestroyed(this);
    }

    void OnApplicationQuit()
    {
        appIsQuitting = true;
    }
}