using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using PushStars.Core;
using PushStars.OTA;
using PushStars.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    public sealed partial class LeagueView
    {
        private LeaguePage _cloud;
        private LeagueRow _lastOwn;
        private readonly List<LeagueRow> _players = new List<LeagueRow>();
        private readonly List<GameObject> _generated = new List<GameObject>();
        private GameObject _sticky;
        private int _generation;
        private bool _loading;
        private string _account, _archive = "", _selectedLeague = "", _shownArchive = "", _notice = "";
        private float _clockAt;
        private float _nextSeasonRetry;
        private long _serverAt;
        private Coroutine _poll;

        private void StartOnline()
        {
            _generation++;
            _loading = false;
            _poll = StartCoroutine(PollLeague());
        }
        private IEnumerator PollLeague()
        {
            while (true)
            {
                _ = LoadPage(false);
                yield return new WaitForSecondsRealtime(60);
            }
        }
        private void OnDisable()
        {
            Hero?.GetComponent<LeagueHeroSwipe>()?.CancelSlide();
            _generation++;
            _loading = false;
            if (_poll != null) StopCoroutine(_poll);
            _poll = null;
            ClearGenerated();
            _cloud = null;
            _selectedLeague = ""; _lastOwn = null; _players.Clear();
        }
        private void Update()
        {
            if (_cloud == null) return;
            if (_account != LeagueClient.Uid)
            {
                _generation++; _loading = false; _cloud = null; _lastOwn = null; _archive = ""; _selectedLeague = "";
                ClearGenerated(); Refresh(); _ = LoadPage(false); return;
            }
            if (Season == null) return;
            if (!string.IsNullOrEmpty(_notice)) { Season.text = _notice; return; }
            if (_archive != "") { Season.text = $"SEASON {_cloud.season.name} · FINAL RESULTS"; return; }
            long remaining = _cloud.season.endAtMs - (_serverAt + (long)((Time.realtimeSinceStartup - _clockAt) * 1000));
            if (remaining <= 0)
            {
                Season.text = "NEW SEASON · UPDATING…";
                if (!_loading && Time.realtimeSinceStartup >= _nextSeasonRetry)
                { _nextSeasonRetry = Time.realtimeSinceStartup + 60; _ = LoadPage(false, true); }
                return;
            }
            var span = TimeSpan.FromMilliseconds(remaining);
            Season.text = $"SEASON {_cloud.season.name} · {span.Days}D {span.Hours:00}H {span.Minutes:00}M";
        }
        private async Task LoadPage(bool more, bool force = false)
        {
            if (_loading || !isActiveAndEnabled) return;
            _loading = true;
            int version = _generation;
            string requestedSeason = _archive, requestedLeague = _selectedLeague;
            string cursor = more ? _cloud?.nextCursor ?? "" : "";
            if (Online != null) Online.text = "CONNECTING…";
            try
            {
                await FirebaseAuthService.EnsureReadyAsync();
                if (this == null || !isActiveAndEnabled || version != _generation) return;
                if (!more) await LeagueClient.FlushPending();
                if (this == null || !isActiveAndEnabled || version != _generation) return;
                var page = await LeagueClient.GetPage(cursor, requestedSeason, requestedLeague, force);
                if (this == null || !isActiveAndEnabled || version != _generation) return;
                string chosenName = ProfileIdentityEditor.ResolveName("");
                if (!more && _archive == "" && chosenName.Length >= 2 && chosenName.Length <= 20 && page.own?.displayName != chosenName)
                {
                    await LeagueClient.SyncDisplayName(chosenName);
                    if (this == null || !isActiveAndEnabled || version != _generation) return;
                    page = await LeagueClient.GetPage("", requestedSeason, requestedLeague, true);
                    if (this == null || !isActiveAndEnabled || version != _generation) return;
                }
                if (!more || _cloud == null || _cloud.season.id != page.season.id || _cloud.league != page.league) _players.Clear();
                ApplyStandings(page);
            }
            catch (Exception e)
            {
                if (this == null || !isActiveAndEnabled || version != _generation) return;
                if (Online != null) Online.text = _cloud != null ? "OFFLINE · SAVED TABLE" : "OFFLINE";
                _archive = _shownArchive;
                if (e is LeagueCallException call && call.Status == "NOT_FOUND")
                {
                    _notice = "NO RESULTS FOR THAT SEASON";
                    if (Online != null && _cloud != null) Online.text = $"{_cloud.total} PLAYERS";
                }
                if (Season != null && _cloud == null) Season.text = "RANKING UNAVAILABLE · RETRY BELOW";
                if (_cloud == null) ShowRetry();
                Debug.LogWarning("[League] " + e.Message);
            }
            finally { if (this != null && version == _generation) _loading = false; }
        }
        private void ApplyStandings(LeaguePage page)
        {
            _account = LeagueClient.Uid;
            _cloud = page; _lastOwn = page.own; _serverAt = page.serverTimeMs; _clockAt = Time.realtimeSinceStartup;
            _shownArchive = _archive;
            _notice = _archive != "" || string.IsNullOrEmpty(LeagueClient.LastSyncError) ? ""
                : LeagueClient.HasPendingResults ? "RESULT SYNC PENDING · RETRYING" : "LAST RANKED RESULT NOT ACCEPTED";
            foreach (var row in page.rows ?? Array.Empty<LeagueRow>())
            {
                int index = _players.FindIndex(p => p.uid == row.uid);
                if (index < 0) _players.Add(row); else _players[index] = row;
            }
            RenderOnline();
        }
        private void ClearGenerated()
        {
            foreach (var row in _generated) if (row != null) { row.SetActive(false); if (Application.isPlaying) Destroy(row); else DestroyImmediate(row); }
            _generated.Clear();
            if (_sticky != null) { _sticky.SetActive(false); if (Application.isPlaying) Destroy(_sticky); else DestroyImmediate(_sticky); _sticky = null; }
            var layout = GetComponent<LeagueLayout>();
            if (layout != null) layout.StickyFooterHeight = 0;
        }
        private Transform RowTemplate(bool own)
        {
            if (Names == null) return null;
            foreach (var name in Names)
                if (name != null && (name.transform.parent.name == "CurrentPlayerRow") == own) return name.transform.parent;
            return null;
        }
        private void RenderOnline()
        {
            if (_cloud == null) return;
            ClearGenerated();
            ApplyHero(HeroForLeague(_cloud.league));
            GetComponent<LeagueVisualStyle>()?.Refresh(_cloud.league, DisplayTrophies, _cloud.own != null);
            if (Title != null) Title.text = LeagueName;
            if (Score != null) Score.text = _cloud.own != null ? _cloud.own.trophies.ToString() : "—";
            if (ProgressBar != null) { ProgressBar.Fill = _cloud.own != null ? Progress : 0; ProgressBar.Refresh(); }
            if (Online != null) Online.text = $"{_cloud.total} PLAYERS";
            if (Names != null) foreach (var name in Names) if (name != null) name.transform.parent.gameObject.SetActive(false);
            var layout = GetComponent<LeagueLayout>();
            if (layout == null || layout.Rows == null) return;
            float y = 0;
            MakeButton(layout.Rows, "Ranked", _archive == "" ? (_cloud.seeded ? "PLAY RANKED" : "CALIBRATE RANK") : "CURRENT SEASON", y,
                () => { if (_archive == "") _ = PlayRanked(); else { _archive = ""; _ = LoadPage(false, true); } });
            // Ranked and archive actions share one row above the real leaderboard.
            MakeButton(layout.Rows, "Archive", "PREVIOUS SEASON", y, () =>
            {
                var month = DateTime.ParseExact(_cloud.season.id + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                _archive = month.AddMonths(-1).ToString("yyyy-MM");
                _ = LoadPage(false, true);
            });
            y += 82;
            var template = RowTemplate(false) ?? RowTemplate(true);
            foreach (var player in _players)
            {
                if (template == null) break;
                var go = Instantiate(template.gameObject, layout.Rows, false);
                _generated.Add(go); go.name = "OnlinePlayer_" + player.uid; go.SetActive(true);
                var rect = (RectTransform)go.transform;
                rect.localScale = Vector3.one;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -y);
                FillRow(go, player);
                y += 69;
            }
            if (!string.IsNullOrEmpty(_cloud.nextCursor)) MakeButton(layout.Rows, "LoadMore", "LOAD MORE", y, () => _ = LoadPage(true));
            var ownTemplate = RowTemplate(true) ?? template;
            if (_cloud.own != null && _cloud.own.league == _cloud.league && ownTemplate != null)
            {
                _sticky = Instantiate(ownTemplate.gameObject, transform, false);
                _sticky.name = "PinnedOwnRank"; _sticky.SetActive(true);
                var rect = (RectTransform)_sticky.transform;
                rect.anchorMin = new Vector2(0, 0); rect.anchorMax = new Vector2(1, 0); rect.pivot = new Vector2(.5f, 0);
                rect.localScale = Vector3.one;
                rect.offsetMin = new Vector2(12, 94); rect.offsetMax = new Vector2(-12, 155);
                FillRow(_sticky, _cloud.own);
                layout.StickyFooterHeight = 77;
            }
            layout.Fit();
        }
        private static void FillRow(GameObject go, LeagueRow row)
        {
            var group = go.GetComponent<CanvasGroup>(); if (group != null) { group.alpha = 1; group.blocksRaycasts = true; }
            foreach (var label in go.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                label.richText = false;
                if (label.name == "Rank") { label.text = row.rank.ToString(); label.enableAutoSizing = true; label.fontSizeMin = 9; }
                else if (label.name == "PlayerName") label.text = string.IsNullOrWhiteSpace(row.displayName) ? "PLAYER" : row.displayName;
                else if (label.name == "Trophies") label.text = row.trophies.ToString();
            }
        }
        private void MakeButton(RectTransform parent, string name, string text, float y, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            _generated.Add(go);
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            bool archive = name == "Archive", ranked = name == "Ranked";
            bool primary = ranked || name == "Retry";
            rect.sizeDelta = new Vector2(archive ? 108 : 248, 66);
            rect.anchoredPosition = new Vector2(archive ? 258 : ranked ? 0 : 59, -y);
            var style = GetComponent<LeagueVisualStyle>();
            var plate = go.GetComponent<Image>();
            plate.sprite = primary ? style?.RankedPlate : style?.SecondaryPlate;
            plate.color = plate.sprite != null ? Color.white : new Color32(43, 100, 245, 255);
            var button = go.GetComponent<Button>(); button.targetGraphic = plate;
            button.onClick.AddListener(() => { if (!_loading) action(); });
            if (archive && style != null && style.HistoryIcon != null)
            {
                var iconObject = new GameObject("HistoryIcon", typeof(RectTransform), typeof(Image));
                var icon = iconObject.GetComponent<Image>(); icon.transform.SetParent(go.transform, false);
                icon.sprite = style.HistoryIcon; icon.preserveAspect = true; icon.raycastTarget = false;
                icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(.5f, 1);
                icon.rectTransform.sizeDelta = new Vector2(42, 42); icon.rectTransform.anchoredPosition = new Vector2(0, -10);
            }
            var caption = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            caption.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)caption.transform; labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8, 5); labelRect.offsetMax = new Vector2(-8, archive ? -31 : -5);
            var label = caption.GetComponent<TextMeshProUGUI>(); label.text = archive ? "PREVIOUS\nSEASON" : text;
            label.font = Title != null ? Title.font : null; label.fontSharedMaterial = Title != null ? Title.fontSharedMaterial : null;
            label.fontSize = archive ? 12 : 23; label.enableAutoSizing = true; label.fontSizeMin = archive ? 10 : 15; label.fontSizeMax = archive ? 12 : 23;
            label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
        }
        private void ShowRetry()
        {
            ClearGenerated();
            var layout = GetComponent<LeagueLayout>();
            if (layout?.Rows != null) { MakeButton(layout.Rows, "Retry", "RETRY CONNECTION", 75, () => _ = LoadPage(false, true)); layout.Fit(); }
        }
        private async Task PlayRanked()
        {
            if (_loading) return;
            _loading = true;
            int version = _generation;
            if (Online != null) Online.text = "PREPARING RANKED SET…";
            try
            {
                var session = await LeagueClient.Begin(!_cloud.seeded);
                if (this == null || !isActiveAndEnabled || version != _generation) { await LeagueClient.Cancel(session); return; }
                LeagueClient.PreparedSession = session;
                if (session.mode == "assessment") FightRequest.LevelTest(); else FightRequest.Ghost();
                OtaSceneLoader.LoadScene(FightConfig.FightSceneName);
            }
            catch (Exception e)
            {
                if (this != null && isActiveAndEnabled && version == _generation) _notice = e.Message;
            }
            finally { if (this != null && version == _generation) _loading = false; }
        }
    }
}
