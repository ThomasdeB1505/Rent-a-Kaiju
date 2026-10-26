using UnityEngine;
using DiabolicalGames;

[DisallowMultipleComponent]
public class ObjectiveTarget : MonoBehaviour
{
    [Header("Settings Source")]
    [Tooltip("If on, values below are pulled from the scene's ObjectiveSettings. Turn off to use this prefab's own values.")]
    public bool useGlobalSettings = true;

    [Header("Breaking")]
    [Tooltip("If on, this value replaces the Force Required on the DestructibleObject.")]
    public bool overrideForceRequired = true;
    public float forceRequired = 5f;

    [Header("Glow")]
    public Color glowColor = Color.blue;
    public float glowIntensity = 2.5f;
    public bool pulse = true;
    public float pulseSpeed = 2f;

    private Renderer[] renderers;
    private MaterialPropertyBlock propBlock;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private DestructibleObject destructible;
    private bool destroyedReported;
    private static bool appIsQuitting;

    void Awake()
    {
        destructible = GetComponent<DestructibleObject>();
        if (destructible == null) destructible = GetComponentInParent<DestructibleObject>();

        renderers = GetComponentsInChildren<Renderer>();
        propBlock = new MaterialPropertyBlock();

        foreach (var r in renderers)
        {
            if (r == null) continue;
            foreach (var mat in r.materials)
            {
                if (mat != null) mat.EnableKeyword("_EMISSION");
            }
        }

        ApplySettings();
    }

    void OnEnable() { ObjectiveSettings.Changed += ApplySettings; }
    void OnDisable() { ObjectiveSettings.Changed -= ApplySettings; }

    /// Pulls values from ObjectiveSettings (if enabled) and applies them.
    public void ApplySettings()
    {
        if (useGlobalSettings)
        {
            var s = ObjectiveSettings.Instance;
            if (s != null)
            {
                forceRequired = s.forceRequired;
                glowColor = s.glowColor;
                glowIntensity = s.glowIntensity;
                pulse = s.pulse;
                pulseSpeed = s.pulseSpeed;
            }
            else
            {
                Debug.LogWarning($"{name}: useGlobalSettings is on but no ObjectiveSettings exists in the scene.", this);
            }
        }

        if (overrideForceRequired)
        {
            if (destructible != null)
                destructible.ForceRequired = forceRequired;
            else
                Debug.LogWarning($"{name}: No DestructibleObject found, so Force Required can't be applied.", this);
        }

        ApplyGlow();
    }

    void Start()
    {
        if (ObjectiveManager.Instance != null)
            ObjectiveManager.Instance.Register(this);
        else
            Debug.LogWarning($"{name}: No ObjectiveManager found in the scene — add one so this objective is tracked.", this);
    }

    void Update()
    {
        if (pulse) ApplyGlow();
    }

    public void SetForceRequired(float value)
    {
        forceRequired = value;
        if (destructible != null) destructible.ForceRequired = value;
    }

    void ApplyGlow()
    {
        if (renderers == null) return;

        float t = pulse ? (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f : 1f;
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

    void OnDestroy()
    {
        if (appIsQuitting) return;
        ReportDestroyedOnce();
    }

    public void ReportDestroyedOnce()
    {
        if (destroyedReported) return;
        destroyedReported = true;

        if (ObjectiveManager.Instance != null)
            ObjectiveManager.Instance.ReportDestroyed(this);
    }

    void OnApplicationQuit() { appIsQuitting = true; }
}