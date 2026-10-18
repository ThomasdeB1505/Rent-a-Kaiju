
using UnityEngine;

public class FourViewHologram : MonoBehaviour
{
    public Transform mapTarget;

    public Camera[] cameras = new Camera[4];
    public RenderTexture[] renderTextures =
        new RenderTexture[4];

    public float cameraDistance = 10f;
    public float cameraHeight = 2f;
    public float fieldOfView = 40f;

    void Start()
    {
        if (mapTarget == null ||
            cameras.Length != 4 ||
            renderTextures.Length != 4)
        {
            Debug.LogError(
                "Vul de Map Target, vier cameras " +
                "en vier RenderTextures in!"
            );
            enabled = false;
            return;
        }

        for (int i = 0; i < 4; i++)
        {
            if (cameras[i] == null ||
                renderTextures[i] == null)
            {
                Debug.LogError(
                    "Er ontbreekt een camera of texture!"
                );
                enabled = false;
                return;
            }

            cameras[i].targetTexture = renderTextures[i];
            cameras[i].fieldOfView = fieldOfView;
            cameras[i].clearFlags = CameraClearFlags.SolidColor;
            cameras[i].backgroundColor = Color.black;
        }
    }

    void LateUpdate()
    {
        if (mapTarget == null) return;

        for (int i = 0; i < 4; i++)
        {
            float angle = i * 90f;

            Vector3 direction =
                Quaternion.Euler(0f, angle, 0f)
                * Vector3.forward;

            cameras[i].transform.position =
                mapTarget.position
                + direction * cameraDistance
                + Vector3.up * cameraHeight;

            cameras[i].transform.LookAt(mapTarget.position);
        }
    }
}
