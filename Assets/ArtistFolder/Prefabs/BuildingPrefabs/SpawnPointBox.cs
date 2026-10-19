using UnityEngine;

/// <summary>
/// Optional: put this on a spawn point empty to give it its own size limit.
/// Spawn points without this component use the default size set on the BuildingSpawner.
///
/// The box is centered on the empty, rotates with it, and sits on top of it
/// (the bottom of the box is at the empty's position).
/// Keep the spawn point's scale at (1, 1, 1) so the sizes mean what they say.
///
/// Select the spawn point and drag the handles in the Scene view to resize the box
/// (see Editor/SpawnPointBoxEditor.cs).
/// </summary>
public class SpawnPointBox : MonoBehaviour
{
    [Tooltip("Width (X) and depth (Z) of the box in world units. 0 = no limit on that axis.")]
    public Vector2 footprint = new Vector2(10f, 10f);

    [Tooltip("Maximum building height. 0 = no limit.")]
    public float maxHeight = 0f;

    private void OnDrawGizmos()
    {
        DrawBox(false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawBox(true);
    }

    private void DrawBox(bool selected)
    {
        if (footprint.x <= 0f || footprint.y <= 0f) return;

        // With no height limit, only a 1 unit tall box is drawn so you can still see the footprint.
        float height = maxHeight > 0f ? maxHeight : 1f;
        Vector3 center = new Vector3(0f, height * 0.5f, 0f);
        Vector3 size = new Vector3(footprint.x, height, footprint.y);

        Gizmos.matrix = transform.localToWorldMatrix;

        if (selected)
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.15f);
            Gizmos.DrawCube(center, size);
        }

        Gizmos.color = selected ? new Color(0.4f, 1f, 0.5f, 1f) : Color.green;
        Gizmos.DrawWireCube(center, size);

        Gizmos.matrix = Matrix4x4.identity;
    }
}