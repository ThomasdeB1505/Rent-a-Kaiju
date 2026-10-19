using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene-view tools for SpawnPointBox. This file MUST be inside a folder named "Editor".
/// - Drag the red handles to change the width, the blue handles for the depth, the green handle for the height.
/// - Hold Ctrl while dragging to snap to 0.5 units.
/// - A 1 unit grid and size labels are drawn so you can read the size accurately.
/// - The thick blue edge is the front of the box (the side the building faces).
/// </summary>
[CustomEditor(typeof(SpawnPointBox))]
[CanEditMultipleObjects]
public class SpawnPointBoxEditor : Editor
{
    private const float SnapStep = 0.5f;
    private const float MinSize = 0.1f;
    private const int MaxGridLines = 100;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SpawnPointBox box = (SpawnPointBox)target;
        if (box.footprint.x <= 0f || box.footprint.y <= 0f)
        {
            EditorGUILayout.HelpBox("Set both Footprint values above 0 to see the box and its handles.",
                MessageType.Warning);
        }

        EditorGUILayout.HelpBox(
            "Drag the colored handles in the Scene view to resize the box. " +
            "Hold Ctrl to snap to " + SnapStep + " units. Grid lines are 1 unit apart. " +
            "The thick blue edge is the front.",
            MessageType.Info);
    }

    private void OnSceneGUI()
    {
        SpawnPointBox box = (SpawnPointBox)target;

        float width = box.footprint.x;
        float depth = box.footprint.y;
        if (width <= 0f || depth <= 0f) return;

        float height = Mathf.Max(box.maxHeight, 0f);
        float shownHeight = height > 0f ? height : 1f;

        Matrix4x4 oldMatrix = Handles.matrix;
        Color oldColor = Handles.color;
        Handles.matrix = box.transform.localToWorldMatrix;

        DrawGrid(width, depth);
        DrawFrontEdge(width, depth);
        DrawLabels(width, depth, height, shownHeight);

        EditorGUI.BeginChangeCheck();

        float newWidth = width;
        float newDepth = depth;
        float newHeight = height;

        Handles.color = Color.red;
        newWidth += 2f * Slide(new Vector3(width * 0.5f, shownHeight * 0.5f, 0f), Vector3.right);
        newWidth += 2f * Slide(new Vector3(-width * 0.5f, shownHeight * 0.5f, 0f), Vector3.left);

        Handles.color = Color.blue;
        newDepth += 2f * Slide(new Vector3(0f, shownHeight * 0.5f, depth * 0.5f), Vector3.forward);
        newDepth += 2f * Slide(new Vector3(0f, shownHeight * 0.5f, -depth * 0.5f), Vector3.back);

        Handles.color = Color.green;
        newHeight += Slide(new Vector3(0f, height, 0f), Vector3.up);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(box, "Resize Spawn Point Box");

            box.footprint = new Vector2(
                Clean(Mathf.Max(newWidth, MinSize)),
                Clean(Mathf.Max(newDepth, MinSize)));

            // Dragging the height handle down to the floor turns the height limit off (0).
            box.maxHeight = newHeight <= 0.0001f ? 0f : Clean(Mathf.Max(newHeight, MinSize));

            EditorUtility.SetDirty(box);
        }

        Handles.matrix = oldMatrix;
        Handles.color = oldColor;
    }

    // Draws a handle and returns how far it was dragged along 'direction' (positive = outward).
    private static float Slide(Vector3 position, Vector3 direction)
    {
        float size = HandleUtility.GetHandleSize(position) * 0.08f;
        Vector3 moved = Handles.Slider(position, direction, size, Handles.CubeHandleCap, 0f);
        return Vector3.Dot(moved - position, direction);
    }

    // Snaps to SnapStep while Ctrl is held, and always rounds to 2 decimals so the numbers stay tidy.
    private static float Clean(float value)
    {
        Event e = Event.current;
        if (e != null && e.control)
            value = Mathf.Round(value / SnapStep) * SnapStep;

        return Mathf.Round(value * 100f) / 100f;
    }

    private static void DrawGrid(float width, float depth)
    {
        if (width > MaxGridLines || depth > MaxGridLines) return;

        const float lift = 0.02f; // just above the ground so it doesn't flicker
        Handles.color = new Color(1f, 1f, 1f, 0.15f);

        for (float x = -width * 0.5f + 1f; x < width * 0.5f - 0.001f; x += 1f)
            Handles.DrawLine(new Vector3(x, lift, -depth * 0.5f), new Vector3(x, lift, depth * 0.5f));

        for (float z = -depth * 0.5f + 1f; z < depth * 0.5f - 0.001f; z += 1f)
            Handles.DrawLine(new Vector3(-width * 0.5f, lift, z), new Vector3(width * 0.5f, lift, z));
    }

    private static void DrawFrontEdge(float width, float depth)
    {
        Handles.color = Color.blue;
        Handles.DrawAAPolyLine(5f,
            new Vector3(-width * 0.5f, 0.03f, depth * 0.5f),
            new Vector3(width * 0.5f, 0.03f, depth * 0.5f));
    }

    private static void DrawLabels(float width, float depth, float height, float shownHeight)
    {
        GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 12
        };
        style.normal.textColor = Color.white;

        Handles.Label(new Vector3(0f, 0f, depth * 0.5f), "Width " + width.ToString("0.##") + "  (front)", style);
        Handles.Label(new Vector3(width * 0.5f, 0f, 0f), "Depth " + depth.ToString("0.##"), style);
        Handles.Label(new Vector3(0f, shownHeight, 0f),
            height > 0f ? "Height " + height.ToString("0.##") : "No height limit", style);
    }
}
