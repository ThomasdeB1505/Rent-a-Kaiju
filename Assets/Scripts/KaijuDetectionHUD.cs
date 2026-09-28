using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;

/// <summary>
/// Monitors a set of cameras (independent of which one is currently displayed)
/// and shows a HUD message when the kaiju is actually visible to one or more of them.
/// e.g. "KAIJU DETECTED ON CAMERA 1 (and 3)"
///
/// Detection is done per sample point (not one big bounding box), and each point must:
///   1) fall inside the camera's on-screen viewport (not just "somewhere in the frustum"), and
///   2) have a clear, unobstructed line of sight from the camera (checked via raycast).
/// This avoids false positives from an oversized bounding box and from geometry
/// (buildings, terrain, walls) blocking the view.
/// </summary>
public class KaijuDetectionHUD : MonoBehaviour
{
    [Header("Cameras to monitor (slot 0 = Camera 1, slot 1 = Camera 2, ...)")]
    public Camera[] cameras = new Camera[9];

    [Header("Target")]
    [Tooltip("Renderers making up the kaiju. Only used to auto-generate fallback sample points if Detection Points is empty.")]
    public Renderer[] kaijuRenderers;
    [Tooltip("Optional: if Kaiju Renderers is empty, all child renderers of this transform are used.")]
    public Transform targetRoot;
    [Tooltip("RECOMMENDED for accuracy: specific points on the kaiju to test (e.g. empty GameObjects placed at the head, chest, tail tip, hands). If left empty, 9 points (center + 8 bounding-box corners) are generated automatically each check, which is a rougher approximation.")]
    public Transform[] detectionPoints;

    [Header("Occlusion")]
    [Tooltip("Layers that can block the camera's view of the kaiju (buildings, terrain, walls, etc). Do NOT include the kaiju's own layer here, or it can occlude itself.")]
    public LayerMask occlusionMask = ~0;
    [Tooltip("Small distance trimmed off the end of each occlusion raycast to avoid false hits right at the sample point's surface.")]
    public float occlusionSkin = 0.1f;

    [Header("HUD")]
    [Tooltip("TextMeshPro UI element that shows the message.")]
    public TMP_Text hudText;
    [Tooltip("Optional container (e.g. a panel/icon) to show/hide alongside the text.")]
    public GameObject hudPanel;

    [Header("Settings")]
    [Tooltip("How many times per second to re-check visibility. Lower = cheaper.")]
    public float checksPerSecond = 10f;

    private Bounds combinedBounds;
    private bool boundsValid;
    private float timer;
    private readonly List<int> detectedCameras = new List<int>();
    private readonly List<Vector3> samplePoints = new List<Vector3>(9);

    void Start()
    {
        if ((kaijuRenderers == null || kaijuRenderers.Length == 0) && targetRoot != null)
        {
            kaijuRenderers = targetRoot.GetComponentsInChildren<Renderer>();
        }

        if (hudPanel != null) hudPanel.SetActive(false);
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < 1f / Mathf.Max(checksPerSecond, 0.01f)) return;
        timer = 0f;

        if (!GatherSamplePoints())
        {
            SetHUD(null);
            return;
        }

        detectedCameras.Clear();
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam == null) continue;

            if (IsVisibleToCamera(cam))
            {
                detectedCameras.Add(i + 1); // human-readable camera number
            }
        }

        SetHUD(detectedCameras.Count > 0 ? detectedCameras : null);
    }

    /// <summary>Fills samplePoints with either the manually assigned detectionPoints, or a fallback box of 9 points.</summary>
    bool GatherSamplePoints()
    {
        samplePoints.Clear();

        if (detectionPoints != null && detectionPoints.Length > 0)
        {
            foreach (var t in detectionPoints)
            {
                if (t != null) samplePoints.Add(t.position);
            }
            return samplePoints.Count > 0;
        }

        UpdateBounds();
        if (!boundsValid) return false;

        Vector3 c = combinedBounds.center;
        Vector3 e = combinedBounds.extents;
        samplePoints.Add(c);
        samplePoints.Add(c + new Vector3(e.x, e.y, e.z));
        samplePoints.Add(c + new Vector3(e.x, e.y, -e.z));
        samplePoints.Add(c + new Vector3(e.x, -e.y, e.z));
        samplePoints.Add(c + new Vector3(e.x, -e.y, -e.z));
        samplePoints.Add(c + new Vector3(-e.x, e.y, e.z));
        samplePoints.Add(c + new Vector3(-e.x, e.y, -e.z));
        samplePoints.Add(c + new Vector3(-e.x, -e.y, e.z));
        samplePoints.Add(c + new Vector3(-e.x, -e.y, -e.z));
        return true;
    }

    void UpdateBounds()
    {
        boundsValid = false;
        if (kaijuRenderers == null) return;

        foreach (var r in kaijuRenderers)
        {
            if (r == null || !r.enabled) continue;

            if (!boundsValid)
            {
                combinedBounds = r.bounds;
                boundsValid = true;
            }
            else
            {
                combinedBounds.Encapsulate(r.bounds);
            }
        }
    }

    /// <summary>True as soon as ANY sample point is both on-screen for this camera and has a clear line of sight to it.</summary>
    bool IsVisibleToCamera(Camera cam)
    {
        Vector3 origin = cam.transform.position;

        foreach (var point in samplePoints)
        {
            Vector3 viewport = cam.WorldToViewportPoint(point);

            // Behind the camera, or outside the 0-1 on-screen rectangle: not actually in frame.
            if (viewport.z <= 0f) continue;
            if (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) continue;

            Vector3 toPoint = point - origin;
            float distance = toPoint.magnitude;
            if (distance <= occlusionSkin)
            {
                return true; // camera is essentially at the point
            }

            // If something in the occlusion mask blocks the line of sight, this point doesn't count.
            if (!Physics.Raycast(origin, toPoint.normalized, distance - occlusionSkin, occlusionMask, QueryTriggerInteraction.Ignore))
            {
                return true; // clear line of sight to an on-screen point
            }
        }

        return false;
    }

    void SetHUD(List<int> camList)
    {
        bool visible = camList != null && camList.Count > 0;

        if (hudPanel != null) hudPanel.SetActive(visible);
        if (hudText == null) return;

        if (!visible)
        {
            hudText.text = "";
            return;
        }

        var sb = new StringBuilder();
        sb.Append("KAIJU DETECTED ON CAMERA ").Append(camList[0]);

        if (camList.Count > 1)
        {
            sb.Append(" (and ");
            for (int i = 1; i < camList.Count; i++)
            {
                sb.Append(camList[i]);
                if (i < camList.Count - 1) sb.Append(", ");
            }
            sb.Append(")");
        }

        hudText.text = sb.ToString();
    }
}