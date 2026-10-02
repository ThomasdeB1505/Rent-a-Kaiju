using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Put this on a real building. At start it creates a visual-only copy on the Minimap layer
/// (meshes + materials only — no colliders, no scripts), so:
///   - the copy never counts toward ObjectiveManager / LoseManager,
///   - the radar never detects the copy instead of the real building,
///   - the copy disappears when the real building is destroyed or disabled.
///
/// The copy is optionally tinted with the building's ObjectiveTarget / ForbiddenTarget glow
/// color, so good and bad buildings are recognizable on the map at a glance.
/// </summary>
[DisallowMultipleComponent]
public class MinimapProxy : MonoBehaviour
{
    [Tooltip("Layer the copy is put on. Must be in RadarDisplay's Minimap Layers.")]
    public string minimapLayer = "Minimap";
    [Tooltip("Must match RadarDisplay.mapWorldOffset. Keep at zero to place copies on top of the originals.")]
    public Vector3 mapWorldOffset;
    [Tooltip("Optional flat/stylized material for the map. Empty = use the building's own materials.")]
    public Material overrideMaterial;
    [Tooltip("Tint the copy with the ObjectiveTarget / ForbiddenTarget glow color, if this building has one.")]
    public bool tintFromTargetType = true;
    public float tintEmission = 1.5f;
    [Tooltip("Keep the copy in sync if the building moves/rotates. Off is cheaper for static buildings.")]
    public bool followOriginal = false;

    GameObject proxy;
    readonly List<(Transform src, Transform dst)> parts = new List<(Transform, Transform)>();

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP / HDRP
    static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    void Start()
    {
        int layer = LayerMask.NameToLayer(minimapLayer);
        if (layer < 0)
        {
            Debug.LogWarning($"MinimapProxy: layer '{minimapLayer}' doesn't exist. Add it under Project Settings > Tags and Layers.", this);
            return;
        }

        proxy = new GameObject(name + " (Minimap)") { layer = layer };

        bool hasTint = TryGetTint(out Color tint);
        var block = new MaterialPropertyBlock();

        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (!mr || !mf.sharedMesh) continue;

            var part = new GameObject(mf.name, typeof(MeshFilter), typeof(MeshRenderer)) { layer = layer };
            part.transform.SetParent(proxy.transform, false);
            part.GetComponent<MeshFilter>().sharedMesh = mf.sharedMesh;

            var pr = part.GetComponent<MeshRenderer>();
            if (overrideMaterial)
            {
                var mats = new Material[mr.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = overrideMaterial;
                pr.sharedMaterials = mats;
            }
            else pr.sharedMaterials = mr.sharedMaterials;

            // Copies must not cast shadows into the real game view.
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
            pr.lightProbeUsage = LightProbeUsage.Off;
            pr.reflectionProbeUsage = ReflectionProbeUsage.Off;

            if (hasTint)
            {
                block.Clear();
                block.SetColor(BaseColorId, tint);
                block.SetColor(ColorId, tint);
                block.SetColor(EmissionId, tint * tintEmission);
                pr.SetPropertyBlock(block);
            }

            parts.Add((mf.transform, part.transform));
        }

        SyncTransforms();
    }

    bool TryGetTint(out Color c)
    {
        c = Color.white;
        if (!tintFromTargetType) return false;
        var obj = GetComponent<ObjectiveTarget>();
        if (obj) { c = obj.glowColor; return true; }
        var bad = GetComponent<ForbiddenTarget>();
        if (bad) { c = bad.glowColor; return true; }
        return false;
    }

    void SyncTransforms()
    {
        foreach (var (src, dst) in parts)
        {
            if (!src) continue;
            dst.SetPositionAndRotation(src.position + mapWorldOffset, src.rotation);
            dst.localScale = src.lossyScale; // proxy root is unscaled, so lossy == local here
        }
    }

    void LateUpdate()
    {
        if (followOriginal && proxy) SyncTransforms();
    }

    // Mirror the original's lifetime (works with both Destroy and pooling via SetActive).
    void OnEnable() { if (proxy) proxy.SetActive(true); }
    void OnDisable() { if (proxy) proxy.SetActive(false); }
    void OnDestroy() { if (proxy) Destroy(proxy); }
}
