using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Press a key (R by default) to ping a radar that finds the CLOSEST target in range
/// for each "channel" and shows an on-screen marker (or an edge arrow when off-screen).
///
/// Default channels:
///   - Objective (blue)  -> closest ObjectiveTarget
///   - Forbidden (red)   -> closest ForbiddenTarget
/// You can add more channels in the Inspector, including ones that search by Tag.
///
/// Put this on your player (or camera). No UI setup needed — it draws with OnGUI.
/// </summary>
public class RadarScanner : MonoBehaviour
{
    public enum TargetSource { ObjectiveTargets, ForbiddenTargets, Tag }
    public enum ActivationMode
    {
        Pulse,   // press once: shows for Display Duration, then cooldown
        Toggle,  // press to turn on, press again to turn off
        Hold     // active only while the key is held
    }

    [Serializable]
    public class RadarChannel
    {
        public string label = "Target";
        public bool enabled = true;
        public TargetSource source = TargetSource.ObjectiveTargets;
        [Tooltip("Only used when Source = Tag.")]
        public string tag = "Untagged";
        public Color color = Color.blue;

        [NonSerialized] public Transform current;
        [NonSerialized] public bool hasTarget;
    }

    // ---------------------------------------------------------------- Input
    [Header("Input")]
#if ENABLE_INPUT_SYSTEM
    public Key activateKey = Key.R;
#else
    public KeyCode activateKey = KeyCode.R;
#endif
    public ActivationMode activationMode = ActivationMode.Pulse;

    // ---------------------------------------------------------------- Detection
    [Header("Detection")]
    [Tooltip("Max distance at which targets are detected.")]
    public float range = 50f;
    [Tooltip("Where distances are measured from. Defaults to this object.")]
    public Transform scanOrigin;
    [Tooltip("Only detect targets that aren't blocked by geometry.")]
    public bool requireLineOfSight = false;
    [Tooltip("Layers that block line of sight. Exclude your player's layer!")]
    public LayerMask lineOfSightMask = ~0;
    [Tooltip("How often (seconds) to re-find the closest target while active. 0 = only on activation.")]
    public float refreshInterval = 0.25f;

    public List<RadarChannel> channels = new List<RadarChannel>
    {
        new RadarChannel { label = "Objective", source = TargetSource.ObjectiveTargets, color = new Color(0.25f, 0.55f, 1f) },
        new RadarChannel { label = "Forbidden", source = TargetSource.ForbiddenTargets, color = new Color(1f, 0.25f, 0.25f) },
    };

    // ---------------------------------------------------------------- Timing
    [Header("Timing")]
    [Tooltip("Pulse mode: how long markers stay visible.")]
    public float displayDuration = 4f;
    [Tooltip("Seconds after the radar turns off before it can be used again.")]
    public float cooldown = 2f;

    // ---------------------------------------------------------------- Display
    [Header("Display")]
    [Tooltip("Draw floating markers/arrows over targets on screen. RadarDisplay turns this off automatically if you want.")]
    public bool drawScreenMarkers = true;
    [Tooltip("Camera used to place markers. Defaults to Camera.main.")]
    public Camera targetCamera;
    public float markerSize = 36f;
    public bool pulseMarkers = true;
    [Tooltip("World-space height offset so the marker sits above the object's pivot.")]
    public float markerHeightOffset = 1f;
    public bool showOffscreenArrows = true;
    public float arrowSize = 32f;
    [Tooltip("Distance from the screen edge for off-screen arrows.")]
    public float edgePadding = 40f;
    public bool showLabels = true;
    public bool showDistance = true;
    public int fontSize = 16;
    [Tooltip("Show a status line at the top of the screen per channel (incl. 'none in range').")]
    public bool showStatusText = true;
    [Tooltip("Optional custom textures (white, so they can be tinted). Leave empty for built-in shapes.")]
    public Texture2D markerTexture;
    public Texture2D arrowTexture;

    // ---------------------------------------------------------------- Audio & events
    [Header("Audio & Events")]
    public AudioSource audioSource;
    public AudioClip scanSound;
    public UnityEvent onRadarActivated;
    public UnityEvent onRadarDeactivated;

    // ---------------------------------------------------------------- State
    public bool IsActive { get; private set; }
    public float CooldownRemaining => Mathf.Max(0f, cooldownUntil - Time.time);

    float activeUntil;
    float cooldownUntil;
    float nextRefresh;
    readonly List<Transform> candidates = new List<Transform>();
    Texture2D defaultMarker, defaultArrow;
    GUIStyle labelStyle;

    public Vector3 Origin => scanOrigin ? scanOrigin.position : transform.position;

    // ================================================================ Loop

    void Update()
    {
        HandleInput();
        if (!IsActive) return;

        if (activationMode == ActivationMode.Pulse && Time.time >= activeUntil)
        {
            Deactivate();
            return;
        }

        bool lostTarget = false;
        foreach (var ch in channels)
            if (ch.hasTarget && !IsValid(ch.current)) lostTarget = true;

        if (lostTarget || (refreshInterval > 0f && Time.time >= nextRefresh))
            Scan();
    }

    void HandleInput()
    {
        bool pressed = KeyPressed();
        bool ready = Time.time >= cooldownUntil;

        switch (activationMode)
        {
            case ActivationMode.Pulse:
                if (pressed && ready && !IsActive) Activate();
                break;
            case ActivationMode.Toggle:
                if (pressed)
                {
                    if (IsActive) Deactivate();
                    else if (ready) Activate();
                }
                break;
            case ActivationMode.Hold:
                bool held = KeyHeld();
                if (held && !IsActive && ready) Activate();
                else if (!held && IsActive) Deactivate();
                break;
        }
    }

    bool KeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current[activateKey].wasPressedThisFrame;
#else
        return Input.GetKeyDown(activateKey);
#endif
    }

    bool KeyHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current[activateKey].isPressed;
#else
        return Input.GetKey(activateKey);
#endif
    }

    // ================================================================ Public API

    public void Activate()
    {
        IsActive = true;
        activeUntil = Time.time + displayDuration;
        Scan();
        if (audioSource && scanSound) audioSource.PlayOneShot(scanSound);
        onRadarActivated?.Invoke();
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        cooldownUntil = Time.time + cooldown;
        foreach (var ch in channels) { ch.current = null; ch.hasTarget = false; }
        onRadarDeactivated?.Invoke();
    }

    /// <summary>Closest target found for a channel (null if none in range).</summary>
    public Transform GetClosest(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= channels.Count) return null;
        var t = channels[channelIndex].current;
        return IsValid(t) ? t : null;
    }

    /// <summary>Finds the closest target in range for every enabled channel.</summary>
    public void Scan()
    {
        Vector3 origin = Origin;
        nextRefresh = Time.time + refreshInterval;

        foreach (var ch in channels)
        {
            ch.current = null;
            ch.hasTarget = false;
            if (!ch.enabled) continue;

            candidates.Clear();
            Collect(ch, candidates);

            float bestSqr = range * range;
            foreach (var t in candidates)
            {
                if (!IsValid(t)) continue;
                float d = (t.position - origin).sqrMagnitude;
                if (d > bestSqr) continue;
                if (requireLineOfSight && !HasLineOfSight(origin, t)) continue;
                bestSqr = d;
                ch.current = t;
            }
            ch.hasTarget = ch.current != null;
        }
    }

    // ================================================================ Detection helpers

    void Collect(RadarChannel ch, List<Transform> list)
    {
        switch (ch.source)
        {
            case TargetSource.ObjectiveTargets:
                foreach (var o in FindAll<ObjectiveTarget>()) list.Add(o.transform);
                break;
            case TargetSource.ForbiddenTargets:
                foreach (var o in FindAll<ForbiddenTarget>()) list.Add(o.transform);
                break;
            case TargetSource.Tag:
                if (string.IsNullOrEmpty(ch.tag)) break;
                try
                {
                    foreach (var go in GameObject.FindGameObjectsWithTag(ch.tag)) list.Add(go.transform);
                }
                catch (UnityException)
                {
                    Debug.LogWarning($"RadarScanner: tag '{ch.tag}' is not defined in the Tag Manager.", this);
                }
                break;
        }
    }

    static T[] FindAll<T>() where T : UnityEngine.Object
    {
#if UNITY_2023_1_OR_NEWER
        return FindObjectsByType<T>(FindObjectsSortMode.None);
#else
        return FindObjectsOfType<T>();
#endif
    }

    static bool IsValid(Transform t) => t != null && t.gameObject.activeInHierarchy;

    bool HasLineOfSight(Vector3 origin, Transform t)
    {
        Vector3 dir = t.position - origin;
        float dist = dir.magnitude;
        if (dist < 0.01f) return true;

        if (Physics.Raycast(origin, dir / dist, out RaycastHit hit, dist, lineOfSightMask, QueryTriggerInteraction.Ignore))
        {
            // Ignore hits on ourselves (in case the player's layer wasn't excluded).
            if (hit.transform.IsChildOf(transform)) return true;
            return hit.transform == t || hit.transform.IsChildOf(t);
        }
        return true;
    }

    // ================================================================ Drawing

    void OnGUI()
    {
        if (!drawScreenMarkers || !IsActive || Event.current.type != EventType.Repaint) return;
        Camera cam = targetCamera ? targetCamera : Camera.main;
        if (!cam) return;

        EnsureGuiResources();
        Color prev = GUI.color;

        float statusY = 10f;
        foreach (var ch in channels)
        {
            if (!ch.enabled) continue;
            bool valid = IsValid(ch.current);

            if (showStatusText)
            {
                string status = valid
                    ? $"{ch.label}: {Vector3.Distance(Origin, ch.current.position):0} m"
                    : $"{ch.label}: none in range";
                DrawLabel(new Vector2(Screen.width * 0.5f, statusY + fontSize * 0.5f), status, ch.color);
                statusY += fontSize + 6f;
            }

            if (valid) DrawMarker(cam, ch);
        }

        GUI.color = prev;
    }

    void DrawMarker(Camera cam, RadarChannel ch)
    {
        Vector3 world = ch.current.position + Vector3.up * markerHeightOffset;
        Vector3 sp = cam.WorldToScreenPoint(world);
        Vector2 center = new Vector2(Screen.width, Screen.height) * 0.5f;
        Vector2 screen = new Vector2(sp.x, sp.y);
        bool behind = sp.z < 0f;
        if (behind) screen = center - (screen - center); // mirror so the arrow points the right way

        bool onScreen = !behind &&
                        screen.x >= 0 && screen.x <= Screen.width &&
                        screen.y >= 0 && screen.y <= Screen.height;

        string text = BuildLabel(ch);
        GUI.color = ch.color;

        if (onScreen)
        {
            Vector2 gui = new Vector2(screen.x, Screen.height - screen.y);
            float s = markerSize * (pulseMarkers ? 1f + 0.12f * Mathf.Sin(Time.time * 6f) : 1f);
            GUI.DrawTexture(new Rect(gui.x - s * 0.5f, gui.y - s * 0.5f, s, s), markerTexture ? markerTexture : defaultMarker);
            if (text != null) DrawLabel(gui + new Vector2(0f, s * 0.5f + fontSize * 0.7f), text, ch.color);
        }
        else if (showOffscreenArrows)
        {
            Vector2 dir = screen - center;
            if (dir.sqrMagnitude < 0.001f) dir = Vector2.down;
            dir.Normalize();

            Vector2 half = center - Vector2.one * edgePadding;
            float scale = Mathf.Min(half.x / Mathf.Max(Mathf.Abs(dir.x), 1e-4f),
                                    half.y / Mathf.Max(Mathf.Abs(dir.y), 1e-4f));
            Vector2 edge = center + dir * scale;
            Vector2 gui = new Vector2(edge.x, Screen.height - edge.y);
            float angle = Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg;

            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, gui);
            GUI.DrawTexture(new Rect(gui.x - arrowSize * 0.5f, gui.y - arrowSize * 0.5f, arrowSize, arrowSize),
                            arrowTexture ? arrowTexture : defaultArrow);
            GUI.matrix = m;

            if (text != null)
            {
                // Put the label on the inner side of the arrow so it stays on screen.
                Vector2 inward = new Vector2(-dir.x, dir.y) * (arrowSize + fontSize);
                DrawLabel(gui + inward, text, ch.color);
            }
        }
    }

    string BuildLabel(RadarChannel ch)
    {
        if (!showLabels && !showDistance) return null;
        string s = showLabels ? ch.label : "";
        if (showDistance)
        {
            float d = Vector3.Distance(Origin, ch.current.position);
            s += (s.Length > 0 ? " " : "") + $"{d:0} m";
        }
        return s;
    }

    void DrawLabel(Vector2 centerPos, string text, Color color)
    {
        labelStyle.fontSize = fontSize;
        Vector2 size = labelStyle.CalcSize(new GUIContent(text));
        Rect r = new Rect(centerPos.x - size.x * 0.5f, centerPos.y - size.y * 0.5f, size.x, size.y);

        GUI.color = Color.white;
        labelStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
        GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, labelStyle); // shadow
        labelStyle.normal.textColor = color;
        GUI.Label(r, text, labelStyle);
        GUI.color = color;
    }

    void EnsureGuiResources()
    {
        if (labelStyle == null)
            labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        if (!defaultMarker) defaultMarker = MakeRingTexture(64);
        if (!defaultArrow) defaultArrow = MakeArrowTexture(64);
    }

    static Texture2D MakeRingTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        float r = size * 0.5f - 1f, thick = size * 0.1f, dot = size * 0.1f;
        Vector2 c = new Vector2(size - 1, size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float ring = Mathf.Clamp01(1f - Mathf.Abs(d - (r - thick * 0.5f)) / (thick * 0.5f + 0.5f));
                float centerDot = Mathf.Clamp01(dot - d + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Max(ring, centerDot)));
            }
        tex.Apply();
        return tex;
    }

    static Texture2D MakeArrowTexture(int size)
    {
        // Triangle pointing right (+X), rotated at draw time.
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        float mid = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float halfWidth = (size - 1 - x) * 0.5f;
                float a = Mathf.Clamp01(halfWidth - Mathf.Abs(y - mid) + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return tex;
    }

    void OnDestroy()
    {
        if (defaultMarker) Destroy(defaultMarker);
        if (defaultArrow) Destroy(defaultArrow);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireSphere(scanOrigin ? scanOrigin.position : transform.position, range);
    }
}
