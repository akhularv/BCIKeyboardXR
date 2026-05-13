using System;
using System.Collections.Generic;
using UnityEngine;

namespace BCIKeyboardXR.UI
{
    public class PhraseRowController : MonoBehaviour
    {
        private static readonly float[] FlickerFrequencies = { 6.0f, 6.5f, 7.0f, 7.5f };

        [SerializeField] private PhraseTile phraseTilePrefab;
        [SerializeField] private RectTransform flyTarget;

        private readonly List<PhraseTile> _tiles = new List<PhraseTile>(4);

        public event Action<string> OnPhraseSelected;
        public event Action<string> OnHoverPreview;
        public event Action OnHoverExit;

        public RectTransform FlyTarget
        {
            get => flyTarget;
            set
            {
                flyTarget = value;
                for (int i = 0; i < _tiles.Count; i++)
                    _tiles[i].FlyTarget = flyTarget;
            }
        }

        private void Awake()
        {
            EnsureTiles();
        }

        private void OnEnable()
        {
            EnsureTiles();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void UpdatePhrases(List<string> phrases)
        {
            EnsureTiles();
            phrases ??= new List<string>();

            for (int i = 0; i < _tiles.Count; i++)
            {
                bool visible = i < phrases.Count && !string.IsNullOrWhiteSpace(phrases[i]);
                _tiles[i].gameObject.SetActive(visible);
                if (visible)
                    _tiles[i].SetPhrase(phrases[i]);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _tiles.Count; i++)
                _tiles[i].gameObject.SetActive(false);
        }

        private void EnsureTiles()
        {
            if (_tiles.Count == 4)
                return;

            _tiles.Clear();
            for (int i = 0; i < 4; i++)
            {
                PhraseTile tile = phraseTilePrefab != null
                    ? Instantiate(phraseTilePrefab, transform)
                    : CreateTile(transform);

                tile.name = $"PhraseTile_{i + 1}";
                tile.FlyTarget = flyTarget;
                tile.SetFlickerHz(FlickerFrequencies[i]);
                tile.gameObject.SetActive(false);
                _tiles.Add(tile);
            }
        }

        private static PhraseTile CreateTile(Transform parent)
        {
            var go = new GameObject("PhraseTile", typeof(RectTransform), typeof(PhraseTile));
            go.transform.SetParent(parent, false);
            return go.GetComponent<PhraseTile>();
        }

        private void Subscribe()
        {
            for (int i = 0; i < _tiles.Count; i++)
            {
                _tiles[i].OnPhraseSelected += HandlePhraseSelected;
                _tiles[i].OnHoverPreview += HandleHoverPreview;
                _tiles[i].OnHoverExit += HandleHoverExit;
            }
        }

        private void Unsubscribe()
        {
            for (int i = 0; i < _tiles.Count; i++)
            {
                if (_tiles[i] == null)
                    continue;

                _tiles[i].OnPhraseSelected -= HandlePhraseSelected;
                _tiles[i].OnHoverPreview -= HandleHoverPreview;
                _tiles[i].OnHoverExit -= HandleHoverExit;
            }
        }

        private void HandlePhraseSelected(string phrase) => OnPhraseSelected?.Invoke(phrase);
        private void HandleHoverPreview(string phrase) => OnHoverPreview?.Invoke(phrase);
        private void HandleHoverExit() => OnHoverExit?.Invoke();
    }
}
