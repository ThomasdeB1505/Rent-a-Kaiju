using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

/// <summary>
/// Replaces CameraSwitcher. Shows every camera of the current "page" on its own screen (RawImage),
/// lets the player move a selection with the arrow keys, and zooms the selected feed to fill the display.
///
/// SETUP
///  1. Put this on an empty GameObject (e.g. "CameraManager").
///  2. Add one entry to "Pages" per level/page. For each page, set Columns and fill the Slots list
///     (any number of slots). Each slot = one Camera + the RawImage that displays it.
///  3. Slots are numbered left-to-right, top-to-bottom. With Columns = 3, slot 0-2 is the first row.
///  4. Parent each camera to the building it should be destroyed with. When the building is destroyed
///     (or deactivated, if that option is on), that screen switches to static.
///  5. When the player reaches level 2, call:  cameraMonitorSystem.UnlockPage(1);
///
/// Cameras are never rendered to the display directly. Each one renders into a RenderTexture that the
/// RawImage shows, and the script switches cameras on only on the frames they are due to update.
/// </summary>
public class CameraMonitorSystem : MonoBehaviour
{
    public enum FeedState { Unknown, Live, Static }

    // ------------------------------------------------------------------ data

    [Serializable]
    public class MonitorSlot
    {
        [Tooltip("The camera for this screen. Leave empty to show static.")]
        public Camera camera;

        [Tooltip("The RawImage (inside your screen art) that displays this camera.")]
        public RawImage screen;

        [NonSerialized] public FeedState state = FeedState.Unknown;
        [NonSerialized] public RenderTexture renderTexture;
        [NonSerialized] public bool bound;
        [NonSerialized] public bool hasRenderedOnce;
        [NonSerialized] public float nextRenderTime;
    }

    [Serializable]
    public class CameraPage
    {
        public string name = "Level 1";

        [Tooltip("Tick for pages the player has from the beginning. Leave unticked for later levels and call UnlockPage(index).")]
        public bool unlockedAtStart = true;

        [Min(1), Tooltip("Screens per row. 9 screens with 3 columns = 3x3 grid, 6 screens with 3 columns = 3x2.")]
        public int columns = 3;

        [Tooltip("Optional. A parent object holding all of this page's screen art. It is shown/hidden with the page. " +
                 "If empty, only the screens' own RawImage objects are shown/hidden.")]
        public GameObject pageRoot;

        public MonitorSlot[] slots = new MonitorSlot[9];

        [NonSerialized] public bool unlocked;
        [NonSerialized] public int lastSelected;
    }

    // ------------------------------------------------------------------ inspector

    [Header("Pages")]
    public List<CameraPage> pages = new List<CameraPage> { new CameraPage() };

    [Tooltip("Jump to a page as soon as it is unlocked. If off, the player switches with the page keys.")]
    public bool autoSwitchToUnlockedPage = false;

    [Tooltip("If on, pressing next on the last page wraps around to the first (and the reverse), so it never shakes. " +
             "If off, pages are laid out left to right and the end of the row shakes.")]
    public bool wrapPages = false;

    [Header("Page Slide")]
    [Tooltip("Seconds for the side-to-side slide between pages. 0 = instant switch.")]
    public float pageSlideDuration = 0.4f;
    public AnimationCurve pageSlideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("How far the pages travel, as a multiple of the display width. 1 = a full screen width.")]
    public float pageSlideDistance = 1f;

    [Header("Edge Shake (no page on that side)")]
    [Tooltip("Seconds the shake lasts. 0 = no shake.")]
    public float shakeDuration = 0.3f;

    [Tooltip("Size of the shake in canvas units (roughly pixels).")]
    public float shakeStrength = 20f;

    [Tooltip("How many back-and-forth swings the shake has.")]
    public float shakeCycles = 3f;

    [Tooltip("Optional. Extra UI (e.g. the console background art) that shakes together with the current page.")]
    public RectTransform[] extraShakeTargets;

    [Header("Controls (old Input Manager)")]
    public KeyCode leftKey = KeyCode.LeftArrow;
    public KeyCode rightKey = KeyCode.RightArrow;
    public KeyCode upKey = KeyCode.UpArrow;
    public KeyCode downKey = KeyCode.DownArrow;

    [Tooltip("Zooms the selected screen in. Pressing it again zooms back out.")]
    public KeyCode zoomKey = KeyCode.Return;

    [Tooltip("Extra key that only zooms out. Set to None to disable.")]
    public KeyCode zoomOutKey = KeyCode.Escape;

    [Tooltip("Slides to the next unlocked page (pages sit to the right). Shakes the screen if there is none.")]
    public KeyCode nextPageKey = KeyCode.Tab;

    [Tooltip("Slides back to the previous unlocked page (pages sit to the left). Shakes the screen if there is none. None = disabled.")]
    public KeyCode previousPageKey = KeyCode.None;

    [Header("Display")]
    [Tooltip("The Canvas holding the screens. If empty, it is found from the first assigned screen.")]
    public Canvas monitorCanvas;

    [Tooltip("Only needed if the canvas is 'Screen Space - Camera'. This camera is sent to the display and is used for the split-screen fallback. " +
             "Give it a higher Depth than your feed cameras.")]
    public Camera uiCamera;

    [Tooltip("0 = main display (laptop), 1 = second display (monitor). Match this to City Display Index in KaijuDisplaySetup.")]
    public int displayIndex = 1;

    [Tooltip("If only one screen is connected, draw on the right half of the main display. Needs a uiCamera (an Overlay canvas cannot be cropped).")]
    public bool splitScreenFallback = true;

    [Header("Zoom")]
    [Tooltip("A full-display RectTransform that the zoomed screen is moved into. If empty, the root canvas is used.")]
    public RectTransform zoomLayer;

    public float zoomDuration = 0.35f;
    public AnimationCurve zoomCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Allow zooming on a screen that is showing static.")]
    public bool allowZoomOnStatic = true;

    [Tooltip("Objects hidden while a screen is fully zoomed in (e.g. the overview background art).")]
    public GameObject[] hideWhenZoomed;

    [Header("Selection Look")]
    public Color selectedColor = Color.white;
    public Color unselectedColor = new Color(0.65f, 0.65f, 0.65f, 1f);

    [Tooltip("Optional frame/outline UI element that is moved over the selected screen. Place it above the screens in the hierarchy.")]
    public RectTransform selectionHighlight;
    public float highlightPadding = 8f;

    [Header("Feed Quality")]
    [Tooltip("Resolution of each small screen's feed.")]
    public Vector2Int feedResolution = new Vector2Int(640, 360);

    [Tooltip("Resolution of the feed while zoomed in. One shared texture is used for whichever screen is zoomed.")]
    public Vector2Int zoomResolution = new Vector2Int(1920, 1080);

    [Header("Performance")]
    [Tooltip("Updates per second for the selected screen. 0 = every frame.")]
    public int selectedFps = 0;

    [Tooltip("Updates per second for all other screens. 0 = every frame. Updates are staggered so they don't all render on the same frame.")]
    public int backgroundFps = 5;

    [Tooltip("While fully zoomed in, stop rendering every other camera.")]
    public bool pauseOthersWhenZoomed = true;

    [Header("Static / Destroyed Cameras")]
    [Tooltip("Optional. Your own static image. Leave empty to use generated noise. Use Wrap Mode = Repeat if Animate Static is on.")]
    public Texture staticTexture;

    public bool animateStatic = true;
    public float staticFlickerInterval = 0.06f;

    [Tooltip("Also show static while the camera's object is inactive (e.g. a building replaced by rubble, or a level not loaded yet). " +
             "Feed comes back automatically when it becomes active again.")]
    public bool inactiveCameraCountsAsDestroyed = true;

    // ------------------------------------------------------------------ events

    public event Action<int> PageChanged;              // page index
    public event Action<int> PageUnlocked;             // page index
    public event Action<int, int> SelectionChanged;    // page, slot
    public event Action<bool> ZoomChanged;             // true = zooming in, false = zooming out (fires when the transition starts)
    public event Action<int, int> FeedLost;            // page, slot: a live camera was destroyed / deactivated
    public event Action<int> PageEdgeHit;              // -1 = tried to go left, +1 = tried to go right (use for a bump sound)

    // ------------------------------------------------------------------ public state

    public int CurrentPage => currentPage;
    public int SelectedIndex => selectedIndex;
    public bool IsZoomed => zoomedSlot != null && zoomTarget > 0.5f;
    public bool IsTransitioning => transitioning;

    // ------------------------------------------------------------------ private state

    struct LayoutSnapshot
    {
        public Transform parent;
        public int siblingIndex;
        public Vector2 anchorMin, anchorMax, offsetMin, offsetMax, pivot;
        public Vector3 localScale;
        public Quaternion localRotation;
    }

    bool ready;
    bool transitioning;
    Coroutine shakeRoutine;
    List<RectTransform> shakeTargets;
    readonly Dictionary<RectTransform, Vector2> restPos = new Dictionary<RectTransform, Vector2>();
    int currentPage = -1;
    int selectedIndex;

    MonitorSlot zoomedSlot;
    float zoomT;
    float zoomTarget;
    LayoutSnapshot savedLayout;
    Vector2 zoomStartPos, zoomStartSize;
    bool hiddenForZoom;
    RenderTexture zoomRT;

    Texture2D generatedStatic;
    float staticTimer;
    readonly Vector3[] corners = new Vector3[4];

    CameraPage CurrentPageData => (currentPage >= 0 && currentPage < pages.Count) ? pages[currentPage] : null;
    Texture StaticTex => staticTexture != null ? staticTexture : generatedStatic;

    static MonitorSlot GetSlot(CameraPage p, int i)
    {
        return (p != null && p.slots != null && i >= 0 && i < p.slots.Length) ? p.slots[i] : null;
    }

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        NormalizeData();
        ResolveCanvas();
        SetupDisplay();

        if (staticTexture == null) generatedStatic = BuildNoiseTexture(128);

        foreach (var p in pages)
        {
            p.unlocked = p.unlockedAtStart;
            p.lastSelected = 0;
            foreach (var s in p.slots) InitSlot(s);
        }

        RefreshAllFeedStates();

        foreach (var p in pages) SetPageVisible(p, false);

        ready = true;

        int first = pages.FindIndex(p => p.unlocked);
        if (first >= 0) ShowPage(first);
    }

    void Update()
    {
        HandleInput();
        UpdateZoomAnimation();
        AnimateStatic();
    }

    void LateUpdate()
    {
        // Runs after gameplay code, so a building destroyed this frame is already seen as null here.
        RefreshAllFeedStates();
        UpdateRendering();
        UpdateHighlight();
    }

    void OnDestroy()
    {
        foreach (var p in pages)
        {
            if (p == null || p.slots == null) continue;
            foreach (var s in p.slots)
            {
                if (s == null) continue;
                if (s.camera != null) s.camera.targetTexture = null;
                ReleaseRT(s);
            }
        }

        if (zoomRT != null)
        {
            zoomRT.Release();
            Destroy(zoomRT);
        }
        if (generatedStatic != null) Destroy(generatedStatic);
    }

    // ------------------------------------------------------------------ setup helpers

    void NormalizeData()
    {
        if (pages == null) pages = new List<CameraPage>();
        for (int i = 0; i < pages.Count; i++)
        {
            if (pages[i] == null) pages[i] = new CameraPage();
            if (pages[i].slots == null) pages[i].slots = new MonitorSlot[0];
            for (int j = 0; j < pages[i].slots.Length; j++)
                if (pages[i].slots[j] == null) pages[i].slots[j] = new MonitorSlot();
        }
    }

    void ResolveCanvas()
    {
        if (monitorCanvas == null)
        {
            foreach (var p in pages)
            {
                foreach (var s in p.slots)
                {
                    if (s.screen == null) continue;
                    var c = s.screen.GetComponentInParent<Canvas>(true);
                    if (c != null) { monitorCanvas = c.rootCanvas; break; }
                }
                if (monitorCanvas != null) break;
            }
        }

        if (zoomLayer == null && monitorCanvas != null)
            zoomLayer = monitorCanvas.rootCanvas.transform as RectTransform;

        if (zoomLayer == null)
            Debug.LogWarning("CameraMonitorSystem: no canvas or zoom layer found, zooming won't work. Assign Monitor Canvas or Zoom Layer.", this);
    }

    void SetupDisplay()
    {
        // In the Editor, Display.displays.Length is always 1, so assume a second screen there.
        // Preview it with a Game view set to "Display 2".
        bool hasSecondScreen = Application.isEditor || Display.displays.Length > displayIndex;
        int target = hasSecondScreen ? displayIndex : 0;

        bool overlay = monitorCanvas != null && monitorCanvas.renderMode == RenderMode.ScreenSpaceOverlay;
        if (overlay) monitorCanvas.targetDisplay = target;

        if (uiCamera != null)
        {
            uiCamera.targetDisplay = target;
            if (!hasSecondScreen && splitScreenFallback)
                uiCamera.rect = new Rect(0.5f, 0f, 0.5f, 1f);
        }
        else if (!hasSecondScreen && splitScreenFallback && overlay)
        {
            Debug.LogWarning("CameraMonitorSystem: split-screen fallback needs a 'Screen Space - Camera' canvas with a UI Camera assigned. " +
                             "The monitors will fill the whole main display instead.", this);
        }
    }

    void InitSlot(MonitorSlot s)
    {
        if (s.camera != null) s.camera.enabled = false; // we switch it on only on the frames it should render
        s.state = FeedState.Unknown;
        s.bound = false;
        s.hasRenderedOnce = false;
        s.nextRenderTime = 0f;
    }

    static Texture2D BuildNoiseTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "GeneratedStatic",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Point
        };
        var px = new Color32[size * size];
        for (int i = 0; i < px.Length; i++)
        {
            byte v = (byte)Random.Range(0, 256);
            px[i] = new Color32(v, v, v, 255);
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    // ------------------------------------------------------------------ input

    static bool Pressed(KeyCode k) => k != KeyCode.None && Input.GetKeyDown(k);

    void HandleInput()
    {
        bool zoomPressed = Pressed(zoomKey);

        if (zoomedSlot != null)
        {
            if (zoomPressed) SetZoomTarget(zoomTarget > 0.5f ? 0f : 1f);
            else if (Pressed(zoomOutKey)) SetZoomTarget(0f);
            return;
        }

        if (transitioning) return; // ignore input while pages are sliding

        if (Pressed(nextPageKey)) TryChangePage(1);
        else if (Pressed(previousPageKey)) TryChangePage(-1);

        var page = CurrentPageData;
        if (page == null || page.slots.Length == 0) return;

        int dx = (Pressed(rightKey) ? 1 : 0) - (Pressed(leftKey) ? 1 : 0);
        int dy = (Pressed(downKey) ? 1 : 0) - (Pressed(upKey) ? 1 : 0);
        if (dx != 0) dy = 0;

        if (dx != 0 || dy != 0)
            Select(MoveSelection(selectedIndex, dx, dy, page.slots.Length, Mathf.Max(1, page.columns)));

        if (zoomPressed) BeginZoomIn();
    }

    /// <summary>Grid movement with wrap-around. dy: +1 = down, -1 = up. Handles a partly filled last row.</summary>
    static int MoveSelection(int index, int dx, int dy, int count, int cols)
    {
        int rows = Mathf.CeilToInt(count / (float)cols);
        int row = index / cols;
        int col = index % cols;

        if (dx != 0)
        {
            int rowLen = Mathf.Min(cols, count - row * cols);
            col = (col + dx + rowLen) % rowLen;
        }

        if (dy != 0)
        {
            do { row = (row + dy + rows) % rows; }
            while (row * cols + col >= count);
        }

        return row * cols + col;
    }

    void Select(int index)
    {
        var page = CurrentPageData;
        if (page == null || page.slots.Length == 0) return;

        index = Mathf.Clamp(index, 0, page.slots.Length - 1);
        if (index == selectedIndex) return;

        selectedIndex = index;
        page.lastSelected = index;
        page.slots[index].nextRenderTime = 0f; // new selection starts rendering at full rate right away
        ApplyTints();
        SelectionChanged?.Invoke(currentPage, index);
    }

    // ------------------------------------------------------------------ pages

    /// <summary>Call this from your level code when the player reaches a new level.</summary>
    public void UnlockPage(int index)
    {
        if (index < 0 || index >= pages.Count)
        {
            Debug.LogWarning("CameraMonitorSystem.UnlockPage: no page with index " + index, this);
            return;
        }

        var p = pages[index];

        if (!ready) { p.unlockedAtStart = true; return; } // called before Awake finished

        if (p.unlocked) return;
        p.unlocked = true;
        PageUnlocked?.Invoke(index);

        if (zoomedSlot == null && !transitioning && (currentPage < 0 || autoSwitchToUnlockedPage))
            ShowPage(index, currentPage < 0 ? 0 : (index > currentPage ? 1 : -1));
    }

    public bool IsPageUnlocked(int index)
    {
        return index >= 0 && index < pages.Count && pages[index].unlocked;
    }

    public bool SetPage(int index)
    {
        if (!IsPageUnlocked(index) || index == currentPage || zoomedSlot != null || transitioning) return false;
        ShowPage(index, currentPage < 0 ? 0 : (index > currentPage ? 1 : -1));
        return true;
    }

    /// <summary>
    /// Slides to the next (+1) or previous (-1) unlocked page.
    /// If there is no page on that side, the screen shakes instead and this returns false.
    /// </summary>
    public bool TryChangePage(int direction)
    {
        if (zoomedSlot != null || transitioning || direction == 0 || pages.Count == 0) return false;
        direction = direction > 0 ? 1 : -1;

        int n = pages.Count;
        int target = -1;

        if (wrapPages)
        {
            for (int step = 1; step < n; step++)
            {
                int idx = (((currentPage + direction * step) % n) + n) % n;
                if (pages[idx].unlocked) { target = idx; break; }
            }
        }
        else
        {
            for (int i = currentPage + direction; i >= 0 && i < n; i += direction)
            {
                if (pages[i].unlocked) { target = i; break; }
            }
        }

        if (target < 0 || target == currentPage)
        {
            PlayEdgeShake(direction);
            return false;
        }

        ShowPage(target, direction);
        return true;
    }

    public void CyclePage(int direction) => TryChangePage(direction);

    /// <param name="direction">+1 = new page slides in from the right, -1 = from the left, 0 = instant switch.</param>
    void ShowPage(int index, int direction = 0)
    {
        CancelShake();

        CameraPage oldPage = (currentPage >= 0 && currentPage < pages.Count) ? pages[currentPage] : null;
        if (oldPage != null)
        {
            foreach (var s in oldPage.slots)
                if (s.camera != null) s.camera.enabled = false;
        }

        currentPage = index;
        var page = pages[index];

        selectedIndex = Mathf.Clamp(page.lastSelected, 0, Mathf.Max(0, page.slots.Length - 1));

        // Fresh frame on every screen, then re-stagger.
        foreach (var s in page.slots)
        {
            s.hasRenderedOnce = false;
            s.nextRenderTime = 0f;
        }

        ApplyTints();

        bool animate = oldPage != null && oldPage != page && direction != 0 && pageSlideDuration > 0f;
        if (animate)
        {
            transitioning = true;
            StartCoroutine(PageSlide(oldPage, page, direction));
        }
        else
        {
            if (oldPage != null) SetPageVisible(oldPage, false);
            SetPageVisible(page, true);
        }

        PageChanged?.Invoke(index);
    }

    // ------------------------------------------------------------------ page slide + edge shake

    /// <summary>The RectTransforms that make up a page: its Page Root, or else each of its screens.</summary>
    List<RectTransform> GetPageTargets(CameraPage p)
    {
        var list = new List<RectTransform>();

        if (p.pageRoot != null)
        {
            var rt = p.pageRoot.transform as RectTransform;
            if (rt != null) { list.Add(rt); return list; }
        }

        foreach (var s in p.slots)
            if (s.screen != null) list.Add(s.screen.rectTransform);

        return list;
    }

    Vector2 RestPos(RectTransform rt)
    {
        if (!restPos.TryGetValue(rt, out var pos))
        {
            pos = rt.anchoredPosition;
            restPos[rt] = pos;
        }
        return pos;
    }

    /// <summary>Moves every target to its resting position plus an offset.</summary>
    void SetOffset(List<RectTransform> targets, Vector2 offset)
    {
        if (targets == null) return;
        foreach (var rt in targets)
            if (rt != null) rt.anchoredPosition = RestPos(rt) + offset;
    }

    IEnumerator PageSlide(CameraPage oldPage, CameraPage newPage, int direction)
    {
        var oldTargets = GetPageTargets(oldPage);
        var newTargets = GetPageTargets(newPage);

        // Remember where everything rests before anything is moved.
        foreach (var rt in oldTargets) RestPos(rt);
        foreach (var rt in newTargets) RestPos(rt);

        float width = (zoomLayer != null ? zoomLayer.rect.width : 1920f) * pageSlideDistance;

        // Put the new page just off-screen before it becomes visible, so it never flashes in place.
        SetOffset(newTargets, new Vector2(direction * width, 0f));
        SetPageVisible(newPage, true);

        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / Mathf.Max(0.01f, pageSlideDuration));
            float k = pageSlideCurve.Evaluate(t);

            // Old page leaves the opposite way the new one arrives.
            SetOffset(oldTargets, new Vector2(-direction * width * k, 0f));
            SetOffset(newTargets, new Vector2(direction * width * (1f - k), 0f));
            yield return null;
        }

        SetOffset(oldTargets, Vector2.zero);
        SetOffset(newTargets, Vector2.zero);
        SetPageVisible(oldPage, false);

        transitioning = false;
    }

    void PlayEdgeShake(int direction)
    {
        PageEdgeHit?.Invoke(direction);

        if (shakeDuration <= 0f || shakeStrength <= 0f) return;

        var page = CurrentPageData;
        if (page == null) return;

        CancelShake();

        shakeTargets = GetPageTargets(page);
        if (extraShakeTargets != null)
            foreach (var rt in extraShakeTargets)
                if (rt != null && !shakeTargets.Contains(rt)) shakeTargets.Add(rt);

        shakeRoutine = StartCoroutine(ShakeRoutine(shakeTargets, direction));
    }

    IEnumerator ShakeRoutine(List<RectTransform> targets, int direction)
    {
        foreach (var rt in targets) RestPos(rt);

        float elapsed = 0f;
        while (elapsed < 1f)
        {
            elapsed = Mathf.Min(1f, elapsed + Time.unscaledDeltaTime / shakeDuration);

            float decay = (1f - elapsed) * (1f - elapsed);

            // The first swing pushes towards the missing page, then it rebounds and settles.
            float x = direction * Mathf.Sin(elapsed * shakeCycles * Mathf.PI * 2f) * shakeStrength * decay;
            float y = Random.Range(-1f, 1f) * shakeStrength * 0.15f * decay;

            SetOffset(targets, new Vector2(x, y));
            yield return null;
        }

        SetOffset(targets, Vector2.zero);
        shakeRoutine = null;
    }

    void CancelShake()
    {
        if (shakeRoutine == null) return;
        StopCoroutine(shakeRoutine);
        shakeRoutine = null;
        SetOffset(shakeTargets, Vector2.zero);
    }

    static void SetPageVisible(CameraPage p, bool visible)
    {
        if (p.pageRoot != null)
        {
            p.pageRoot.SetActive(visible);
            return;
        }
        foreach (var s in p.slots)
            if (s.screen != null) s.screen.gameObject.SetActive(visible);
    }

    void ApplyTints()
    {
        var page = CurrentPageData;
        if (page == null) return;
        for (int i = 0; i < page.slots.Length; i++)
        {
            var s = page.slots[i];
            if (s.screen != null) s.screen.color = (i == selectedIndex) ? selectedColor : unselectedColor;
        }
    }

    // ------------------------------------------------------------------ feed state (live / static)

    bool IsFeedAvailable(MonitorSlot s)
    {
        if (s.camera == null) return false; // destroyed (or never assigned)
        if (inactiveCameraCountsAsDestroyed && !s.camera.gameObject.activeInHierarchy) return false;
        return true;
    }

    void RefreshAllFeedStates()
    {
        for (int p = 0; p < pages.Count; p++)
        {
            var slots = pages[p].slots;
            for (int i = 0; i < slots.Length; i++) RefreshFeedState(slots[i], p, i);
        }
    }

    void RefreshFeedState(MonitorSlot s, int pageIndex, int slotIndex)
    {
        FeedState desired = IsFeedAvailable(s) ? FeedState.Live : FeedState.Static;
        if (s.state == desired) return;

        bool wasLive = s.state == FeedState.Live;
        s.state = desired;

        if (desired == FeedState.Live)
        {
            s.bound = false;
            s.hasRenderedOnce = false;
            s.nextRenderTime = 0f;
            s.camera.enabled = false;
            if (s.screen != null) s.screen.uvRect = new Rect(0f, 0f, 1f, 1f);
        }
        else
        {
            if (s.camera != null)
            {
                s.camera.enabled = false;
                s.camera.targetTexture = null;
            }
            if (s.screen != null)
            {
                s.screen.texture = StaticTex;
                s.screen.uvRect = RandomStaticRect();
            }
            s.bound = false;
            ReleaseRT(s);

            if (wasLive) FeedLost?.Invoke(pageIndex, slotIndex);
        }
    }

    /// <summary>Assign or replace a camera at runtime (e.g. for a building that spawns later).</summary>
    public void AssignCamera(int pageIndex, int slotIndex, Camera cam)
    {
        if (pageIndex < 0 || pageIndex >= pages.Count) return;
        var s = GetSlot(pages[pageIndex], slotIndex);
        if (s == null) return;

        if (s.camera != null && s.camera != cam) s.camera.targetTexture = null;

        s.camera = cam;
        InitSlot(s);
    }

    void AnimateStatic()
    {
        if (!animateStatic) return;

        staticTimer -= Time.unscaledDeltaTime;
        if (staticTimer > 0f) return;
        staticTimer = Mathf.Max(0.01f, staticFlickerInterval);

        var page = CurrentPageData;
        if (page == null) return;
        foreach (var s in page.slots)
            if (s.state == FeedState.Static && s.screen != null)
                s.screen.uvRect = RandomStaticRect();
    }

    static Rect RandomStaticRect()
    {
        return new Rect(Random.value, Random.value, Random.Range(0.5f, 1f), Random.Range(0.5f, 1f));
    }

    // ------------------------------------------------------------------ rendering / performance

    static float Interval(int fps) => fps <= 0 ? 0f : 1f / fps;

    void UpdateRendering()
    {
        var page = CurrentPageData;
        if (page == null) return;

        float now = Time.unscaledTime;
        bool fullyZoomed = zoomedSlot != null && zoomT >= 1f && zoomTarget >= 1f;
        int count = page.slots.Length;

        for (int i = 0; i < count; i++)
        {
            var s = page.slots[i];
            if (s.state != FeedState.Live || s.camera == null) continue;

            if (!s.bound) Bind(s);

            bool isFocus = (i == selectedIndex);

            if (fullyZoomed && !isFocus && pauseOthersWhenZoomed)
            {
                s.camera.enabled = false;
                continue;
            }

            float interval = isFocus
                ? (zoomedSlot != null ? 0f : Interval(selectedFps))
                : Interval(backgroundFps);

            bool due = !s.hasRenderedOnce || now >= s.nextRenderTime;
            if (due)
            {
                // First render happens immediately for every screen; later ones are offset so they don't all land on one frame.
                float stagger = s.hasRenderedOnce ? 0f : interval * (i / (float)count);
                s.hasRenderedOnce = true;
                s.nextRenderTime = now + interval + stagger;
            }

            s.camera.enabled = due;
        }
    }

    void Bind(MonitorSlot s)
    {
        EnsureRT(s);
        var tex = TextureFor(s);
        s.camera.targetTexture = tex;
        if (s.screen != null) s.screen.texture = tex;
        s.bound = true;
    }

    RenderTexture TextureFor(MonitorSlot s)
    {
        return (s == zoomedSlot && zoomTarget > 0.5f && zoomRT != null) ? zoomRT : s.renderTexture;
    }

    void EnsureRT(MonitorSlot s)
    {
        if (s.renderTexture != null) return;
        s.renderTexture = new RenderTexture(Mathf.Max(16, feedResolution.x), Mathf.Max(16, feedResolution.y), 16)
        {
            name = "CameraFeed",
            filterMode = FilterMode.Bilinear
        };
        s.renderTexture.Create();
    }

    void EnsureZoomRT()
    {
        if (zoomRT != null) return;
        zoomRT = new RenderTexture(Mathf.Max(16, zoomResolution.x), Mathf.Max(16, zoomResolution.y), 24)
        {
            name = "CameraFeedZoom",
            filterMode = FilterMode.Bilinear
        };
        zoomRT.Create();
    }

    void ReleaseRT(MonitorSlot s)
    {
        if (s.renderTexture == null) return;
        if (s.camera != null && s.camera.targetTexture == s.renderTexture) s.camera.targetTexture = null;
        s.renderTexture.Release();
        Destroy(s.renderTexture);
        s.renderTexture = null;
    }

    // ------------------------------------------------------------------ zoom

    public void ZoomIn() => BeginZoomIn();

    public void ZoomOut() => SetZoomTarget(0f);

    public void ToggleZoom()
    {
        if (zoomedSlot == null) BeginZoomIn();
        else SetZoomTarget(zoomTarget > 0.5f ? 0f : 1f);
    }

    void BeginZoomIn()
    {
        if (zoomedSlot != null) { SetZoomTarget(1f); return; }
        if (zoomLayer == null) return;

        var s = GetSlot(CurrentPageData, selectedIndex);
        if (s == null || s.screen == null) return;
        if (s.state != FeedState.Live && !allowZoomOnStatic) return;
        if (transitioning) return;

        CancelShake(); // snap everything back to rest so the zoom starts from the true position

        // Move the screen into the zoom layer so it can cover the whole display, keeping its current look.
        var rt = s.screen.rectTransform;
        savedLayout = new LayoutSnapshot
        {
            parent = rt.parent,
            siblingIndex = rt.GetSiblingIndex(),
            anchorMin = rt.anchorMin,
            anchorMax = rt.anchorMax,
            offsetMin = rt.offsetMin,
            offsetMax = rt.offsetMax,
            pivot = rt.pivot,
            localScale = rt.localScale,
            localRotation = rt.localRotation
        };

        rt.SetParent(zoomLayer, true);
        rt.SetAsLastSibling();

        rt.GetWorldCorners(corners);
        Vector3 c0 = zoomLayer.InverseTransformPoint(corners[0]);
        Vector3 c2 = zoomLayer.InverseTransformPoint(corners[2]);

        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

        zoomStartSize = new Vector2(c2.x - c0.x, c2.y - c0.y);
        zoomStartPos = new Vector2((c0.x + c2.x) * 0.5f, (c0.y + c2.y) * 0.5f) - zoomLayer.rect.center;
        rt.sizeDelta = zoomStartSize;
        rt.anchoredPosition = zoomStartPos;

        zoomedSlot = s;
        zoomT = 0f;
        zoomTarget = 0f;
        SetZoomTarget(1f);
    }

    void SetZoomTarget(float t)
    {
        if (zoomedSlot == null || Mathf.Approximately(zoomTarget, t)) return;

        zoomTarget = t;
        SwapFeedTexture(zoomedSlot, t > 0.5f);

        if (t <= 0.5f) SetHiddenForZoom(false);
        ZoomChanged?.Invoke(t > 0.5f);
    }

    /// <summary>Switches the zoomed camera between its small feed texture and the shared high-res zoom texture.</summary>
    void SwapFeedTexture(MonitorSlot s, bool toZoom)
    {
        if (s.state != FeedState.Live || s.camera == null || s.screen == null) return;
        if (!s.bound) Bind(s);

        if (toZoom)
        {
            EnsureZoomRT();
            if (s.renderTexture != null) Graphics.Blit(s.renderTexture, zoomRT); // avoids a blank flash
            s.camera.targetTexture = zoomRT;
            s.screen.texture = zoomRT;
        }
        else
        {
            if (zoomRT != null && s.renderTexture != null) Graphics.Blit(zoomRT, s.renderTexture);
            s.camera.targetTexture = s.renderTexture;
            s.screen.texture = s.renderTexture;
        }

        s.nextRenderTime = 0f;
    }

    void UpdateZoomAnimation()
    {
        if (zoomedSlot == null) return;

        if (zoomedSlot.screen == null) { zoomedSlot = null; zoomT = zoomTarget = 0f; return; }

        zoomT = Mathf.MoveTowards(zoomT, zoomTarget, Time.unscaledDeltaTime / Mathf.Max(0.01f, zoomDuration));
        float k = zoomCurve.Evaluate(zoomT);

        var rt = zoomedSlot.screen.rectTransform;
        rt.anchoredPosition = Vector2.LerpUnclamped(zoomStartPos, Vector2.zero, k);
        rt.sizeDelta = Vector2.LerpUnclamped(zoomStartSize, zoomLayer.rect.size, k);

        if (zoomTarget >= 1f && zoomT >= 1f && !hiddenForZoom)
            SetHiddenForZoom(true);

        if (zoomTarget <= 0f && zoomT <= 0f)
            FinishZoomOut();
    }

    void FinishZoomOut()
    {
        var rt = zoomedSlot.screen.rectTransform;

        rt.SetParent(savedLayout.parent, false);
        rt.SetSiblingIndex(savedLayout.siblingIndex);
        rt.localScale = savedLayout.localScale;
        rt.localRotation = savedLayout.localRotation;
        rt.pivot = savedLayout.pivot;
        rt.anchorMin = savedLayout.anchorMin;
        rt.anchorMax = savedLayout.anchorMax;
        rt.offsetMin = savedLayout.offsetMin;
        rt.offsetMax = savedLayout.offsetMax;

        zoomedSlot = null;
        zoomT = 0f;
        zoomTarget = 0f;
    }

    void SetHiddenForZoom(bool hidden)
    {
        hiddenForZoom = hidden;
        if (hideWhenZoomed == null) return;
        foreach (var go in hideWhenZoomed)
            if (go != null) go.SetActive(!hidden);
    }

    // ------------------------------------------------------------------ selection highlight

    void UpdateHighlight()
    {
        if (selectionHighlight == null) return;

        var s = GetSlot(CurrentPageData, selectedIndex);
        bool show = s != null && s.screen != null && zoomedSlot == null;

        if (selectionHighlight.gameObject.activeSelf != show)
            selectionHighlight.gameObject.SetActive(show);
        if (!show) return;

        var parent = selectionHighlight.parent as RectTransform;
        if (parent == null) return;

        s.screen.rectTransform.GetWorldCorners(corners);
        Vector3 a = parent.InverseTransformPoint(corners[0]);
        Vector3 b = parent.InverseTransformPoint(corners[2]);

        selectionHighlight.pivot = new Vector2(0.5f, 0.5f);
        selectionHighlight.position = (corners[0] + corners[2]) * 0.5f;
        selectionHighlight.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Abs(b.x - a.x) + highlightPadding * 2f);
        selectionHighlight.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Abs(b.y - a.y) + highlightPadding * 2f);
    }
}