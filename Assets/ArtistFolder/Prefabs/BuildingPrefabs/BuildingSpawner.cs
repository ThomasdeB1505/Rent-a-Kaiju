using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

/// <summary>
/// One building prefab plus the rotation needed to make its front face the spawn point's forward (blue arrow).
/// </summary>
[Serializable]
public class BuildingEntry
{
    public GameObject prefab;

    [Tooltip("Rotation (degrees) applied on top of the spawn point's rotation. " +
             "Usually only Y is needed, e.g. 90, 180 or -90.")]
    public Vector3 rotationOffset = Vector3.zero;
}

/// <summary>
/// Spawns a random building on every child of 'spawnPointsParent'.
/// Then picks random spawn points and adds the ObjectiveTarget component to
/// 'objectiveCount' of them and the ForbiddenTarget component to 'forbiddenCount' of them.
/// Any building prefab can become an objective or a forbidden building.
/// </summary>
public class BuildingSpawner : MonoBehaviour
{
    [Header("Spawn Points")]
    [Tooltip("Parent object whose direct children are the spawn point empties.")]
    [SerializeField] private Transform spawnPointsParent;

    [Header("Buildings")]
    [FormerlySerializedAs("normalBuildings")]
    [SerializeField] private BuildingEntry[] buildings;

    [Header("Counts")]
    [Tooltip("How many buildings get the ObjectiveTarget component (blue glow, must be destroyed).")]
    [Min(0)][SerializeField] private int objectiveCount = 3;

    [Tooltip("How many buildings get the ForbiddenTarget component (red glow, must NOT be destroyed).")]
    [Min(0)][SerializeField] private int forbiddenCount = 2;

    [Header("Size Limit")]
    [Tooltip("Default box for spawn points WITHOUT a SpawnPointBox component. " +
             "Width (X) and depth (Z). 0 = no limit.")]
    [SerializeField] private Vector2 defaultFootprint = Vector2.zero;

    [Tooltip("Default max building height for spawn points without a SpawnPointBox. 0 = no limit.")]
    [Min(0)][SerializeField] private float defaultMaxHeight = 0f;

    [Tooltip("Scale buildings (uniformly) so they fit in the box. By default this only scales buildings " +
             "DOWN when they are bigger than the box; see 'Scale Up To Fit' to also scale up.")]
    [SerializeField] private bool fitToBox = true;

    [Tooltip("Also scale buildings UP (uniformly) until they touch the box on their tightest side. " +
             "Needs 'Fit To Box' to be on.")]
    [SerializeField] private bool scaleUpToFit = false;

    [Tooltip("When scaling up, never make a building larger than this multiple of its original size " +
             "(2 = at most twice as big). Prevents tiny buildings from becoming huge in big boxes.")]
    [Min(1)][SerializeField] private float maxScaleUp = 2f;

    [Tooltip("Move each building so the bottom-center of its mesh sits at the bottom-center of the box. " +
             "Fixes models whose pivot is not at their base. Only applies when a box size is set.")]
    [SerializeField] private bool alignToBoxBase = true;

    [Header("Options")]
    [Tooltip("Spawn automatically in Start(). Turn off if you want to call Spawn() yourself.")]
    [SerializeField] private bool spawnOnStart = true;

    private readonly List<GameObject> spawnedBuildings = new List<GameObject>();

    private void Start()
    {
        if (spawnOnStart)
            Spawn();
    }

    public void Spawn()
    {
        if (spawnPointsParent == null)
        {
            Debug.LogError("BuildingSpawner: no Spawn Points Parent assigned.", this);
            return;
        }

        if (buildings == null || buildings.Length == 0)
        {
            Debug.LogWarning("BuildingSpawner: no buildings assigned.", this);
            return;
        }

        ClearSpawned();

        // Collect spawn points (direct children only).
        List<Transform> points = new List<Transform>();
        foreach (Transform child in spawnPointsParent)
            points.Add(child);

        if (points.Count == 0)
        {
            Debug.LogWarning("BuildingSpawner: the spawn points parent has no children.", this);
            return;
        }

        // Clamp counts so we never ask for more special buildings than there are spawn points.
        int objectives = objectiveCount;
        int forbidden = forbiddenCount;

        if (objectives + forbidden > points.Count)
        {
            Debug.LogWarning($"BuildingSpawner: Objective Count ({objectiveCount}) + Forbidden Count " +
                             $"({forbiddenCount}) is higher than the number of spawn points ({points.Count}). " +
                             "Clamping the counts.", this);

            objectives = Mathf.Min(objectives, points.Count);
            forbidden = Mathf.Min(forbidden, points.Count - objectives);
        }

        // Shuffle so the objective/forbidden buildings end up in random places.
        Shuffle(points);

        for (int i = 0; i < points.Count; i++)
        {
            GameObject instance = SpawnBuilding(points[i]);
            if (instance == null) continue;

            if (i < objectives)
                AddOnce<ObjectiveTarget>(instance);
            else if (i < objectives + forbidden)
                AddOnce<ForbiddenTarget>(instance);
        }
    }

    private GameObject SpawnBuilding(Transform point)
    {
        BuildingEntry entry = buildings[Random.Range(0, buildings.Length)];
        if (entry == null || entry.prefab == null)
        {
            Debug.LogWarning("BuildingSpawner: an entry without a prefab was picked.", this);
            return null;
        }

        Quaternion rotation = point.rotation * Quaternion.Euler(entry.rotationOffset);
        GameObject instance = Instantiate(entry.prefab, point.position, rotation, transform);
        spawnedBuildings.Add(instance);

        FitToBox(instance, point);
        return instance;
    }

    private void GetBox(Transform point, out Vector2 footprint, out float maxHeight)
    {
        if (point.TryGetComponent(out SpawnPointBox box))
        {
            footprint = box.footprint;
            maxHeight = box.maxHeight;
        }
        else
        {
            footprint = defaultFootprint;
            maxHeight = defaultMaxHeight;
        }
    }

    // Scales the building down so it fits the spawn point's box, then (optionally) aligns it to the box base.
    private void FitToBox(GameObject instance, Transform point)
    {
        GetBox(point, out Vector2 footprint, out float maxHeight);

        // No box configured for this spawn point: leave the building as it is.
        if (footprint.x <= 0f && footprint.y <= 0f && maxHeight <= 0f)
            return;

        // Measure the building in the spawn point's own axes (X = width, Y = height, Z = depth).
        if (!TryGetLocalBounds(instance, point, out Bounds bounds))
            return;

        if (fitToBox)
        {
            Vector3 size = bounds.size;

            // The scale at which the building exactly touches the box on its tightest limited side.
            float scale = float.PositiveInfinity;

            if (footprint.x > 0f && size.x > 0.0001f) scale = Mathf.Min(scale, footprint.x / size.x);
            if (footprint.y > 0f && size.z > 0.0001f) scale = Mathf.Min(scale, footprint.y / size.z);
            if (maxHeight > 0f && size.y > 0.0001f) scale = Mathf.Min(scale, maxHeight / size.y);

            if (float.IsInfinity(scale))
                scale = 1f;
            else if (scaleUpToFit)
                scale = Mathf.Min(scale, maxScaleUp);   // grow or shrink to fit, but not beyond the max
            else
                scale = Mathf.Min(scale, 1f);           // only ever shrink

            if (!Mathf.Approximately(scale, 1f))
            {
                instance.transform.localScale *= scale;

                // Measure again, because scaling moves the bounds around the pivot.
                if (!TryGetLocalBounds(instance, point, out bounds))
                    return;
            }
        }

        if (alignToBoxBase)
        {
            Vector3 bottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            instance.transform.position -= point.TransformVector(bottomCenter);
        }
    }

    // Bounds of all meshes in 'root', expressed in the local space of 'space'.
    // Uses the meshes' own bounds (not the world-space AABB), so it stays accurate for rotated buildings.
    private static bool TryGetLocalBounds(GameObject root, Transform space, out Bounds bounds)
    {
        bounds = default;
        bool found = false;

        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            Mesh mesh = null;

            if (r is SkinnedMeshRenderer skinned)
                mesh = skinned.sharedMesh;
            else if (r is MeshRenderer && r.TryGetComponent(out MeshFilter filter))
                mesh = filter.sharedMesh;

            if (mesh == null) continue;

            Vector3 c = mesh.bounds.center;
            Vector3 e = mesh.bounds.extents;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);

                Vector3 p = space.InverseTransformPoint(r.transform.TransformPoint(corner));

                if (!found)
                {
                    bounds = new Bounds(p, Vector3.zero);
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(p);
                }
            }
        }

        return found;
    }

    // Adds the component unless the prefab already has it (both scripts allow only one per object).
    private static void AddOnce<T>(GameObject go) where T : Component
    {
        if (!go.TryGetComponent(out T _))
            go.AddComponent<T>();
    }

    private void ClearSpawned()
    {
        foreach (GameObject go in spawnedBuildings)
        {
            if (go != null)
                Destroy(go);
        }

        spawnedBuildings.Clear();
    }

    // Fisher-Yates shuffle
    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // Shows the spawn points and their facing direction in the Scene view.
    private void OnDrawGizmos()
    {
        if (spawnPointsParent == null) return;

        foreach (Transform point in spawnPointsParent)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(point.position, 0.5f);

            Gizmos.color = Color.blue;
            Gizmos.DrawRay(point.position, point.forward * 2f);

            GetBox(point, out Vector2 footprint, out float maxHeight);
            // Spawn points with their own SpawnPointBox draw their own (editable) box.
            if (!point.TryGetComponent(out SpawnPointBox _) && footprint.x > 0f && footprint.y > 0f)
            {
                float height = maxHeight > 0f ? maxHeight : 1f;
                Gizmos.matrix = point.localToWorldMatrix;
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(new Vector3(0f, height * 0.5f, 0f), new Vector3(footprint.x, height, footprint.y));
                Gizmos.matrix = Matrix4x4.identity;
            }
        }
    }
}