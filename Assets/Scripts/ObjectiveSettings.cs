using UnityEngine;

/// Central tuning for every ObjectiveTarget, including ones spawned at runtime.
/// Edit these values in the Inspector, even while the game is running.
[DefaultExecutionOrder(-100)] // make sure it exists before spawned buildings run Awake
public class ObjectiveSettings : MonoBehaviour
{
    public static ObjectiveSettings Instance { get; private set; }

    /// Fired when you change a value in the Inspector or call NotifyChanged().
    public static event System.Action Changed;

    [Header("Breaking")]
    public float forceRequired = 5f;

    [Header("Glow")]
    public Color glowColor = Color.blue;
    public float glowIntensity = 2.5f;
    public bool pulse = true;
    public float pulseSpeed = 2f;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Runs whenever you edit a value in the Inspector (including in Play mode),
    // so existing buildings update live.
    void OnValidate()
    {
        if (Application.isPlaying) Changed?.Invoke();
    }

    /// Call this from code (difficulty menu, upgrades) after changing values.
    public void NotifyChanged() => Changed?.Invoke();
}