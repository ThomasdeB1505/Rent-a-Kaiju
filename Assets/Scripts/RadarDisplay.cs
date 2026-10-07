using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Submarine / sonar style radar screen for RadarScanner.
///
/// A round green scope with range rings, a rotating sweep with a fading trail, and
/// blips (one per scanner channel: blue objective, red forbidden) that light up when
/// the sweep passes over them and then fade — like real sonar.
///
/// Setup: add this next to RadarScanner on your player. It builds its own Canvas and
/// graphics at runtime, so no prefab or sprites are needed.
/// </summary>
public class RadarDisplay : MonoBehaviour
{
    public enum Corner { BottomLeft, BottomRight, TopLeft, TopRight, Center }

    // ---------------------------------------------------------------- Source
    [Header("Source")]
    public RadarScanner scanner;
    [Tooltip("Whose facing the radar follows. Defaults to the main camera, then the scanner.")]
    public Transform headingSource;
    [Tooltip("On: the top of the radar is where you're facing. Off: north-up (world +Z is up).")]
    public bool headingUp = true;
    [Tooltip("Turn off the scanner's floating on-screen markers so only this radar shows targets.")]
    public bool hideScannerScreenMarkers = true;

    // ---------------------------------------------------------------- Visibility
    [Header("Visibility")]
    [Tooltip("Off: radar only appears while the scanner is active (after pressing R).")]
    public bool alwaysVisible = false;
    [Tooltip("How fast the radar fades in/out.")]
    public float fadeSpeed = 4f;

    // ---------------------------------------------------------------- Layout
    [Header("Layout")]
    [Tooltip("Leave empty to auto-create an overlay canvas.")]
    public Canvas canvas;
    public Corner corner = Corner.BottomRight;
    public float size = 280f;
    public Vector2 margin = new Vector2(30f, 30f);
    [Tooltip("Reference resolution for the auto-created canvas.")]
    public Vector2 referenceResolution = new Vector2(1920f, 1080f);

    // ---------------------------------------------------------------- Monitor
    [Header("Monitor")]
    [Tooltip("Which monitor shows the radar: 0 = main monitor, 1 = second monitor, 2 = third...")]
    public int targetDisplay = 1;
    [Tooltip("If that monitor isn't connected, show the radar on the main monitor instead.")]
    public bool fallbackToMainDisplay = true;
    [Tooltip("Background color of the radar monitor when it's not the main one.")]
    public Color monitorBackground = Color.black;

    // ---------------------------------------------------------------- Look
    [Header("Custom Art (leave empty to use the built-in look)")]
    [Tooltip("The round screen behind everything. Its shape also crops the minimap.")]
    public Sprite customBackground;
    [Tooltip("Range rings / crosshair overlay.")]
    public Sprite customGrid;
    [Tooltip("The rotating sweep. Draw it pointing UP from the center, trail to the LEFT (counter-clockwise). It rotates around the image center.")]
    public Sprite customSweep;
    [Tooltip("Blip used for every channel, unless a channel has its own below.")]
    public Sprite customBlip;
    [Tooltip("Optional per-channel blips, in the same order as the scanner's channels (0 = Objective, 1 = Forbidden).")]
    public List<Sprite> customChannelBlips = new List<Sprite>();
    [Tooltip("Player marker in the center (only shown if Show Player Marker is on). Draw it pointing UP.")]
    public Sprite customPlayer;
    [Tooltip("Optional bezel/frame drawn ON TOP of everything (screws, glass glare, scratches...). Can be bigger than the scope.")]
    public Sprite customFrame;
    [Tooltip("Frame size relative to the scope. 1 = same size, 1.2 = 20% bigger (for a bezel around the scope).")]
    public float frameScale = 1f;
    [Tooltip("Optional shape that crops the minimap. Empty = uses the background's shape.")]
    public Sprite customMapMask;
    [Tooltip("On: custom sprites are tinted with the colors below (draw them white/grey). Off: your art keeps its own colors.")]
    public bool tintCustomSprites = false;
    [Tooltip("Blips use the channel color (blue/red) even when tinting is off. Turn off if your blip art is already colored.")]
    public bool tintBlipsWithChannelColor = true;

    [Header("Look")]
    public Color backgroundColor = new Color(0.02f, 0.12f, 0.06f, 0.85f);
    public Color gridColor = new Color(0.2f, 1f, 0.45f, 0.35f);
    public Color sweepColor = new Color(0.3f, 1f, 0.5f, 0.55f);
    public Color playerColor = new Color(0.6f, 1f, 0.7f, 1f);
    [Tooltip("Applied at start.")]
    [Range(1, 8)] public int rangeRings = 3;
    [Tooltip("Applied at start.")]
    public bool crosshair = true;
    [Tooltip("Applied at start.")]
    [Range(10f, 180f)] public float sweepTrailDegrees = 70f;
    [Tooltip("Sweep rotation speed in degrees per second.")]
    public float sweepSpeed = 180f;

    [Header("Blips")]
    public float blipSize = 16f;
    [Tooltip("Seconds for a blip to fade after the sweep passes over it.")]
    public float blipFadeTime = 2f;
    [Tooltip("Blips never fade below this while their target is still detected.")]
    [Range(0f, 1f)] public float blipMinAlpha = 0.15f;
    [Tooltip("On: blips only move when the sweep passes (classic sonar). Off: blips track targets live.")]
    public bool updateBlipsOnSweep = true;

    [Header("Readout")]
    [Tooltip("Text next to the scope with each channel's distance.")]
    public bool showReadout = true;
    public int readoutFontSize = 18;
    [Tooltip("Leave empty to use Unity's built-in font.")]
    public Font readoutFont;

    // ---------------------------------------------------------------- Minimap
    [Header("Minimap")]
    [Tooltip("Render a top-down map inside the scope, behind the sweep and blips.")]
    public bool useMinimap = true;
    [Tooltip("Leave empty to auto-create one.")]
    public Camera minimapCamera;
    [Tooltip("Layers the minimap camera renders (your duplicated buildings). Never include the player's layer.")]
    public LayerMask minimapLayers;
    [Tooltip("Remove the minimap layers from the main camera so the duplicates never show in the game view.")]
    public bool hideMinimapLayersFromMainCamera = true;
    [Tooltip("Only if your duplicates live elsewhere in the world: offset from a real position to its map position.")]
    public Vector3 mapWorldOffset;
    [Tooltip("Height of the minimap camera above the player.")]
    public float minimapCameraHeight = 150f;
    public int minimapResolution = 512;
    [Tooltip("Tint applied to the map image.")]
    public Color minimapTint = Color.white;
    [Tooltip("Show the small triangle for the player in the center of the scope.")]
    public bool showPlayerMarker = false;

    // ---------------------------------------------------------------- Internals
    class Blip
    {
        public Image image;
        public Vector2 pos;
        public float lastPing = -999f;
        public bool pinged;
    }

    RectTransform root, sweepRT, playerRT, blipParent;
    Image bgImage, gridImage, sweepImage, playerImage, frameImage;
    CanvasGroup group;
    Text readout;
    Sprite blipSprite;
    readonly List<Blip> blips = new List<Blip>();
    readonly List<Object> generated = new List<Object>();
    readonly StringBuilder sb = new StringBuilder();
    bool createdCanvas;
    float sweepAngle;
    RenderTexture mapTexture;
    RawImage mapImage;
    bool createdCamera;
    Camera monitorCamera;

    void Reset()
    {
        minimapLayers = LayerMask.GetMask("Minimap");
    }

    // ================================================================ Setup

    void Start()
    {
        if (!scanner) scanner = GetComponent<RadarScanner>();
#if UNITY_2023_1_OR_NEWER
        if (!scanner) scanner = FindFirstObjectByType<RadarScanner>();
#else
        if (!scanner) scanner = FindObjectOfType<RadarScanner>();
#endif
        if (!scanner)
        {
            Debug.LogWarning("RadarDisplay: no RadarScanner found.", this);
            enabled = false;
            return;
        }

        if (hideScannerScreenMarkers) scanner.drawScreenMarkers = false;
        Build();
    }

    void Build()
    {
        int display = ResolveDisplay();

        if (!canvas)
        {
            var go = new GameObject("RadarCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
            createdCanvas = true;
        }

        canvas.targetDisplay = display;
        if (display != 0)
        {
            // A monitor with no camera shows garbage or "No cameras rendering", so give it
            // a camera that renders nothing and just clears the screen.
            monitorCamera = new GameObject("RadarMonitorCamera").AddComponent<Camera>();
            monitorCamera.cullingMask = 0;
            monitorCamera.clearFlags = CameraClearFlags.SolidColor;
            monitorCamera.backgroundColor = monitorBackground;
            monitorCamera.targetDisplay = display;
            monitorCamera.depth = -100f;
        }

        root = NewRect("Radar", canvas.transform);
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = alwaysVisible ? 1f : 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        const int res = 256;
        // Built-in art is only generated for the parts you didn't replace.
        bgImage = NewImage("Background", root, customBackground ? customBackground : ToSprite(MakeDisc(res)));
        if (useMinimap) BuildMinimap();
        sweepImage = NewImage("Sweep", root, customSweep ? customSweep : ToSprite(MakeSweep(res, sweepTrailDegrees)));
        gridImage = NewImage("Grid", root, customGrid ? customGrid : ToSprite(MakeGrid(res, rangeRings, crosshair)));
        sweepRT = sweepImage.rectTransform;

        blipParent = NewRect("Blips", root);
        Stretch(blipParent);
        blipSprite = customBlip ? customBlip : ToSprite(MakeBlip(32));

        playerImage = NewImage("Player", root, customPlayer ? customPlayer : ToSprite(MakeTriangle(64)));
        playerImage.preserveAspect = true;
        playerRT = playerImage.rectTransform;
        Center(playerRT);

        if (customFrame)
        {
            frameImage = NewImage("Frame", root, customFrame);
            frameImage.preserveAspect = true;
            Center(frameImage.rectTransform);
        }

        var textGo = new GameObject("Readout", typeof(RectTransform), typeof(Text), typeof(Outline));
        readout = textGo.GetComponent<Text>();
        readout.rectTransform.SetParent(root, false);
        readout.font = readoutFont ? readoutFont : BuiltinFont();
        readout.supportRichText = true;
        readout.raycastTarget = false;
        readout.horizontalOverflow = HorizontalWrapMode.Overflow;
        readout.verticalOverflow = VerticalWrapMode.Overflow;
        textGo.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);

        ApplyLayout();
    }

    int ResolveDisplay()
    {
        int d = Mathf.Max(0, targetDisplay);
        if (d == 0) return 0;
#if UNITY_EDITOR
        // The editor can't open real extra monitors; pick "Display 2" in a Game view instead.
        return d;
#else
        if (d < Display.displays.Length)
        {
            if (!Display.displays[d].active) Display.displays[d].Activate();
            return d;
        }
        if (fallbackToMainDisplay)
        {
            Debug.LogWarning($"RadarDisplay: monitor {d + 1} not connected, showing radar on the main monitor.", this);
            return 0;
        }
        return d;
#endif
    }

    void BuildMinimap()
    {
        if (minimapLayers.value == 0) minimapLayers = LayerMask.GetMask("Minimap");
        if (minimapLayers.value == 0)
        {
            Debug.LogWarning("RadarDisplay: no Minimap Layers set. Create a 'Minimap' layer for your duplicated buildings.", this);
            return;
        }

        mapTexture = new RenderTexture(minimapResolution, minimapResolution, 16, RenderTextureFormat.ARGB32) { name = "MinimapRT" };

        if (!minimapCamera)
        {
            minimapCamera = new GameObject("MinimapCamera").AddComponent<Camera>();
            createdCamera = true;
        }
        minimapCamera.orthographic = true;
        minimapCamera.clearFlags = CameraClearFlags.SolidColor;
        minimapCamera.backgroundColor = new Color(0f, 0f, 0f, 0f); // transparent: scope background shows through
        minimapCamera.cullingMask = minimapLayers;
        minimapCamera.nearClipPlane = 0.3f;
        minimapCamera.farClipPlane = minimapCameraHeight + 500f;
        minimapCamera.targetTexture = mapTexture;

        if (hideMinimapLayersFromMainCamera && Camera.main)
            Camera.main.cullingMask &= ~minimapLayers.value;

        // Circular crop: a Mask using the same disc sprite as the scope background.
        var maskRT = NewRect("MapMask", root);
        Stretch(maskRT);
        var maskImg = maskRT.gameObject.AddComponent<Image>();
        maskImg.sprite = customMapMask ? customMapMask : bgImage.sprite;
        maskImg.raycastTarget = false;
        maskRT.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        var rawRT = NewRect("Map", maskRT);
        Stretch(rawRT);
        mapImage = rawRT.gameObject.AddComponent<RawImage>();
        mapImage.texture = mapTexture;
        mapImage.raycastTarget = false;
    }

    // ================================================================ Per frame

    void LateUpdate()
    {
        if (!scanner || !root) return;

        ApplyLayout();
        ApplyColors();

        float targetAlpha = (alwaysVisible || scanner.IsActive) ? 1f : 0f;
        group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, fadeSpeed * Time.unscaledDeltaTime);
        bool visible = !(group.alpha <= 0f && targetAlpha <= 0f);
        if (mapTexture && minimapCamera) minimapCamera.enabled = visible; // no rendering cost while hidden
        if (!visible) return;

        // Sweep (clockwise from the top)
        float prev = sweepAngle;
        float step = sweepSpeed * Time.deltaTime;
        sweepAngle = Mathf.Repeat(sweepAngle + step, 360f);
        sweepRT.localEulerAngles = new Vector3(0f, 0f, -sweepAngle);

        float yaw = HeadingYaw();
        playerRT.localEulerAngles = new Vector3(0f, 0f, headingUp ? 0f : -yaw);

        float radius = size * 0.5f;
        Vector3 origin = scanner.Origin;

        // Minimap camera: centered on the player, orthographic size = radar range,
        // so a building on the map sits exactly under its blip.
        if (mapTexture && minimapCamera)
        {
            Vector3 p = origin + mapWorldOffset;
            minimapCamera.transform.SetPositionAndRotation(
                new Vector3(p.x, p.y + minimapCameraHeight, p.z),
                Quaternion.Euler(90f, headingUp ? yaw : 0f, 0f));
            minimapCamera.orthographicSize = scanner.range;
            mapImage.color = minimapTint;
        }

        EnsureBlips();

        for (int i = 0; i < blips.Count; i++)
        {
            var b = blips[i];
            if (i >= scanner.channels.Count) { b.image.enabled = false; continue; }

            var ch = scanner.channels[i];
            bool valid = ch.enabled && ch.current != null && ch.current.gameObject.activeInHierarchy;

            if (valid)
            {
                Vector2 p = ToRadar(ch.current.position - origin, yaw, radius);
                float ang = Mathf.Repeat(Mathf.Atan2(p.x, p.y) * Mathf.Rad2Deg, 360f);
                bool crossed = step >= 360f || Mathf.Repeat(ang - prev, 360f) <= step;

                if (crossed)
                {
                    b.lastPing = Time.time;
                    b.pos = p;
                    b.pinged = true;
                }
                if (!updateBlipsOnSweep) { b.pos = p; b.pinged = true; }
            }
            else
            {
                b.pinged = false;
            }

            float since = Time.time - b.lastPing;
            float a = Mathf.Clamp01(1f - since / Mathf.Max(0.01f, blipFadeTime));
            if (valid && b.pinged) a = Mathf.Max(a, blipMinAlpha);

            Sprite channelSprite = i < customChannelBlips.Count ? customChannelBlips[i] : null;
            if (channelSprite && b.image.sprite != channelSprite) b.image.sprite = channelSprite;
            bool hasCustomArt = channelSprite || customBlip;

            Color c = (!hasCustomArt || tintBlipsWithChannelColor) ? ch.color : Color.white;
            c.a *= a;
            b.image.color = c;
            b.image.enabled = a > 0.001f;

            float s = blipSize * (1f + 0.6f * Mathf.Clamp01(1f - since / 0.25f)); // pop on ping
            b.image.rectTransform.sizeDelta = new Vector2(s, s);
            b.image.rectTransform.anchoredPosition = b.pos;
        }

        UpdateReadout(origin);
    }

    Vector2 ToRadar(Vector3 delta, float yaw, float radius)
    {
        delta.y = 0f;
        if (headingUp) delta = Quaternion.Euler(0f, -yaw, 0f) * delta;
        Vector2 p = new Vector2(delta.x, delta.z) / Mathf.Max(0.001f, scanner.range) * radius;
        float maxR = radius - blipSize * 0.5f;
        if (p.magnitude > maxR) p = p.normalized * maxR;
        return p;
    }

    float HeadingYaw()
    {
        Transform h = headingSource ? headingSource : (Camera.main ? Camera.main.transform : scanner.transform);
        Vector3 f = h.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 1e-6f) return h.eulerAngles.y;
        return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    void EnsureBlips()
    {
        while (blips.Count < scanner.channels.Count)
        {
            var img = NewImage("Blip" + blips.Count, blipParent, blipSprite);
            Center(img.rectTransform);
            img.enabled = false;
            blips.Add(new Blip { image = img });
        }
    }

    void UpdateReadout(Vector3 origin)
    {
        readout.enabled = showReadout;
        if (!showReadout) return;

        sb.Clear();
        foreach (var ch in scanner.channels)
        {
            if (!ch.enabled) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(ch.color)).Append('>').Append(ch.label).Append("  ");
            bool valid = ch.current != null && ch.current.gameObject.activeInHierarchy;
            sb.Append(valid ? $"{Vector3.Distance(origin, ch.current.position):0} m" : "---");
            sb.Append("</color>");
        }
        readout.text = sb.ToString();
    }

    // ================================================================ Layout helpers

    void ApplyLayout()
    {
        Vector2 a;
        switch (corner)
        {
            case Corner.BottomLeft: a = new Vector2(0f, 0f); break;
            case Corner.TopLeft: a = new Vector2(0f, 1f); break;
            case Corner.TopRight: a = new Vector2(1f, 1f); break;
            case Corner.Center: a = new Vector2(0.5f, 0.5f); break;
            default: a = new Vector2(1f, 0f); break;
        }
        root.anchorMin = root.anchorMax = root.pivot = a;
        Vector2 sign = new Vector2(a.x == 0f ? 1f : a.x == 1f ? -1f : 0f,
                                   a.y == 0f ? 1f : a.y == 1f ? -1f : 0f);
        root.anchoredPosition = Vector2.Scale(margin, sign);
        root.sizeDelta = new Vector2(size, size);

        playerRT.sizeDelta = Vector2.one * Mathf.Max(8f, size * 0.06f);
        if (frameImage) frameImage.rectTransform.sizeDelta = Vector2.one * size * Mathf.Max(0.01f, frameScale);

        // Readout goes above the scope when docked at the bottom, otherwise below.
        bool below = a.y > 0f;
        var rt = readout.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, below ? 0f : 1f);
        rt.pivot = new Vector2(0.5f, below ? 1f : 0f);
        rt.anchoredPosition = new Vector2(0f, below ? -8f : 8f);
        rt.sizeDelta = new Vector2(size, readoutFontSize * 1.4f);
        readout.fontSize = readoutFontSize;
        readout.alignment = below ? TextAnchor.UpperCenter : TextAnchor.LowerCenter;
    }

    void ApplyColors()
    {
        bgImage.color = Tint(customBackground, backgroundColor);
        gridImage.color = Tint(customGrid, gridColor);
        sweepImage.color = Tint(customSweep, sweepColor);
        playerImage.color = Tint(customPlayer, playerColor);
        playerImage.enabled = showPlayerMarker;
    }

    // Built-in art is white and always tinted; your own art is only tinted if you ask for it.
    Color Tint(Sprite custom, Color color)
    {
        return (custom && !tintCustomSprites) ? Color.white : color;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    Image NewImage(string name, Transform parent, Sprite sprite)
    {
        var rt = NewRect(name, parent);
        Stretch(rt);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void Center(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
    }

    static Font BuiltinFont()
    {
#if UNITY_2022_2_OR_NEWER
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
    }

    // ================================================================ Procedural textures (white, tinted by Image.color)

    Sprite ToSprite(Texture2D tex)
    {
        var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        generated.Add(tex);
        generated.Add(sp);
        return sp;
    }

    static Texture2D NewTex(int s) =>
        new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

    static float Line(float x, float center, float thickness) =>
        Mathf.Clamp01(thickness * 0.5f - Mathf.Abs(x - center) + 0.5f);

    static Texture2D Fill(int s, System.Func<float, float, float, float, Color> f)
    {
        var tex = NewTex(s);
        var px = new Color32[s * s];
        float c = (s - 1) * 0.5f, r = s * 0.5f - 1f;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = x - c, dy = y - c;
                px[y * s + x] = f(dx, dy, Mathf.Sqrt(dx * dx + dy * dy), r);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    static Texture2D MakeDisc(int s) => Fill(s, (dx, dy, d, r) =>
    {
        float a = Mathf.Clamp01(r - d + 0.5f);
        float v = Mathf.Lerp(1f, 0.55f, d / r); // slightly brighter center
        return new Color(v, v, v, a);
    });

    static Texture2D MakeGrid(int s, int rings, bool cross) => Fill(s, (dx, dy, d, r) =>
    {
        float th = s * 0.008f + 1f;
        float a = 0f;
        for (int k = 1; k <= rings; k++)
            a = Mathf.Max(a, Line(d, (r - th * 0.5f) * k / rings, k == rings ? th * 1.6f : th));
        if (cross && d < r)
            a = Mathf.Max(a, 0.6f * Mathf.Max(Line(dx, 0f, th * 0.6f), Line(dy, 0f, th * 0.6f)));
        return new Color(1f, 1f, 1f, a);
    });

    static Texture2D MakeSweep(int s, float trail) => Fill(s, (dx, dy, d, r) =>
    {
        if (d >= r + 0.5f) return new Color(1f, 1f, 1f, 0f);
        float inside = Mathf.Clamp01(r - d + 0.5f);
        float ang = Mathf.Repeat(Mathf.Atan2(dx, dy) * Mathf.Rad2Deg, 360f); // clockwise from up
        float behind = Mathf.Repeat(-ang, 360f);                               // trail is counter-clockwise
        float trailA = behind < trail ? Mathf.Pow(1f - behind / trail, 1.5f) * 0.8f : 0f;
        float edge = dy > 0f ? Line(dx, 0f, 2.5f) : 0f;                        // bright leading line
        return new Color(1f, 1f, 1f, Mathf.Max(trailA, edge) * inside);
    });

    static Texture2D MakeBlip(int s) => Fill(s, (dx, dy, d, r) =>
    {
        float t = Mathf.Clamp01(1f - d / r);
        float a = Mathf.Max(t * t * (3f - 2f * t), Mathf.Clamp01(r * 0.35f - d + 0.5f)); // soft glow + solid core
        return new Color(1f, 1f, 1f, a);
    });

    static Texture2D MakeTriangle(int s) => Fill(s, (dx, dy, d, r) =>
    {
        float y = dy + r;                  // 0 at bottom
        float halfW = (2f * r - y) * 0.35f; // narrows toward the top
        float a = y >= 0f ? Mathf.Clamp01(halfW - Mathf.Abs(dx) + 0.5f) : 0f;
        return new Color(1f, 1f, 1f, a);
    });

    // ================================================================ Cleanup

    void OnDestroy()
    {
        foreach (var o in generated) if (o) Destroy(o);
        if (createdCamera && minimapCamera) Destroy(minimapCamera.gameObject);
        else if (minimapCamera && mapTexture) minimapCamera.targetTexture = null;
        if (mapTexture) { mapTexture.Release(); Destroy(mapTexture); }
        if (monitorCamera) Destroy(monitorCamera.gameObject);
        if (createdCanvas && canvas) Destroy(canvas.gameObject);
        else if (root) Destroy(root.gameObject);
    }
}