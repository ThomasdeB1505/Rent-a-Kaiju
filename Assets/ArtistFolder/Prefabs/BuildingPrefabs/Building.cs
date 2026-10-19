using System;
using UnityEngine;

public enum BuildingCategory
{
    Normal,
    Objective,
    Dangerous
}

/// <summary>
/// Put this on a building prefab (optional - the spawner adds it automatically if missing).
/// Other scripts (damage, scoring) can check 'category' to know what kind of building this is.
/// </summary>
public class Building : MonoBehaviour
{
    public BuildingCategory category = BuildingCategory.Normal;

    // Fired when this building is destroyed (Destroy() called on it).
    public event Action<Building> Destroyed;

    private void OnDestroy()
    {
        Destroyed?.Invoke(this);
    }
}