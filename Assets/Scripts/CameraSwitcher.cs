using UnityEngine;

/// <summary>
/// Attach this to an empty GameObject (e.g. "CameraManager").
/// Drag your cameras into the "Cameras" slots in the Inspector.
/// Element 0 = key "1", Element 1 = key "2", ... Element 8 = key "9".
/// Press the matching number key at runtime to switch the active camera.
/// </summary>
public class CameraSwitcher : MonoBehaviour
{
    [Tooltip("Slot 0 = key 1, slot 1 = key 2, ... slot 8 = key 9. Leave unused slots empty.")]
    public Camera[] cameras = new Camera[9];

    private int currentIndex = -1;

    void Start()
    {
        // Activate the first camera that's assigned, disable the rest.
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] == null) continue;

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

        if (currentIndex >= 0 && cameras[currentIndex] != null)
            cameras[currentIndex].gameObject.SetActive(false);

        cameras[index].gameObject.SetActive(true);
        currentIndex = index;
    }
}
