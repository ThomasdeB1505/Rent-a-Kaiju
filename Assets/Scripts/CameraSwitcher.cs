using UnityEngine;

/// <summary>
/// Attach this to an empty GameObject (e.g. "CameraManager").
/// Drag your cameras into the "Cameras" slots in the Inspector.
/// Element 0 = key "1", Element 1 = key "2", ... Element 8 = key "9".
/// Press the matching number key at runtime to switch the active camera.
///
/// All cameras in this switcher render to the same display (the city player's screen).
/// </summary>
public class CameraSwitcher : MonoBehaviour
{
    [Tooltip("Slot 0 = key 1, slot 1 = key 2, ... slot 8 = key 9. Leave unused slots empty.")]
    public Camera[] cameras = new Camera[9];

    [Header("Display")]
    [Tooltip("Which screen these cameras show on. 0 = main display (laptop), 1 = second display (monitor). Match this to City Display Index in KaijuDisplaySetup.")]
    public int displayIndex = 1;

    [Tooltip("If only one screen is connected, draw these cameras on the right half of the main display (matches the split-screen fallback in KaijuDisplaySetup).")]
    public bool splitScreenFallback = true;

    private int currentIndex = -1;

    void Start()
    {
        // In the Editor, Display.displays.Length is always 1, so assume a second screen there.
        // Preview it with a Game view set to "Display 2".
        bool hasSecondScreen = Application.isEditor || Display.displays.Length > displayIndex;
        int target = hasSecondScreen ? displayIndex : 0;

        // Activate the first camera that's assigned, disable the rest.
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] == null) continue;

            // Send every switchable camera to the city player's screen.
            cameras[i].targetDisplay = target;

            if (!hasSecondScreen && splitScreenFallback)
                cameras[i].rect = new Rect(0.5f, 0f, 0.5f, 1f);

            if (currentIndex == -1)
            {
                cameras[i].gameObject.SetActive(true);
                currentIndex = i;
            }
            else
            {
                cameras[i].gameObject.SetActive(false);
            }
        }
    }

    void Update()
    {
        for (int i = 0; i < cameras.Length; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                SwitchToCamera(i);
            }
        }
    }

    void SwitchToCamera(int index)
    {
        if (index == currentIndex) return;
        if (index < 0 || index >= cameras.Length || cameras[index] == null) return;

        // Enable the new camera before disabling the old one,
        // so the screen never has a frame with no camera on it.
        cameras[index].gameObject.SetActive(true);

        if (currentIndex >= 0 && cameras[currentIndex] != null)
            cameras[currentIndex].gameObject.SetActive(false);

        currentIndex = index;
    }
}