using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Spawns buildings at runtime on every child of 'spawnPointsParent'.
/// - The first 'objectiveCount' (shuffled) points get objective buildings
/// - The next 'dangerousCount' points get dangerous buildings
/// - All remaining points get normal buildings
/// Each building uses the position AND rotation of its spawn point,
/// so rotate the empty's blue arrow (forward) toward the road.
/// </summary>
public class BuildingSpawner : MonoBehaviour
{
    [Header("Spawn Points")]
    [Tooltip("Parent object whose direct children are the spawn point empties.")]
    [SerializeField] private Transform spawnPointsParent;

    [Header("Building Prefabs")]
    [SerializeField] private GameObject[] normalBuildings;
    [SerializeField] private GameObject[] objectiveBuildings;
    [SerializeField] private GameObject[] dangerousBuildings;

    [Header("Counts")]
    [Min(0)][SerializeField] private int objectiveCount = 3;
    [Min(0)][SerializeField] private int dangerousCount = 2;

    [Header("Options")]
    [Tooltip("Extra rotation (degrees around Y) applied to every building. " +
             "Use this if your models' fronts don't match the prefab's forward direction.")]
    [SerializeField] private float yRotationOffset = 0f;

    [Tooltip("Spawn automatically in Start(). Turn off if you want to call Spawn() yourself.")]
    [SerializeField] private bool spawnOnStart = true;

    [Header("Events")]
    public UnityEvent onAllObjectivesDestroyed;

    private readonly List<GameObject> spawnedBuildings = new List<GameObject>();
    private int objectivesRemaining;
    private bool isShuttingDown;

    public int ObjectivesRemaining => objectivesRemaining;

    private void Start()
    {
        if (spawnOnStart)
            Spawn();
    }

    private void OnApplicationQuit()
    {
        isShuttingDown = true;
    }

    public void Spawn()
    {
        if (spawnPointsParent == null)
        {
            Debug.LogError("BuildingSpawner: no Spawn Points Parent assigned.", this);
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
        int dangerous = dangerousCount;

        if (objectives + dangerous > points.Count)
        {
            Debug.LogWarning($"BuildingSpawner: Objective Count ({objectiveCount}) + Dangerous Count " +
                             $"({dangerousCount}) is higher than the number of spawn points ({points.Count}). " +
                             "Clamping the counts.", this);

            objectives = Mathf.Min(objectives, points.Count);
            dangerous = Mathf.Min(dangerous, points.Count - objectives);
        }

        Shuffle(points);

        for (int i = 0; i < points.Count; i++)
        {
            BuildingCategory category;
            GameObject[] pool;

            if (i < objectives)
            {
                category = BuildingCategory.Objective;
                pool = objectiveBuildings;
            }
            else if (i < objectives + dangerous)
            {
                category = BuildingCategory.Dangerous;
                pool = dangerousBuildings;
            }
            else
            {
                category = BuildingCategory.Normal;
                pool = normalBuildings;
            }

            SpawnBuilding(points[i], pool, category);
        }
    }

    private void SpawnBuilding(Transform point, GameObject[] pool, BuildingCategory category)
    {
        if (pool == null || pool.Length == 0)
        {
            Debug.LogWarning($"BuildingSpawner: no prefabs assigned for the {category} category. " +
                             $"Spawn point '{point.name}' was left empty.", this);
            return;
        }

        GameObject prefab = pool[Random.Range(0, pool.Length)];
        if (prefab == null)
        {
            Debug.LogWarning($"BuildingSpawner: an empty slot in the {category} array was picked.", this);
            return;
        }

        Quaternion rotation = point.rotation * Quaternion.Euler(0f, yRotationOffset, 0f);
        GameObject instance = Instantiate(prefab, point.position, rotation, transform);
        spawnedBuildings.Add(instance);

        // Make sure the building knows what it is.
        if (!instance.TryGetComponent(out Building building))
            building = instance.AddComponent<Building>();

        building.category = category;

        if (category == BuildingCategory.Objective)
        {
            objectivesRemaining++;
            building.Destroyed += OnObjectiveDestroyed;
        }
    }

    private void OnObjectiveDestroyed(Building building)
    {
        // Ignore the destruction that happens when the scene unloads or the game stops.
        if (isShuttingDown || this == null)
            return;

        objectivesRemaining--;

        if (objectivesRemaining <= 0)
            onAllObjectivesDestroyed?.Invoke();
    }

    private void ClearSpawned()
    {
        foreach (GameObject go in spawnedBuildings)
        {
            if (go == null) continue;

            if (go.TryGetComponent(out Building b))
                b.Destroyed -= OnObjectiveDestroyed;

            Destroy(go);
        }

        spawnedBuildings.Clear();
        objectivesRemaining = 0;
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
        }
    }
}