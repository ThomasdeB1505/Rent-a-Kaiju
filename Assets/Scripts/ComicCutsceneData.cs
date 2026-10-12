using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace ComicCutscenes
{
    /// <summary>How a panel / text box appears when its step plays.</summary>
    public enum EnterAnimation
    {
        None,
        Fade,
        SlideFromLeft,
        SlideFromRight,
        SlideFromTop,
        SlideFromBottom,
        Pop,      // scale up with a little overshoot (good for speech bubbles)
        ZoomOut,  // starts slightly too big and settles (good for dramatic panels)
        Shake     // appears instantly and shakes (impacts, shouting)
    }

    /// <summary>One click-to-advance beat: reveal a panel or a text box.</summary>
    [Serializable]
    public class ComicStep
    {
        [Tooltip("Panel image or text box to reveal. A CanvasGroup is added automatically.")]
        public RectTransform target;

        public EnterAnimation animation = EnterAnimation.Fade;

        [Min(0f)] public float duration = 0.4f;

        [Tooltip("Pixels travelled by Slide animations; Shake uses 15% of this as its strength.")]
        [Min(0f)] public float slideDistance = 80f;

        [Tooltip("Optional. If set, this text is typed out letter by letter after the target appears.")]
        public TMP_Text typewriterText;

        [Tooltip("Play the next step automatically instead of waiting for a click (chain several panels).")]
        public bool autoAdvance;

        [Min(0f)] public float autoAdvanceDelay = 0.15f;

        [Tooltip("Optional one-shot sound when this step starts.")]
        public AudioClip sound;

        public UnityEvent onShown;
    }

    /// <summary>A comic page: a root object (background, frame...) plus the steps revealed on it.</summary>
    [Serializable]
    public class ComicPage
    {
        [Tooltip("Root of the page. Everything under it that is NOT a step target is visible as soon as the page opens.")]
        public RectTransform root;

        public List<ComicStep> steps = new List<ComicStep>();

        [Tooltip("Slow push-in on the whole page while it is shown. 1 = off.")]
        [Min(1f)] public float driftScale = 1.03f;

        [Min(0.1f)] public float driftDuration = 15f;
    }
}
