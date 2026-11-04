using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using ComicCutscenes;
using DiabolicalGames;

/// <summary>
/// Drives level progression through a list of walls.
///
/// SETUP
///  1. Put this on an empty GameObject (e.g. the same one as ObjectiveManager / CameraMonitorSystem).
///  2. Add one entry to "Gates" per wall, in the order the player meets them.
///     - A wall made of several pieces: drag the parent object into Wall Root (every DestructibleObject
///       under it is used), or add the pieces to the Walls list.
///     - Area Index + Buildings Required: the wall unlocks once that many correct buildings have been
///       destroyed IN THAT AREA. Each area counts separately, so area 0 buildings never count towards
///       an area 1 wall. 0 buildings = breakable from the start.
///     - Camera Page To Unlock: the page index passed to CameraMonitorSystem.UnlockPage (-1 = none).
///  3. The LAST gate is the final wall. Breaking it plays the comic cutscene, then ends the game.
///  4. Untick Auto Win on ObjectiveManager so the end screen only shows after the cutscene.
///
/// Walls are locked by setting the DestructibleObject's Force Required to infinity and restoring
/// the original value when the gate unlocks. Nothing in DestructibleObject needs to change.
/// </summary>
public class LevelProgressionController : MonoBehaviour
{
    [Serializable]
    public class WallGate
    {
        public string label = "Wall";

        [Tooltip("Optional. A parent object holding the wall's pieces. Every DestructibleObject under it is added to this gate automatically.")]
        public Transform wallRoot;

        [Tooltip("Optional. Individual DestructibleObjects to add to this gate (use this and/or Wall Root). A single-piece wall can just go here.")]
        public List<DestructibleObject> walls = new List<DestructibleObject>();

        [Tooltip("Any Piece = the wall counts as broken as soon as one piece breaks (the player has breached it). All Pieces = every piece must be broken.")]
        public BreakCondition brokenWhen = BreakCondition.AnyPiece;

        [Min(0), Tooltip("Which area's buildings count towards this wall (0 = first area, 1 = second area, ...). " +
                         "Must match the Area Index on that area's BuildingSpawner / ObjectiveTargets.")]
        public int areaIndex;

        [FormerlySerializedAs("totalBuildingsRequired")]
        [Min(0), Tooltip("Correct buildings that must be destroyed in this gate's area before the wall can be broken. 0 = breakable from the start.")]
        public int buildingsRequired;

        [Tooltip("Optional. Hidden when the wall unlocks (e.g. a barrier effect or 'locked' icon).")]
        public GameObject lockedVisual;

        [Tooltip("Camera page to unlock via CameraMonitorSystem.UnlockPage. -1 = none. Page 2 is index 1.")]
        public int cameraPageToUnlock = -1;

        [Tooltip("Off = the camera page unlocks as soon as this wall becomes breakable. On = it unlocks when the wall is actually broken.")]
        public bool unlockCameraPageWhenBroken;

        [Tooltip("Fires when the wall becomes breakable. Good for a sound, a message, or a glow.")]
        public UnityEvent onUnlocked;

        [Tooltip("Fires when the wall has been broken.")]
        public UnityEvent onBroken;

        [NonSerialized] public List<DestructibleObject> pieces;
        [NonSerialized] public List<float> originalForces;
        [NonSerialized] public bool unlocked;
        [NonSerialized] public bool broken;
    }

    public enum BreakCondition { AnyPiece, AllPieces }

    [Header("Walls (in order; the LAST one triggers the finale)")]
    public List<WallGate> gates = new List<WallGate>();

    [Header("Cameras")]
    public CameraMonitorSystem cameraSystem;

    [Header("Finale")]
    [Tooltip("The comic cutscene that plays after the final wall breaks. Its 'Play On Start' is forced off by this script, and this script listens to its On Finished event.")]
    public ComicCutscenePlayer cutscene;

    [Min(0f), Tooltip("Seconds between the final wall breaking and the cutscene starting, so the player sees it crash.")]
    public float cutsceneDelay = 1f;

    [Tooltip("Sets Time.timeScale to 0 while the cutscene plays, and restores it afterwards. The cutscene uses unscaled time, so it still runs. " +
             "The camera monitor system is also switched off during the cutscene so Enter doesn't zoom a feed.")]
    public bool pauseGameDuringCutscene = true;

    [Tooltip("When the cutscene finishes, call ObjectiveManager.TriggerWin() (shows its Win Screen and fires its event).")]
    public bool showObjectiveManagerWinScreen = true;

    [Tooltip("Fires once when the game ends, after the cutscene. Use it if your end screen isn't the ObjectiveManager's Win Screen.")]
    public UnityEvent onGameEnded;

    [Header("UI (optional)")]
    [Tooltip("Shows progress towards the next locked wall.")]
    public TMP_Text progressText;
    [Tooltip("{0} = buildings destroyed in the current area, {1} = buildings needed for the next wall.")]
    public string progressFormat = "Buildings destroyed: {0}/{1}";

    [Header("Debug")]
    public bool logProgress;

    ObjectiveManager manager;
    bool finaleStarted;
    bool ended;
    bool paused;
    float previousTimeScale = 1f;

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        if (cutscene != null)
        {
            cutscene.playOnStart = false; // we decide when it plays
            cutscene.onFinished.AddListener(OnCutsceneFinished);
        }

        foreach (var g in gates)
        {
            if (g == null) continue;

            g.pieces = new List<DestructibleObject>();
            g.originalForces = new List<float>();

            if (g.wallRoot != null)
                foreach (var d in g.wallRoot.GetComponentsInChildren<DestructibleObject>(true))
                    AddPiece(g, d);

            if (g.walls != null)
                foreach (var d in g.walls)
                    AddPiece(g, d);

            if (g.pieces.Count == 0)
            {
                Debug.LogWarning($"LevelProgressionController: gate '{g.label}' has no DestructibleObjects. " +
                                 "Assign Wall Root (a parent whose pieces have DestructibleObject) or add pieces to Walls.", this);
                continue;
            }

            // Infinity means neither a player hit nor a collision can ever reach the threshold.
            if (g.buildingsRequired > 0)
                foreach (var piece in g.pieces)
                    piece.ForceRequired = float.PositiveInfinity;
        }
    }

    static void AddPiece(WallGate g, DestructibleObject d)
    {
        if (d == null || g.pieces.Contains(d)) return;
        g.pieces.Add(d);
        g.originalForces.Add(d.ForceRequired);
    }

    static bool IsBroken(WallGate g)
    {
        if (g.pieces == null || g.pieces.Count == 0) return false;

        // A broken piece destroys its GameObject, and a destroyed Unity object compares equal to null.
        if (g.brokenWhen == BreakCondition.AnyPiece)
        {
            foreach (var p in g.pieces) if (p == null) return true;
            return false;
        }

        foreach (var p in g.pieces) if (p != null) return false;
        return true;
    }

    void Start()
    {
        manager = ObjectiveManager.Instance;
        if (manager != null)
            manager.ObjectiveDestroyed += OnObjectiveDestroyed;
        else
            Debug.LogWarning("LevelProgressionController: no ObjectiveManager in the scene, so no wall can ever unlock (except ones needing 0 buildings).", this);

        EvaluateGates(); // unlocks any gate that needs 0 buildings
    }

    void Update()
    {
        if (finaleStarted) return;

        // DestructibleObject has no destroy event, but breaking it destroys the object,
        // and a destroyed Unity object compares equal to null.
        for (int i = 0; i < gates.Count; i++)
        {
            var g = gates[i];
            if (g == null || g.broken) continue;
            if (IsBroken(g)) OnWallBroken(g, i);
            if (finaleStarted) return;
        }
    }

    void OnDestroy()
    {
        if (manager != null) manager.ObjectiveDestroyed -= OnObjectiveDestroyed;
        if (cutscene != null) cutscene.onFinished.RemoveListener(OnCutsceneFinished);
        RestoreTimeScale();
    }

    // ------------------------------------------------------------------ gates

    void OnObjectiveDestroyed(int totalDestroyed)
    {
        EvaluateGates();
    }

    void EvaluateGates()
    {
        foreach (var g in gates)
        {
            if (g == null || g.unlocked) continue;
            if (DestroyedInArea(g) >= g.buildingsRequired) Unlock(g);
        }

        UpdateProgress();
    }

    void Unlock(WallGate g)
    {
        if (g.unlocked) return;
        g.unlocked = true;
        Log($"Unlocked '{g.label}'");

        if (g.pieces != null)
            for (int i = 0; i < g.pieces.Count; i++)
                if (g.pieces[i] != null) g.pieces[i].ForceRequired = g.originalForces[i];
        if (g.lockedVisual != null) g.lockedVisual.SetActive(false);
        if (!g.unlockCameraPageWhenBroken) UnlockCameraPage(g);

        g.onUnlocked?.Invoke();
    }

    void OnWallBroken(WallGate g, int index)
    {
        g.broken = true;
        Log($"Broken '{g.label}'");

        if (!g.unlocked) Unlock(g); // broken some other way (e.g. a direct Break() call)
        if (g.unlockCameraPageWhenBroken) UnlockCameraPage(g);

        g.onBroken?.Invoke();

        if (index == gates.Count - 1)
            StartCoroutine(FinaleRoutine());
    }

    void UnlockCameraPage(WallGate g)
    {
        if (g.cameraPageToUnlock < 0 || cameraSystem == null) return;
        cameraSystem.UnlockPage(g.cameraPageToUnlock);
        Log($"Unlocked camera page {g.cameraPageToUnlock}");
    }

    int DestroyedInArea(WallGate g)
    {
        return manager != null ? manager.GetDestroyedInArea(g.areaIndex) : 0;
    }

    void UpdateProgress()
    {
        if (progressText == null) return;

        foreach (var g in gates)
        {
            if (g == null || g.unlocked) continue;
            progressText.text = string.Format(progressFormat, DestroyedInArea(g), g.buildingsRequired);
            return;
        }

        progressText.text = string.Empty; // every wall is open
    }

    // ------------------------------------------------------------------ finale

    IEnumerator FinaleRoutine()
    {
        finaleStarted = true;
        Log("Final wall broken, starting finale");

        if (cutsceneDelay > 0f) yield return new WaitForSeconds(cutsceneDelay);

        if (cutscene == null)
        {
            Debug.LogWarning("LevelProgressionController: no cutscene assigned, ending the game immediately.", this);
            EndGame();
            yield break;
        }

        if (cameraSystem != null) cameraSystem.enabled = false;

        if (pauseGameDuringCutscene)
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            paused = true;
        }

        cutscene.Play();
    }

    void OnCutsceneFinished()
    {
        if (finaleStarted) EndGame();
    }

    void EndGame()
    {
        if (ended) return;
        ended = true;
        Log("Game ended");

        RestoreTimeScale(); // so a restart/scene reload doesn't begin frozen

        if (showObjectiveManagerWinScreen && manager != null) manager.TriggerWin();
        onGameEnded?.Invoke();
    }

    void RestoreTimeScale()
    {
        if (!paused) return;
        paused = false;
        Time.timeScale = previousTimeScale;
    }

    void Log(string message)
    {
        if (logProgress) Debug.Log($"[LevelProgression] {message}", this);
    }
}