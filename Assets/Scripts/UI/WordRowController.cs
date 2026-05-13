using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BCIKeyboardXR.UI
{
    public class WordRowController : MonoBehaviour
    {
        private static readonly float[] FlickerFrequencies = { 8.0f, 8.5f, 9.0f, 9.5f, 10.0f, 10.5f };

        [SerializeField] private WordTile wordTilePrefab;
        [SerializeField] private RectTransform flyTarget;

        private readonly List<WordTile> _tiles = new List<WordTile>(6);

        public event Action<string> OnWordSelected;
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

        public void UpdateWords(List<string> words)
        {
            EnsureTiles();
            words ??= new List<string>();
            Clear();

            for (int i = 0; i < _tiles.Count; i++)
            {
                bool visible = i < words.Count && !string.IsNullOrWhiteSpace(words[i]);
                if (visible)
                {
                    _tiles[i].gameObject.SetActive(true);
                    _tiles[i].SetWord(words[i]);
                }
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _tiles.Count; i++)
            {
                _tiles[i].ClearWord();
                _tiles[i].gameObject.SetActive(false);
            }
        }

        private void EnsureTiles()
        {
            if (_tiles.Count == 6)
                return;

            _tiles.Clear();
            for (int i = 0; i < 6; i++)
            {
                WordTile tile = wordTilePrefab != null
                    ? Instantiate(wordTilePrefab, transform)
                    : CreateTile(transform);

                tile.name = $"WordTile_{i + 1}";
                tile.FlyTarget = flyTarget;
                tile.SetFlickerHz(FlickerFrequencies[i]);
                var layoutElement = tile.GetComponent<LayoutElement>() ?? tile.gameObject.AddComponent<LayoutElement>();
                layoutElement.preferredWidth = 260f;
                layoutElement.preferredHeight = 64f;
                tile.gameObject.SetActive(false);
                _tiles.Add(tile);
            }
        }

        private static WordTile CreateTile(Transform parent)
        {
            var go = new GameObject("WordTile", typeof(RectTransform), typeof(LayoutElement), typeof(WordTile));
            go.transform.SetParent(parent, false);
            return go.GetComponent<WordTile>();
        }

        private void Subscribe()
        {
            for (int i = 0; i < _tiles.Count; i++)
            {
                _tiles[i].OnWordSelected += HandleWordSelected;
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

                _tiles[i].OnWordSelected -= HandleWordSelected;
                _tiles[i].OnHoverPreview -= HandleHoverPreview;
                _tiles[i].OnHoverExit -= HandleHoverExit;
            }
        }

        private void HandleWordSelected(string word) => OnWordSelected?.Invoke(word);
        private void HandleHoverPreview(string word) => OnHoverPreview?.Invoke(word);
        private void HandleHoverExit() => OnHoverExit?.Invoke();
    }
}
