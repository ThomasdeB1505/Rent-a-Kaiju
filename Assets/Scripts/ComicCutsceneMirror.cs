using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ComicCutscenes
{
    public class ComicCutsceneMirror : MonoBehaviour
    {
        public ComicCutscenePlayer cutscene;
        public Camera comicCamera;
        public List<RawImage> displayImages = new List<RawImage>();
        public Vector2Int resolution = new Vector2Int(1920, 1080);

        RenderTexture _texture;

        void Awake()
        {
            _texture = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.ARGB32);
            _texture.name = "ComicCutsceneRT";
            comicCamera.targetTexture = _texture;

            foreach (RawImage image in displayImages)
            {
                if (image == null) continue;
                image.texture = _texture;
                image.raycastTarget = true;
                image.enabled = false;

                // Forward mouse clicks on this display to the cutscene.
                var trigger = image.gameObject.GetComponent<EventTrigger>();
                if (trigger == null) trigger = image.gameObject.AddComponent<EventTrigger>();
                var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                entry.callback.AddListener(_ => cutscene.RequestAdvance());
                trigger.triggers.Add(entry);
            }
        }

        void Update()
        {
            bool show = cutscene != null && cutscene.IsPlaying;
            foreach (RawImage image in displayImages)
                if (image != null && image.enabled != show) image.enabled = show;
        }

        void OnDestroy()
        {
            if (comicCamera != null) comicCamera.targetTexture = null;
            if (_texture != null) _texture.Release();
        }
    }
}