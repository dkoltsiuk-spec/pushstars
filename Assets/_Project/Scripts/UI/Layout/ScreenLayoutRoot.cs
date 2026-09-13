using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI.Layout
{
    /// <summary>
    /// One independently editable screen. Register stable groups after constructing dynamic UI,
    /// then apply once after show-time placement. Animation should be on children of those groups.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class ScreenLayoutRoot : MonoBehaviour
    {
        [Serializable]
        public sealed class Target
        {
            public string id;
            public RectTransform rect;
        }

        private const string PreferencePrefix = "PushStars.ScreenLayout.v1.";
        [SerializeField] private string _screenId;
        [SerializeField] private bool _autoDiscover = true;
        [SerializeField] private bool _showEditButton = true;
        [SerializeField, Tooltip("The scene's RectTransforms are the source of truth. Legacy saved layouts are not reapplied.")]
        private bool _sceneAuthored;
        [SerializeField] private List<Target> _targets = new List<Target>();
        private readonly Dictionary<string, ScreenLayoutElementState> _original = new Dictionary<string, ScreenLayoutElementState>();
        private ScreenLayoutData _beforeEdit;
        private ScreenLayoutData _resetLayout;
        private ScreenLayoutOverlay _overlay;
        private GameObject _launcher;
        private bool _applyPending;
        private bool _resetPending;
        private bool _available = true;

        public string ScreenId => string.IsNullOrEmpty(_screenId) ? gameObject.name : _screenId;
        public IReadOnlyList<Target> Targets => _targets;
        public bool IsEditing { get; private set; }
        public bool IsSceneAuthored => _sceneAuthored;

        /// <summary>Called once by the migration after baking legacy positions into the scene.</summary>
        public void MarkSceneAuthored()
        {
            _sceneAuthored = true;
            ShowEditButton = false;
            _applyPending = false;
        }
        public bool AvailableForEditing => _available;
        public static ScreenLayoutRoot EditingRoot { get; private set; }
        public static bool IsAnyEditing => EditingRoot != null;
        public static event Action<string> PreviewRequested;
        public static event Action<ScreenLayoutRoot, bool> EditingChanged;
        public event Action LayoutSaved;

        public bool ShowEditButton
        {
            get => _showEditButton;
            set { _showEditButton = value; if (_launcher != null) _launcher.SetActive(value && _available && !IsEditing); }
        }

        public void SetAvailable(bool available)
        {
            if (available && !_available) _applyPending = true;
            _available = available;
            if (!available && IsEditing) CancelEditing();
            if (_launcher != null) _launcher.SetActive(_showEditButton && available && !IsEditing && isActiveAndEnabled);
        }

        public static void RequestPreview(string screenId) => PreviewRequested?.Invoke(screenId);

        public void Configure(string screenId, bool autoDiscover = true)
        {
            if (string.IsNullOrWhiteSpace(screenId)) throw new ArgumentException("A stable screen id is required.", nameof(screenId));
            if (_screenId != screenId)
            {
                if (IsEditing) CancelEditing();
                _original.Clear();
                _targets.Clear();
            }
            _screenId = screenId;
            _autoDiscover = autoDiscover;
            _applyPending = true;
        }

        public void Register(string stableId, RectTransform target)
        {
            if (target == null || target == transform || string.IsNullOrEmpty(stableId)) return;
            var ownerCanvas = GetComponentInParent<Canvas>();
            var targetCanvas = target.GetComponentInParent<Canvas>();
            if (!target.IsChildOf(transform) && (ownerCanvas == null || targetCanvas == null || ownerCanvas.rootCanvas != targetCanvas.rootCanvas))
            {
                Debug.LogWarning($"Layout target '{target.name}' must share the canvas of '{name}'.", this);
                return;
            }
            var existing = _targets.Find(item => item != null && item.id == stableId) ?? _targets.Find(item => item != null && item.rect == target);
            // A caller may replace id A with a rect previously registered as B. Keep both ids
            // and rect references unique, including after a dynamic screen rebuild.
            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                var duplicate = _targets[i];
                if (duplicate == null) { _targets.RemoveAt(i); continue; }
                if (duplicate == existing || (duplicate.id != stableId && duplicate.rect != target)) continue;
                if (!string.IsNullOrEmpty(duplicate.id)) _original.Remove(duplicate.id);
                _targets.RemoveAt(i);
            }
            if (existing == null) _targets.Add(new Target { id = stableId, rect = target });
            else
            {
                if ((existing.rect != target || existing.id != stableId) && !string.IsNullOrEmpty(existing.id)) _original.Remove(existing.id);
                existing.id = stableId;
                existing.rect = target;
            }
            if (!_original.ContainsKey(stableId)) _original.Add(stableId, ScreenLayoutElementState.Capture(stableId, target));
        }

        public void RefreshTargets()
        {
            _targets.RemoveAll(item => item == null || item.rect == null || string.IsNullOrEmpty(item.id));
            if (_autoDiscover)
            {
                foreach (var rect in GetComponentsInChildren<RectTransform>(true))
                {
                    if (rect == transform || rect.GetComponentInParent<ScreenLayoutRoot>() != this) continue;
                    if (IsEditorChrome(rect) || IsDecorative(rect.name)) continue;
                    var graphic = rect.GetComponent<Graphic>();
                    if (graphic == null && rect.GetComponent<Selectable>() == null) continue;
                    var selectableParent = rect.parent != null ? rect.parent.GetComponentInParent<Selectable>() : null;
                    if (selectableParent != null && selectableParent.transform.IsChildOf(transform)) continue;
                    if (_targets.Exists(item => item.rect == rect)) continue;
                    Register(PathFor(rect), rect);
                }
            }
            foreach (var target in _targets)
                if (!_original.ContainsKey(target.id)) _original.Add(target.id, ScreenLayoutElementState.Capture(target.id, target.rect));
        }

        private bool IsEditorChrome(Transform item)
        {
            for (var current = item; current != null && current != transform; current = current.parent)
                if (current.name.StartsWith("__Layout", StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool IsDecorative(string itemName)
        {
            string lower = itemName.ToLowerInvariant();
            return lower.Contains("background") || lower.Contains("backdrop") || lower.Contains("watermark") ||
                   lower.Contains("lightning") || lower.Contains("tapblock") || lower.Contains("shadow");
        }

        private string PathFor(Transform target)
        {
            var path = new StringBuilder();
            for (Transform item = target; item != null && item != transform; item = item.parent)
            {
                int sameNameIndex = 0;
                for (int i = 0; i < item.GetSiblingIndex(); i++)
                    if (item.parent.GetChild(i).name == item.name) sameNameIndex++;
                if (path.Length > 0) path.Insert(0, "/");
                path.Insert(0, Uri.EscapeDataString(item.name) + "#" + sameNameIndex);
            }
            return path.ToString();
        }

        private void OnEnable() => _applyPending = true;

        private void LateUpdate()
        {
            if (_applyPending && _available && !IsEditing) ApplySavedLayout();
            if (_showEditButton && _available && _launcher == null && !string.IsNullOrEmpty(_screenId))
                _launcher = ScreenLayoutOverlay.CreateLauncher(this);
            if (_launcher != null && _launcher.activeSelf != (_showEditButton && _available && !IsEditing))
                _launcher.SetActive(_showEditButton && _available && !IsEditing);
        }

        private void OnDisable()
        {
            if (IsEditing) CancelEditing();
            if (_launcher != null) _launcher.SetActive(false);
        }

        public ScreenLayoutData CaptureLayout()
        {
            RefreshTargets();
            var data = new ScreenLayoutData { screenId = ScreenId };
            foreach (var target in _targets)
                if (target.rect != null) data.elements.Add(ScreenLayoutElementState.Capture(target.id, target.rect));
            return data;
        }

        public void ApplyLayout(ScreenLayoutData data)
        {
            if (data == null || data.version != 1 || data.screenId != ScreenId || data.elements == null) return;
            RefreshTargets();
            foreach (var element in data.elements)
            {
                if (element == null || !element.IsValid) continue;
                var target = _targets.Find(item => item.id == element.id);
                if (target != null) element.Apply(target.rect);
            }
        }

        public void ApplySavedLayout()
        {
            if (IsEditing) return;
            _applyPending = false;
            RefreshTargets();
            if (_sceneAuthored) return;
            ApplyAuthoredLayout();
            string json = PlayerPrefs.GetString(PreferencePrefix + ScreenId, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            try { ApplyLayout(JsonUtility.FromJson<ScreenLayoutData>(json)); }
            catch (ArgumentException) { Debug.LogWarning($"Ignoring invalid saved screen layout: {ScreenId}", this); }
        }

        private void ApplyAuthoredLayout()
        {
            if (_sceneAuthored) return;
            var catalog = Resources.Load<ScreenLayoutCatalog>(ScreenLayoutCatalog.ResourcePath);
            if (catalog != null) ApplyLayout(catalog.Find(ScreenId));
        }

        public void BeginEditing()
        {
            if (IsEditing || !isActiveAndEnabled || !_available) return;
            if (EditingRoot != null) EditingRoot.CancelEditing();
            if (_applyPending) ApplySavedLayout();
            _beforeEdit = CaptureLayout();
            _resetPending = false;
            IsEditing = true;
            EditingRoot = this;
            _overlay = ScreenLayoutOverlay.Open(this);
            if (_launcher != null) _launcher.SetActive(false);
            EditingChanged?.Invoke(this, true);
        }

        public void SaveLayout()
        {
            var data = CaptureLayout();
            if (_resetPending && _resetLayout != null && JsonUtility.ToJson(data) == JsonUtility.ToJson(_resetLayout))
                PlayerPrefs.DeleteKey(PreferencePrefix + ScreenId);
            else PlayerPrefs.SetString(PreferencePrefix + ScreenId, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
            FinishEditing();
            LayoutSaved?.Invoke();
        }

        public void CancelEditing()
        {
            if (!IsEditing) return;
            ApplyLayout(_beforeEdit);
            FinishEditing();
        }

        /// <summary>
        /// Call after an editor writes the current layout into the shared catalog. Accepts the
        /// current edit and removes the device override so it cannot mask the newly authored data.
        /// </summary>
        public void AcceptSavedDefaults()
        {
            FinishEditing();
            PlayerPrefs.DeleteKey(PreferencePrefix + ScreenId);
            PlayerPrefs.Save();
            ApplySavedLayout();
            LayoutSaved?.Invoke();
        }

        private void FinishEditing()
        {
            bool wasEditing = IsEditing;
            IsEditing = false;
            if (EditingRoot == this) EditingRoot = null;
            if (_overlay != null)
            {
                if (Application.isPlaying) Destroy(_overlay.gameObject);
                else DestroyImmediate(_overlay.gameObject);
            }
            _overlay = null;
            _beforeEdit = null;
            _resetLayout = null;
            _resetPending = false;
            if (_launcher != null) _launcher.SetActive(_showEditButton && _available && isActiveAndEnabled);
            if (wasEditing) EditingChanged?.Invoke(this, false);
        }

        /// <summary>While editing, reset is provisional until Save, and Cancel restores the prior layout.</summary>
        public void ResetToDefaults()
        {
            RefreshTargets();
            foreach (var target in _targets)
                if (_original.TryGetValue(target.id, out var state)) state.Apply(target.rect);
            ApplyAuthoredLayout();
            if (IsEditing)
            {
                _resetPending = true;
                _resetLayout = CaptureLayout();
            }
            else
            {
                PlayerPrefs.DeleteKey(PreferencePrefix + ScreenId);
                PlayerPrefs.Save();
            }
        }
    }
}
