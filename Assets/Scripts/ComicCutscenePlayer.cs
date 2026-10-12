using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ComicCutscenes
{
    /// <summary>
    /// Plays a comic-panel cutscene: panels and text boxes appear one by one,
    /// click / Space / Enter / gamepad South advances. A click during an animation
    /// or while text is typing completes it instead of advancing.
    /// Put this on a full-screen RectTransform under a Canvas.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class ComicCutscenePlayer : MonoBehaviour, IPointerClickHandler
    {
        [Header("Content")]
        public List<ComicPage> pages = new List<ComicPage>();

        [Header("Playback")]
        public bool playOnStart = true;
        [Min(1f)] public float charactersPerSecond = 40f;
        [Tooltip("Extra pause after . , ! ? and similar.")]
        [Min(0f)] public float punctuationPause = 0.12f;
        [Min(0f)] public float fadeDuration = 0.5f;
        [Min(0f)] public float pageTransitionDuration = 0.35f;
        [Tooltip("Ignore inputs this soon after the last accepted one, so a double-click doesn't skip two beats.")]
        [Min(0f)] public float inputCooldown = 0.12f;

        [Header("Continue indicator (optional)")]
        public RectTransform continueIndicator;
        public float indicatorBobHeight = 6f;
        public float indicatorBobSpeed = 4f;

        [Header("Audio (optional)")]
        public AudioSource audioSource;
        public AudioClip typeBlip;
        [Min(1)] public int blipEveryNChars = 2;

        [Header("Skip (optional)")]
        public bool allowSkip = true;
        [Tooltip("Hold Escape / gamepad Start this long to skip the whole cutscene.")]
        [Min(0f)] public float holdToSkipSeconds = 1f;
        [Tooltip("An Image with Image Type = Filled, shows hold-to-skip progress.")]
        public Image skipProgressFill;

        [Header("Debug")]
        [Tooltip("Log each page/step and the finish to the Console.")]
        public bool logProgress = false;

        [Header("Events")]
        public UnityEvent onStarted;
        public UnityEvent onFinished;

        public bool IsPlaying { get; private set; }

        CanvasGroup _group;
        Coroutine _run;
        bool _advance;
        bool _skipRequested;
        float _skipHeld;
        float _lastAcceptedInput = -999f;
        Vector2 _indicatorRest;
        readonly Dictionary<RectTransform, Vector2> _restPos = new Dictionary<RectTransform, Vector2>();
        readonly Dictionary<RectTransform, Vector3> _restScale = new Dictionary<RectTransform, Vector3>();

        // ---------------------------------------------------------------- lifecycle

        void Awake()
        {
            _group = GetGroup((RectTransform)transform);

            // A transparent raycast target so clicks anywhere on screen reach us.
            if (GetComponent<Graphic>() == null)
            {
                var img = gameObject.AddComponent<Image>();
                img.color = Color.clear;
            }

            if (continueIndicator != null)
            {
                _indicatorRest = continueIndicator.anchoredPosition;
                continueIndicator.gameObject.SetActive(false);
            }

            CacheRestPoses();
            HideAll();
        }

        void Start()
        {
            if (playOnStart) Play();
        }

        void OnDisable()
        {
            if (IsPlaying)
            {
                IsPlaying = false;
                _run = null;
            }
        }

        void Update()
        {
            if (!IsPlaying) return;

            if (AdvancePressedThisFrame()) RequestAdvance();

            if (allowSkip)
            {
                _skipHeld = SkipHeld() ? _skipHeld + Time.unscaledDeltaTime : 0f;
                if (skipProgressFill != null)
                    skipProgressFill.fillAmount = holdToSkipSeconds > 0f ? Mathf.Clamp01(_skipHeld / holdToSkipSeconds) : 0f;
                if (_skipHeld >= holdToSkipSeconds) Skip();
            }
        }

        // ---------------------------------------------------------------- public API

        /// <summary>Start (or restart) the cutscene from the first page.</summary>
        public void Play()
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (_run != null) StopCoroutine(_run);
            CacheRestPoses();
            _run = StartCoroutine(Run());
        }

        /// <summary>Advance as if the player clicked.</summary>
        public void RequestAdvance()
        {
            if (!IsPlaying) return;
            if (Time.unscaledTime - _lastAcceptedInput < inputCooldown) return;
            _advance = true;
        }

        /// <summary>Jump to the end: fade out and fire onFinished.</summary>
        public void Skip()
        {
            if (IsPlaying) _skipRequested = true;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) RequestAdvance();
        }

        // ---------------------------------------------------------------- main sequence

        IEnumerator Run()
        {
            IsPlaying = true;
            _advance = false;
            _skipRequested = false;
            _skipHeld = 0f;

            HideAll();
            _group.blocksRaycasts = true;
            _group.interactable = true;
            onStarted?.Invoke();

            yield return Fade(_group, 0f, 1f, fadeDuration);

            for (int p = 0; p < pages.Count && !_skipRequested; p++)
            {
                ComicPage page = pages[p];
                if (page.root == null)
                {
                    Log($"Page {p + 1} has no Root, skipping");
                    continue;
                }
                Log($"Page {p + 1}/{pages.Count} ({page.root.name})");

                page.root.gameObject.SetActive(true);
                CanvasGroup pageGroup = GetGroup(page.root);
                pageGroup.alpha = 1f;
                Coroutine drift = StartCoroutine(Drift(page));

                for (int s = 0; s < page.steps.Count && !_skipRequested; s++)
                {
                    ComicStep step = page.steps[s];
                    Log($"  Step {s + 1}/{page.steps.Count} ({(step.target != null ? step.target.name : "no target")})");
                    yield return PlayStep(step);
                    if (_skipRequested) break;

                    if (step.autoAdvance) yield return AutoDelay(step.autoAdvanceDelay);
                    else
                    {
                        Log("  Waiting for click");
                        yield return WaitForAdvance();
                    }
                }

                bool isLast = p == pages.Count - 1;
                if (!isLast && !_skipRequested)
                {
                    yield return Fade(pageGroup, 1f, 0f, pageTransitionDuration);
                    if (drift != null) StopCoroutine(drift);
                    page.root.gameObject.SetActive(false);
                }
                else if (drift != null)
                {
                    StopCoroutine(drift);
                }
            }

            Log("Fading out");
            if (continueIndicator != null) continueIndicator.gameObject.SetActive(false);
            yield return Fade(_group, _group.alpha, 0f, fadeDuration);

            HideAll();
            IsPlaying = false;
            _run = null;
            Log($"Finished, calling On Finished ({onFinished.GetPersistentEventCount()} listener(s) set in inspector)");
            onFinished?.Invoke();
        }

        IEnumerator PlayStep(ComicStep step)
        {
            if (step.sound != null && audioSource != null) audioSource.PlayOneShot(step.sound);

            bool completedEarly = false;

            if (step.target != null)
            {
                step.target.gameObject.SetActive(true);
                CanvasGroup g = GetGroup(step.target);
                float t = 0f;
                while (t < 1f)
                {
                    if (_skipRequested) yield break;
                    if (ConsumeAdvance()) { completedEarly = true; break; }

                    t = step.duration <= 0f ? 1f : Mathf.Min(1f, t + Time.unscaledDeltaTime / step.duration);
                    Apply(step, g, t);
                    yield return null;
                }
                Apply(step, g, 1f);
            }

            step.onShown?.Invoke();

            if (step.typewriterText != null)
                yield return TypeText(step.typewriterText, completedEarly);
        }

        IEnumerator TypeText(TMP_Text text, bool instant)
        {
            text.maxVisibleCharacters = 0;
            text.ForceMeshUpdate();
            int total = text.textInfo.characterCount;

            if (instant || total == 0)
            {
                text.maxVisibleCharacters = int.MaxValue;
                yield break;
            }

            float budget = 0f;
            int shown = 0;

            while (shown < total)
            {
                if (_skipRequested || ConsumeAdvance()) break;

                budget += Time.unscaledDeltaTime * charactersPerSecond;
                while (budget >= 1f && shown < total)
                {
                    budget -= 1f;
                    shown++;

                    char c = text.textInfo.characterInfo[shown - 1].character;
                    if (typeBlip != null && audioSource != null && !char.IsWhiteSpace(c) && shown % blipEveryNChars == 0)
                        audioSource.PlayOneShot(typeBlip);

                    if (IsPunctuation(c))
                    {
                        budget -= punctuationPause * charactersPerSecond;
                        break;
                    }
                }

                text.maxVisibleCharacters = shown;
                yield return null;
            }

            text.maxVisibleCharacters = int.MaxValue;
        }

        IEnumerator WaitForAdvance()
        {
            if (continueIndicator != null) continueIndicator.gameObject.SetActive(true);

            float t = 0f;
            while (!_skipRequested && !ConsumeAdvance())
            {
                t += Time.unscaledDeltaTime;
                if (continueIndicator != null)
                    continueIndicator.anchoredPosition = _indicatorRest +
                        Vector2.up * (Mathf.Sin(t * indicatorBobSpeed) * indicatorBobHeight);
                yield return null;
            }

            if (continueIndicator != null)
            {
                continueIndicator.anchoredPosition = _indicatorRest;
                continueIndicator.gameObject.SetActive(false);
            }
        }

        IEnumerator AutoDelay(float seconds)
        {
            float t = 0f;
            while (t < seconds && !_skipRequested)
            {
                if (ConsumeAdvance()) yield break; // a click cuts the wait short
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        IEnumerator Drift(ComicPage page)
        {
            Vector3 rest = RestScale(page.root);
            page.root.localScale = rest;
            if (page.driftScale <= 1f) yield break;

            float t = 0f;
            while (true)
            {
                t += Time.unscaledDeltaTime / page.driftDuration;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                page.root.localScale = rest * Mathf.Lerp(1f, page.driftScale, e);
                yield return null;
            }
        }

        static IEnumerator Fade(CanvasGroup g, float from, float to, float duration)
        {
            float t = 0f;
            while (t < 1f)
            {
                t = duration <= 0f ? 1f : Mathf.Min(1f, t + Time.unscaledDeltaTime / duration);
                g.alpha = Mathf.Lerp(from, to, EaseOutCubic(t));
                yield return null;
            }
            g.alpha = to;
        }

        // ---------------------------------------------------------------- animation

        void Apply(ComicStep step, CanvasGroup g, float t)
        {
            RectTransform rt = step.target;
            Vector2 pos = RestPos(rt);
            Vector3 scale = RestScale(rt);
            float e = EaseOutCubic(t);
            float d = step.slideDistance * (1f - e);

            rt.anchoredPosition = pos;
            rt.localScale = scale;
            g.alpha = e;

            switch (step.animation)
            {
                case EnterAnimation.None:
                    g.alpha = 1f;
                    break;
                case EnterAnimation.Fade:
                    break;
                case EnterAnimation.SlideFromLeft:
                    rt.anchoredPosition = pos + Vector2.left * d;
                    break;
                case EnterAnimation.SlideFromRight:
                    rt.anchoredPosition = pos + Vector2.right * d;
                    break;
                case EnterAnimation.SlideFromTop:
                    rt.anchoredPosition = pos + Vector2.up * d;
                    break;
                case EnterAnimation.SlideFromBottom:
                    rt.anchoredPosition = pos + Vector2.down * d;
                    break;
                case EnterAnimation.Pop:
                    g.alpha = Mathf.Clamp01(t * 4f);
                    rt.localScale = scale * Mathf.LerpUnclamped(0.6f, 1f, EaseOutBack(t));
                    break;
                case EnterAnimation.ZoomOut:
                    rt.localScale = scale * Mathf.Lerp(1.25f, 1f, e);
                    break;
                case EnterAnimation.Shake:
                    g.alpha = 1f;
                    if (t < 1f)
                        rt.anchoredPosition = pos + Random.insideUnitCircle * (step.slideDistance * 0.15f * (1f - t));
                    break;
            }
        }

        static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        static bool IsPunctuation(char c) =>
            c == '.' || c == ',' || c == '!' || c == '?' || c == ';' || c == ':' || c == '…' ||
            c == '。' || c == '，' || c == '！' || c == '？';

        // ---------------------------------------------------------------- state helpers

        void Log(string message)
        {
            if (logProgress) Debug.Log($"[Comic {name}] {message}", this);
        }

        bool ConsumeAdvance()
        {
            if (!_advance) return false;
            _advance = false;
            _lastAcceptedInput = Time.unscaledTime;
            return true;
        }

        void HideAll()
        {
            if (_group == null) _group = GetGroup((RectTransform)transform);
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            foreach (ComicPage page in pages)
            {
                if (page.root == null) continue;
                page.root.localScale = RestScale(page.root);
                GetGroup(page.root).alpha = 1f;

                foreach (ComicStep step in page.steps)
                {
                    if (step.target != null)
                    {
                        GetGroup(step.target).alpha = 0f;
                        step.target.anchoredPosition = RestPos(step.target);
                        step.target.localScale = RestScale(step.target);
                    }
                    if (step.typewriterText != null) step.typewriterText.maxVisibleCharacters = 0;
                }

                page.root.gameObject.SetActive(false);
            }
        }

        void CacheRestPoses()
        {
            foreach (ComicPage page in pages)
            {
                if (page.root == null) continue;
                Cache(page.root);
                foreach (ComicStep step in page.steps)
                    if (step.target != null) Cache(step.target);
            }
        }

        void Cache(RectTransform rt)
        {
            if (!_restPos.ContainsKey(rt)) _restPos[rt] = rt.anchoredPosition;
            if (!_restScale.ContainsKey(rt)) _restScale[rt] = rt.localScale;
        }

        Vector2 RestPos(RectTransform rt) => _restPos.TryGetValue(rt, out var p) ? p : rt.anchoredPosition;
        Vector3 RestScale(RectTransform rt) => _restScale.TryGetValue(rt, out var s) ? s : rt.localScale;

        static CanvasGroup GetGroup(RectTransform rt)
        {
            var g = rt.GetComponent<CanvasGroup>();
            return g != null ? g : rt.gameObject.AddComponent<CanvasGroup>();
        }

        // ---------------------------------------------------------------- input (old or new Input System)

        static bool AdvancePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame ||
                               kb.numpadEnterKey.wasPressedThisFrame))
                return true;
            var gp = Gamepad.current;
            return gp != null && gp.buttonSouth.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
                   Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.JoystickButton0);
#endif
        }

        static bool SkipHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.isPressed) return true;
            var gp = Gamepad.current;
            return gp != null && gp.startButton.isPressed;
#else
            return Input.GetKey(KeyCode.Escape) || Input.GetKey(KeyCode.JoystickButton7);
#endif
        }
    }
}