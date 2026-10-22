using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Editor tool that fills your map with spawn point empties.
///
/// It lays a grid over the map and shoots a ray straight down at every grid spot:
///   - a ray that hits the map = ground
///   - a ray that hits nothing = empty space = a road
/// For every spot on the ground it looks around for the nearest road, rotates the empty so its blue
/// arrow (forward) points at that road, and checks that the whole box fits on flat ground.
///
/// Use the buttons in the Inspector. Nothing happens at runtime; the generated empties are normal
/// objects you can still move, rotate, resize and delete by hand.
///
/// Requirements: the map model needs colliders (a MeshCollider, or "Generate Colliders" in the model's
/// import settings), because the tool uses Physics raycasts.
/// </summary>
public class SpawnPointGenerator : MonoBehaviour
{
    // Generated empties get this name prefix. "Clear" only removes objects with this prefix,
    // so rename a point (or remove the prefix) if you want to keep it when regenerating.
    public const string GeneratedPrefix = "GeneratedSpawn_";

    [Header("References")]
    [Tooltip("The parent object that holds the spawn points (the same one the BuildingSpawner uses).")]
    [SerializeField] private Transform spawnPointsParent;

    [Tooltip("The map model. Its colliders define the area that gets filled.")]
    [SerializeField] private Transform mapRoot;

    [Tooltip("Which layers count as ground. Put the map on its own layer and select only that layer here, " +
             "otherwise spawn points can end up on top of props or other objects.")]
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Grid")]
    [Tooltip("Distance between the grid spots (world units). Smaller = more spawn points, but slower.")]
    [Min(1f)][SerializeField] private float spacing = 12f;

    [Tooltip("Random shift of each spot, as a fraction of the spacing. Avoids a perfect grid look.")]
    [Range(0f, 0.5f)][SerializeField] private float randomOffset = 0.15f;

    [Tooltip("Change this number to get a different random layout.")]
    [SerializeField] private int seed = 1;

    [Header("Box (size of one building spot)")]
    [Tooltip("Width (X) and depth (Z) of the spot. The whole box must fit on the ground.")]
    [SerializeField] private Vector2 boxSize = new Vector2(10f, 10f);

    [Tooltip("Max building height for the generated boxes. 0 = no limit.")]
    [Min(0f)][SerializeField] private float maxHeight = 0f;

    [Tooltip("Add a SpawnPointBox component (with the size above) to every generated point.")]
    [SerializeField] private bool addBoxComponent = true;

    [Header("Ground")]
    [Tooltip("Spots on ground steeper than this (degrees) are skipped.")]
    [Range(0f, 60f)][SerializeField] private float maxSlope = 15f;

    [Tooltip("Every check point across the box must be within this height of the center, or the spot is skipped.")]
    [Min(0f)][SerializeField] private float maxHeightDifference = 1.5f;

    [Tooltip("Extra margin around the box that must also be ground. Keeps boxes a little away from road edges.")]
    [Min(0f)][SerializeField] private float edgeClearance = 0.5f;

    [Tooltip("Distance between the check points across the box. The whole box area is tested, so a road " +
             "narrower than this could slip between the points. Smaller = safer, but slower.")]
    [Min(0.25f)][SerializeField] private float boxCheckStep = 1f;

    [Header("Roads")]
    [Tooltip("Skip spots with no road nearby. Turn off to also fill spots far from any road (they won't be rotated).")]
    [SerializeField] private bool requireRoadNearby = true;

    [Tooltip("How far (world units) to look for a road around each spot.")]
    [Min(1f)][SerializeField] private float roadSearchDistance = 25f;

    [Tooltip("Step size while searching. Smaller = more accurate, but slower.")]
    [Min(0.25f)][SerializeField] private float roadSearchStep = 1f;

    [Tooltip("How many directions are checked around each spot. More = more accurate facing, but slower.")]
    [Range(8, 64)][SerializeField] private int roadDirections = 16;

    [Tooltip("Move spots next to a road so the front of the box sits just beside the road edge.")]
    [SerializeField] private bool snapToRoadEdge = true;

    [Tooltip("Gap (world units) left between the front of the box and the road.")]
    [Min(0f)][SerializeField] private float roadSetback = 1f;

    [Header("Spacing & Cleanup")]
    [Tooltip("Minimum distance between two spawn points. Points closer than this are skipped.")]
    [Min(0f)][SerializeField] private float minPointDistance = 8f;

    [Tooltip("Remove the previously generated points before generating again.")]
    [SerializeField] private bool clearPreviousFirst = true;

#if UNITY_EDITOR
    // Working values, set at the start of each generation.
    private Bounds area;
    private float rayTop;
    private float rayLength;

    public void Generate()
    {
        if (spawnPointsParent == null || mapRoot == null)
        {
            Debug.LogError("SpawnPointGenerator: assign both the Spawn Points Parent and the Map Root.", this);
            return;
        }

        Collider[] colliders = mapRoot.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
        {
            Debug.LogError("SpawnPointGenerator: the map has no colliders. Add a MeshCollider to the map, " +
                           "or enable 'Generate Colliders' in the model's import settings.", this);
            return;
        }

        if (groundMask.value == 0)
        {
            Debug.LogError("SpawnPointGenerator: Ground Mask is set to Nothing.", this);
            return;
        }

        Physics.SyncTransforms();

        area = colliders[0].bounds;
        foreach (Collider c in colliders)
            area.Encapsulate(c.bounds);

        rayTop = area.max.y + 1f;
        rayLength = area.size.y + 2f;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Generate spawn points");

        if (clearPreviousFirst)
            ClearGeneratedInternal();

        // Remember where points already exist (hand-placed ones) so we don't place on top of them.
        List<Vector3> taken = new List<Vector3>();
        foreach (Transform child in spawnPointsParent)
            taken.Add(child.position);

        System.Random rng = new System.Random(seed);
        int cols = Mathf.CeilToInt(area.size.x / spacing);
        int rows = Mathf.CeilToInt(area.size.z / spacing);
        int created = 0;
        bool cancelled = false;

        try
        {
            for (int row = 0; row < rows && !cancelled; row++)
            {
                cancelled = EditorUtility.DisplayCancelableProgressBar(
                    "Generating spawn points",
                    $"Row {row + 1} of {rows}  -  {created} points so far",
                    (float)row / rows);

                for (int col = 0; col < cols; col++)
                {
                    float x = area.min.x + (col + 0.5f) * spacing + Jitter(rng) * randomOffset * spacing;
                    float z = area.min.z + (row + 0.5f) * spacing + Jitter(rng) * randomOffset * spacing;

                    if (!TryBuildSpot(x, z, taken, out Vector3 position, out Quaternion rotation))
                        continue;

                    CreatePoint(created, position, rotation);
                    taken.Add(position);
                    created++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"SpawnPointGenerator: created {created} spawn points" + (cancelled ? " (cancelled early)." : "."), this);
    }

    public void ClearGenerated()
    {
        if (spawnPointsParent == null)
        {
            Debug.LogError("SpawnPointGenerator: assign the Spawn Points Parent first.", this);
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Clear generated spawn points");

        int removed = ClearGeneratedInternal();

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"SpawnPointGenerator: removed {removed} generated spawn points.", this);
    }

    private int ClearGeneratedInternal()
    {
        int removed = 0;

        for (int i = spawnPointsParent.childCount - 1; i >= 0; i--)
        {
            Transform child = spawnPointsParent.GetChild(i);
            if (child.name.StartsWith(GeneratedPrefix))
            {
                Undo.DestroyObjectImmediate(child.gameObject);
                removed++;
            }
        }

        return removed;
    }

    private void CreatePoint(int index, Vector3 position, Quaternion rotation)
    {
        GameObject go = new GameObject(GeneratedPrefix + index.ToString("000"));
        Undo.RegisterCreatedObjectUndo(go, "Generate spawn points");

        go.transform.SetParent(spawnPointsParent, true);
        go.transform.SetPositionAndRotation(position, rotation);

        if (addBoxComponent)
        {
            SpawnPointBox box = Undo.AddComponent<SpawnPointBox>(go);
            box.footprint = boxSize;
            box.maxHeight = maxHeight;
        }
    }

    // Tests one grid spot. Returns true (and the final position/rotation) if a spawn point can go here.
    private bool TryBuildSpot(float x, float z, List<Vector3> taken, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;

        if (!GroundAt(x, z, out RaycastHit hit) || !SlopeOk(hit))
            return false;

        Vector3 p = hit.point;

        // Find the nearest road (a spot where the downward ray hits nothing).
        bool foundRoad = FindRoad(p, out Vector3 roadDir, out float roadDist);

        if (!foundRoad)
        {
            if (requireRoadNearby)
                return false;
        }
        else
        {
            rotation = Quaternion.LookRotation(roadDir, Vector3.up);

            if (snapToRoadEdge)
            {
                // Move toward (or away from) the road so the front of the box ends up 'roadSetback' from its edge.
                float edgeDistance = roadDist;
                float move = edgeDistance - (boxSize.y * 0.5f + roadSetback);

                // Only spots in the first row next to the road are moved; spots further in stay where they are.
                if (move < spacing)
                {
                    move = Mathf.Max(move, -spacing);
                    Vector3 moved = p + roadDir * move;

                    if (!GroundAt(moved.x, moved.z, out hit) || !SlopeOk(hit))
                        return false;

                    p = hit.point;
                }
            }
        }

        // The whole box (plus the clearance margin) must sit on level ground: no road, no map edge.
        if (!BoxFitsOnGround(p, rotation))
            return false;

        // Keep points apart from each other (and from hand-placed ones).
        float minSqr = minPointDistance * minPointDistance;
        foreach (Vector3 other in taken)
        {
            float dx = other.x - p.x;
            float dz = other.z - p.z;
            if (dx * dx + dz * dz < minSqr)
                return false;
        }

        position = p;
        return true;
    }

    // Tests a grid of points across the whole box (plus the clearance margin around it).
    // Every point must hit ground that is roughly level with the box center, so the box can't
    // overlap a road, hang over the edge of the map, or sit on a steep step.
    private bool BoxFitsOnGround(Vector3 center, Quaternion rotation)
    {
        float halfWidth = boxSize.x * 0.5f + edgeClearance;
        float halfDepth = boxSize.y * 0.5f + edgeClearance;

        int stepsX = Mathf.Max(1, Mathf.CeilToInt(halfWidth * 2f / boxCheckStep));
        int stepsZ = Mathf.Max(1, Mathf.CeilToInt(halfDepth * 2f / boxCheckStep));

        for (int ix = 0; ix <= stepsX; ix++)
        {
            float localX = Mathf.Lerp(-halfWidth, halfWidth, (float)ix / stepsX);

            for (int iz = 0; iz <= stepsZ; iz++)
            {
                float localZ = Mathf.Lerp(-halfDepth, halfDepth, (float)iz / stepsZ);
                Vector3 point = center + rotation * new Vector3(localX, 0f, localZ);

                if (!GroundAt(point.x, point.z, out RaycastHit hit))
                    return false;

                if (Mathf.Abs(hit.point.y - center.y) > maxHeightDifference)
                    return false;
            }
        }

        return true;
    }

    // Looks around 'origin' in several directions and finds the closest spot with no ground (= a road).
    private bool FindRoad(Vector3 origin, out Vector3 bestDirection, out float bestDistance)
    {
        bestDirection = Vector3.forward;
        bestDistance = float.PositiveInfinity;

        for (int i = 0; i < roadDirections; i++)
        {
            float angle = i * Mathf.PI * 2f / roadDirections;
            Vector3 dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

            // 's < bestDistance' stops early once this direction can no longer beat the best one.
            for (float s = roadSearchStep; s <= roadSearchDistance && s < bestDistance; s += roadSearchStep)
            {
                Vector3 q = origin + dir * s;

                // The edge of the map is not a road.
                if (!InsideArea(q))
                    break;

                if (!GroundAt(q.x, q.z, out _))
                {
                    bestDistance = s;
                    bestDirection = dir;
                    break;
                }
            }
        }

        if (float.IsInfinity(bestDistance))
            return false;

        // Refine: the road edge lies between the last ground sample and the first empty one.
        float lo = Mathf.Max(0f, bestDistance - roadSearchStep);
        float hi = bestDistance;

        for (int i = 0; i < 6; i++)
        {
            float mid = (lo + hi) * 0.5f;
            Vector3 point = origin + bestDirection * mid;

            if (GroundAt(point.x, point.z, out _))
                lo = mid;
            else
                hi = mid;
        }

        bestDistance = (lo + hi) * 0.5f;
        return true;
    }

    private bool GroundAt(float x, float z, out RaycastHit hit)
    {
        return Physics.Raycast(new Vector3(x, rayTop, z), Vector3.down, out hit, rayLength,
                               groundMask, QueryTriggerInteraction.Ignore);
    }

    private bool SlopeOk(RaycastHit hit)
    {
        return Vector3.Angle(hit.normal, Vector3.up) <= maxSlope;
    }

    private bool InsideArea(Vector3 p)
    {
        return p.x >= area.min.x && p.x <= area.max.x && p.z >= area.min.z && p.z <= area.max.z;
    }

    private static float Jitter(System.Random rng)
    {
        return (float)(rng.NextDouble() * 2.0 - 1.0);
    }
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(SpawnPointGenerator))]
public class SpawnPointGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        SpawnPointGenerator generator = (SpawnPointGenerator)target;

        if (GUILayout.Button("Generate Spawn Points", GUILayout.Height(30)))
            generator.Generate();

        if (GUILayout.Button("Clear Generated Points"))
            generator.ClearGenerated();

        EditorGUILayout.HelpBox(
            "Tip: start with a large Spacing to see a quick result, then lower it. " +
            "Rename a generated point (remove the 'GeneratedSpawn_' prefix) to keep it when you generate again.",
            MessageType.Info);
    }
}
#endif