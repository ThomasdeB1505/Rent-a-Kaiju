using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Tracks how many ForbiddenTargets have been destroyed. When the count hits
/// loseThreshold, it activates an optional lose screen and fires an event.
/// Add this once, to any GameObject in the scene.
/// </summary>
public class LoseManager : MonoBehaviour
{
    public static LoseManager Instance { get; private set; }

    [Header("Lose Condition")]
    [Tooltip("How many forbidden objects the player can destroy before losing.")]
    public int loseThreshold = 3;

    [Header("UI (optional)")]
    [Tooltip("Auto-updates, e.g. 'Danger objects destroyed: 2/3'.")]
    public TMP_Text destroyedCountText;

    [Header("Lose")]
    [Tooltip("Optional panel/screen that gets SetActive(true) the moment the threshold is hit.")]
    public GameObject loseScreen;
    [Tooltip("Fires once, the moment the lose threshold is reached.")]
    public UnityEvent onLose;

    private int destroyedCount;
    private bool hasLost;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple LoseManagers found in scene — destroying the duplicate.", this);
            Destroy(gameObject);
            return;
        }
        Instance = this;
        UpdateUI();
    }

    public void ReportDestroyed(ForbiddenTarget target)
    {
        if (hasLost) return;

        destroyedCount++;
        UpdateUI();

        if (destroyedCount >= loseThreshold)
        {
            hasLost = true;
            Lose();
        }
    }

    void UpdateUI()
    {
        if (destroyedCountText != null)
            destroyedCountText.text = $"Danger objects destroyed: {destroyedCount}/{loseThreshold}";
    }

    void Lose()
    {
        if (loseScreen != null) loseScreen.SetActive(true);
        onLose?.Invoke();
    }
}
