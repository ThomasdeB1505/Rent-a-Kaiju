using UnityEngine;

/// <summary>
/// Activates a second display and routes each player's camera (and UI) to its own screen.
/// Put this on an empty GameObject in your first scene.
///
/// Display 0 = main display (usually the laptop screen)
/// Display 1 = external monitor
/// Which physical screen counts as "main" follows your OS display settings.
/// </summary>
public class KaijuDisplaySetup : MonoBehaviour
{
    [Header("Cameras")]
    [Tooltip("Camera for the player controlling the kaiju.")]
    public Camera kaijuCamera;

    [Tooltip("Camera for the player who watches the kaiju and the city.")]
    public Camera cityCamera;

    [Header("UI (optional)")]
    [Tooltip("Screen Space - Overlay canvas for the kaiju player's HUD.")]
    public Canvas kaijuCanvas;

    [Tooltip("Screen Space - Overlay canvas for the city player's HUD.")]
    public Canvas cityCanvas;

    [Header("Which screen shows what")]
    [Tooltip("0 = main display (laptop), 1 = second display (monitor).")]
    public int kaijuDisplayIndex = 0;
    public int cityDisplayIndex = 1;

    void Awake()
    {
        int available = Display.displays.Length;
        Debug.Log($"[KaijuDisplaySetup] Displays connected: {available}");

        // Display 0 is always active. Activate the rest that we need.
        // Note: Activate() only works in a standalone build, not in the Editor.
        for (int i = 1; i < available; i++)
        {
            if (i == kaijuDisplayIndex || i == cityDisplayIndex)
            {
                // Use the monitor's native resolution.
                Display.displays[i].Activate();
            }
        }

        // If only one screen is connected, fall back to showing both on the main display
        // so the game is still playable (you could also switch to split-screen here).
        int kaijuTarget = kaijuDisplayIndex < available ? kaijuDisplayIndex : 0;
        int cityTarget  = cityDisplayIndex  < available ? cityDisplayIndex  : 0;

        if (available < 2)
        {
            Debug.LogWarning("[KaijuDisplaySetup] Only one display found. Using split-screen fallback.");
            if (kaijuCamera) kaijuCamera.rect = new Rect(0f, 0f, 0.5f, 1f);
            if (cityCamera)  cityCamera.rect  = new Rect(0.5f, 0f, 0.5f, 1f);
        }

        if (kaijuCamera) kaijuCamera.targetDisplay = kaijuTarget;
        if (cityCamera)  cityCamera.targetDisplay  = cityTarget;

        if (kaijuCanvas) kaijuCanvas.targetDisplay = kaijuTarget;
        if (cityCanvas)  cityCanvas.targetDisplay  = cityTarget;
    }

    /// <summary>
    /// Returns which display the mouse is currently on (-1 if unknown).
    /// Useful if one player uses the mouse and you need to know which screen they're clicking on.
    /// </summary>
    public static int GetMouseDisplay(out Vector2 positionOnThatDisplay)
    {
        Vector3 rel = Display.RelativeMouseAt(Input.mousePosition);
        positionOnThatDisplay = new Vector2(rel.x, rel.y);

        // RelativeMouseAt returns Vector3.zero on platforms that don't support it (e.g. in the Editor).
        if (rel == Vector3.zero) return -1;
        return (int)rel.z;
    }
}
