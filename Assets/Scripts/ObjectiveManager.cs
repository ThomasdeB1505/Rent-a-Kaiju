using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Tracks all active ObjectiveTargets in the scene.
/// Also counts how many have been destroyed and raises ObjectiveDestroyed each time,
/// which LevelProgressionController uses to unlock walls.
/// Add this once, to any GameObject (e.g. your existing GameManager/CameraManager).
/// </summary>
public class ObjectiveManager : MonoBehaviour
{
    public static ObjectiveManager Instance { get; private set; }

    [Header("UI (optional)")]
    [Tooltip("Auto-updates with the remaining objective count, e.g. 'Objectives remaining: 3'.")]
    public TMP_Text remainingText;

    [Header("Win")]
    [Tooltip("If on, the win screen shows the moment the last objective dies (the old behaviour). " +
             "Turn this OFF when using LevelProgressionController, so the game ends after the final cutscene instead.")]
    public bool autoWin = true;
    [Tooltip("Optional panel/screen that gets SetActive(true) when the game is won.")]
    public GameObject winScreen;
    [Tooltip("Fires once, when the game is won. Hook up anything else you need here.")]
    public UnityEvent onAllObjectivesDestroyed;

    /// <summary>How many objectives have been destroyed so far.</summary>
    public int DestroyedCount { get; private set; }

    /// <summary>How many objectives have been destroyed in one area (see ObjectiveTarget.areaIndex).</summary>
    public int GetDestroyedInArea(int area)
    {
        return destroyedPerArea.TryGetValue(area, out int n) ? n : 0;
    }

    /// <summary>How many objectives are still standing.</summary>
    public int RemainingCount => activeObjectives.Count;

    /// <summary>Raised after every destroyed objective. The argument is the total destroyed so far.</summary>
    public event Action<int> ObjectiveDestroyed;

    private readonly List<ObjectiveTarget> activeObjectives = new List<ObjectiveTarget>();
    private readonly Dictionary<int, int> destroyedPerArea = new Dictionary<int, int>();
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

        // Only count targets that were registered, and only once each.
        if (!activeObjectives.Remove(target)) return;

        DestroyedCount++;
        destroyedPerArea[target.areaIndex] = GetDestroyedInArea(target.areaIndex) + 1;
        UpdateUI();
        ObjectiveDestroyed?.Invoke(DestroyedCount);

        if (autoWin && activeObjectives.Count == 0)
        {
            hasWon = true;
            Win();
        }
    }

    /// <summary>Shows the win screen and fires onAllObjectivesDestroyed. Safe to call more than once.</summary>
    public void TriggerWin()
    {
        if (hasWon) return;
        hasWon = true;
        Win();
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