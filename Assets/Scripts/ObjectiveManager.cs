using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Tracks all active ObjectiveTargets in the scene. When the last one is destroyed,
/// it activates an optional win screen and fires an event you can hook up in the Inspector.
/// Add this once, to any GameObject (e.g. your existing GameManager/CameraManager).
/// </summary>
public class ObjectiveManager : MonoBehaviour
{
    public static ObjectiveManager Instance { get; private set; }

    [Header("UI (optional)")]
    [Tooltip("Auto-updates with the remaining objective count, e.g. 'Objectives remaining: 3'.")]
    public TMP_Text remainingText;

    [Header("Win")]
    [Tooltip("Optional panel/screen that gets SetActive(true) the moment the last objective dies.")]
    public GameObject winScreen;
    [Tooltip("Fires once, the moment the last objective is destroyed. Hook up anything else you need here.")]
    public UnityEvent onAllObjectivesDestroyed;

    private readonly List<ObjectiveTarget> activeObjectives = new List<ObjectiveTarget>();
    private bool hasWon;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple ObjectiveManagers found in scene — destroying the duplicate.", this);
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void Register(ObjectiveTarget target)
    {
        if (!activeObjectives.Contains(target))
            activeObjectives.Add(target);
        UpdateUI();
    }

    public void ReportDestroyed(ObjectiveTarget target)
    {
        if (hasWon) return;

        activeObjectives.Remove(target);
        UpdateUI();

        if (activeObjectives.Count == 0)
        {
            hasWon = true;
            Win();
        }
    }

    void UpdateUI()
    {
        if (remainingText != null)
            remainingText.text = $"Objectives remaining: {activeObjectives.Count}";
    }

    void Win()
    {
        if (winScreen != null) winScreen.SetActive(true);
        onAllObjectivesDestroyed?.Invoke();
    }
}
