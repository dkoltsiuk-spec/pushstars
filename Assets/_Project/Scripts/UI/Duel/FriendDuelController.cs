using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Authored private-duel UX. The explicit Editor preview never launches a ghost match.</summary>
    public sealed class FriendDuelController : MonoBehaviour
    {
        public Button Slot, ModeButton, ExerciseButton, BattleButton;
        public Image PlusIcon;
        public GameObject FriendBadge;
        public TextMeshProUGUI SlotName, SlotStatus, BattleCaption;
        public ModeSelectionController ModeSelector;
        public BattleSettingsController ExerciseSettings;
        public Toast ModeHint;
        public Material InactiveModeMaterial;
        public FriendDuelPresentation Presentation;
        public Button FriendAvatarButton;
        public GameObject Overlay;
        public RectTransform Sheet;
        public Button Close, Backdrop, Primary, Secondary, Tertiary, Help, DemoAction, DemoConnection;
        public TextMeshProUGUI Title, Subtitle, Body, Message, CodeLabel;
        public TextMeshProUGUI PrimaryLabel, SecondaryLabel, TertiaryLabel, DemoLabel;
        public TextMeshProUGUI MeLabel, FriendLabel, MeStatus, FriendStatus;
        public GameObject CodeGroup, InputGroup, PlayersGroup, PreviewBadge;
        public TMP_InputField CodeInput;
        public FriendDuelSession Session { get; } = new FriendDuelSession();
        public bool HasRoom => Session.HasRoom;
        public bool IsOpen => Overlay != null && Overlay.activeSelf;
        public bool IsPreview => Application.isEditor;
        private enum Page { Menu, Join, ConfirmJoin, Room, Info, Leave }
        private Page _page, _returnPage;
        private string _candidateCode = "";
        private bool _wired;
        private float _nextRefresh;
        private Image[] _modeImages;
        private Material[] _modeMaterials;
        private TextMeshProUGUI[] _modeLabels;
        private Color[] _modeLabelColors;
        private bool _modeDrained;

        private void Start() => Initialize();
        public void Initialize()
        {
            if (_wired) return;
            _wired = true;
            Slot.onClick.AddListener(Open);
            ModeButton.onClick.AddListener(ExplainLockedMode);
            if (FriendAvatarButton != null) FriendAvatarButton.onClick.AddListener(Open);
            Close.onClick.AddListener(Back);
            Backdrop.onClick.AddListener(Back);
            Primary.onClick.AddListener(PrimaryAction);
            Secondary.onClick.AddListener(SecondaryAction);
            Tertiary.onClick.AddListener(TertiaryAction);
            Help.onClick.AddListener(ShowInfo);
            DemoAction.onClick.AddListener(PreviewAdvance);
            DemoConnection.onClick.AddListener(PreviewConnection);
            CodeInput.onValueChanged.AddListener(_ => Message.text = "");
            Overlay.SetActive(false); Refresh();
        }

        public void Open()
        {
            Session.Tick(Time.realtimeSinceStartupAsDouble);
            Show(Session.State == FriendDuelSession.Phase.Alone ? Page.Menu : Page.Room);
        }

        private void Show(Page page)
        {
            _page = page; Message.text = "";
            Overlay.SetActive(true); Overlay.transform.SetAsLastSibling();
            Refresh(); Fit();
        }

        public void Back()
        {
            if (_page == Page.Info) Show(_returnPage);
            else if (_page == Page.Leave) Show(Page.Room);
            else if (_page == Page.ConfirmJoin) Show(Page.Join);
            else Overlay.SetActive(false);
        }

        public void ShowInfo() { _returnPage = _page; Show(Page.Info); }

        private void ExplainLockedMode()
        {
            if (HasRoom) ModeHint?.Show("Only PvP duels support friends");
        }

        private void SetModeAppearance(bool locked)
        {
            if (_modeImages == null)
            {
                _modeImages = ModeButton.GetComponentsInChildren<Image>(true);
                _modeMaterials = new Material[_modeImages.Length];
                for (int i = 0; i < _modeImages.Length; i++) _modeMaterials[i] = _modeImages[i].material;
                _modeLabels = ModeButton.GetComponentsInChildren<TextMeshProUGUI>(true);
                _modeLabelColors = new Color[_modeLabels.Length];
                for (int i = 0; i < _modeLabels.Length; i++) _modeLabelColors[i] = _modeLabels[i].color;
            }
            if (_modeDrained == locked) return;
            _modeDrained = locked;
            for (int i = 0; i < _modeImages.Length; i++)
                _modeImages[i].material = locked ? InactiveModeMaterial : _modeMaterials[i];
            for (int i = 0; i < _modeLabels.Length; i++)
            {
                var color = _modeLabelColors[i];
                _modeLabels[i].color = locked ? new Color(color.grayscale, color.grayscale, color.grayscale, color.a) : color;
            }
        }

        public bool HandleBattle()
        {
            Session.Tick(Time.realtimeSinceStartupAsDouble);
            if (!HasRoom) { Refresh(); return false; }
            if (Session.State == FriendDuelSession.Phase.Joined || Session.State == FriendDuelSession.Phase.ReadyCheck)
                Session.Ready(Time.realtimeSinceStartupAsDouble);
            Show(Page.Room);
            return true;
        }

        public void PrimaryAction()
        {
            switch (_page)
            {
                case Page.Menu:
                    if (!RequirePreview()) return;
                    Session.Create("736482", Time.realtimeSinceStartupAsDouble); Show(Page.Room); break;
                case Page.Join:
                    if (!FriendDuelSession.TryCode(CodeInput.text, out _candidateCode))
                    { Message.text = "Enter a 6-digit code. Check your input."; return; }
                    if (!RequirePreview()) return;
                    if (_candidateCode == "000000") { Message.text = "Code expired. Ask your friend for a new one."; return; }
                    if (_candidateCode == "111111") { Message.text = "This room is already full."; return; }
                    if (_candidateCode == "736482") { Message.text = "This is your own invitation code."; return; }
                    if (_candidateCode != "482731") { Message.text = "Room not found. Check the code."; return; }
                    Show(Page.ConfirmJoin); break;
                case Page.ConfirmJoin:
                    if (!RequirePreview()) return;
                    Session.Join(_candidateCode, "ALEX_M"); Show(Page.Room); break;
                case Page.Leave:
                    Session.Reset(); Show(Page.Menu); break;
                case Page.Info: Back(); break;
                case Page.Room:
                    if (Session.State == FriendDuelSession.Phase.Waiting)
                    { GUIUtility.systemCopyBuffer = Session.Code; Message.text = "Code copied. Share it with your friend."; }
                    else if (Session.State == FriendDuelSession.Phase.Expired)
                    { Session.Reset(); Show(Page.Menu); }
                    else if (Session.State == FriendDuelSession.Phase.ReadyCheck && Session.LocalReady)
                    { Session.CancelReady(); Refresh(); }
                    else if (Session.State == FriendDuelSession.Phase.Preparing)
                    { Message.text = "Preview complete: both players confirmed.\nCameras and a shared start will arrive with live PvP."; }
                    else HandleBattle();
                    break;
            }
        }

        public void SecondaryAction()
        {
            if (_page == Page.Menu) Show(Page.Join);
            else if (_page == Page.Join || _page == Page.ConfirmJoin) Show(Page.Menu);
            else if (_page == Page.Room) Overlay.SetActive(false);
            else Back();
        }

        public void TertiaryAction()
        {
            if (_page == Page.Room && HasRoom) Show(Page.Leave);
            else Back();
        }

        private bool RequirePreview()
        {
            if (IsPreview) return true;
            Message.text = "Online invitations aren't available yet.\nTry a regular duel or training.";
            return false;
        }

        public void PreviewAdvance()
        {
            if (!IsPreview) return;
            Message.text = "";
            Session.Tick(Time.realtimeSinceStartupAsDouble);
            if (Session.State == FriendDuelSession.Phase.Waiting) Session.FriendJoined("ALEX_M");
            else if (Session.State == FriendDuelSession.Phase.Joined || Session.State == FriendDuelSession.Phase.ReadyCheck)
                Session.SetFriendReady(Time.realtimeSinceStartupAsDouble);
            Refresh();
        }

        public void PreviewConnection()
        {
            if (!IsPreview) return;
            Message.text = "";
            if (Session.State == FriendDuelSession.Phase.Reconnecting) Session.Reconnect(Time.realtimeSinceStartupAsDouble);
            else Session.Disconnect(Time.realtimeSinceStartupAsDouble);
            Refresh();
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + .2f;
                Session.Tick(Time.realtimeSinceStartupAsDouble); Refresh();
            }
            if (IsOpen) { Fit(); if (Input.GetKeyDown(KeyCode.Escape)) Back(); }
        }

        [ContextMenu("Preview/Friend leaves")]
        public void PreviewFriendLeft()
        {
            if (!IsPreview) return;
            Session.FriendLeft(); Show(Page.Room);
        }

        private void Fit()
        {
            var size = ((RectTransform)Sheet.parent).rect.size;
            Sheet.localScale = Vector3.one * Mathf.Min(size.x / 390f, size.y * .94f / 680f);
        }

        public void Refresh()
        {
            if (Slot == null) return;
            var phase = Session.State;
            bool room = HasRoom, friend = room && Session.HasFriend;
            Presentation?.Refresh(Session);
            bool hasPortrait = Presentation != null && Presentation.LoadedAvatar != null;
            PlusIcon.gameObject.SetActive(!friend); FriendBadge.SetActive(friend && !hasPortrait);
            bool showHomeEntry = room || PushStars.Core.SelectedGameMode.Current != PushStars.Core.GameMode.Boss;
            Slot.transform.parent.gameObject.SetActive(showHomeEntry && (!friend || !hasPortrait));
            SlotName.text = friend ? Session.FriendName : room ? "Invitation" : "With a friend";
            SlotStatus.text = friend ? phase == FriendDuelSession.Phase.Reconnecting ? "Disconnected" : "In room" : room ? "Waiting for friend" : "";
            // Keep the locked mode tappable so its restriction can be explained.
            ModeButton.interactable = ExerciseButton.interactable = true;
            BattleButton.interactable = !room || phase == FriendDuelSession.Phase.Joined
                || phase == FriendDuelSession.Phase.ReadyCheck && !Session.LocalReady;
            BattleCaption.text = room ? friend ? "VS " + Session.FriendName : "WAITING FOR FRIEND" : "";
            ModeSelector?.SetFriendDuelActive(room);
            ExerciseSettings?.SetFriendDuelActive(room);
            SetModeAppearance(room);
            if (!IsOpen) return;
            CodeGroup.SetActive(false); InputGroup.SetActive(false); PlayersGroup.SetActive(false);
            Primary.gameObject.SetActive(true); Secondary.gameObject.SetActive(true); Tertiary.gameObject.SetActive(false);
            Primary.interactable = true; Help.gameObject.SetActive(_page != Page.Info && _page != Page.Leave);
            PreviewBadge.SetActive(IsPreview);
            DemoAction.gameObject.SetActive(IsPreview && _page == Page.Room && (phase == FriendDuelSession.Phase.Waiting
                || phase == FriendDuelSession.Phase.Joined || phase == FriendDuelSession.Phase.ReadyCheck && !Session.FriendReady));
            DemoConnection.gameObject.SetActive(IsPreview && _page == Page.Room && room);
            DemoLabel.text = phase == FriendDuelSession.Phase.Waiting ? "Demo: friend joined" : "Demo: friend ready";
            DemoConnection.GetComponentInChildren<TextMeshProUGUI>().text = phase == FriendDuelSession.Phase.Reconnecting ? "Demo: reconnect" : "Demo: disconnect";
            Body.text = "";
            LayoutBody(144, 225);
            switch (_page)
            {
                case Page.Menu:
                    Title.text = "FRIEND DUEL"; Subtitle.text = "Challenge a friend. One on one.";
                    Body.text = "01  Create an invitation and share the code.\n\n02  Your friend enters it on their home screen.\n\n03  Confirm the duel and set up your cameras.\n\nPush-ups · 60 seconds\nNo ranked trophies";
                    PrimaryLabel.text = "INVITE FRIEND"; SecondaryLabel.text = "ENTER CODE"; break;
                case Page.Join:
                    Title.text = "ENTER CODE"; Subtitle.text = "Ask your friend to create an invitation\nand send you the 6-digit code.";
                    InputGroup.SetActive(true);
                    LayoutBody(265, 100);
                    Body.text = "You'll see who invited you first.\nJoin the room after confirming.";
                    PrimaryLabel.text = "FIND ROOM"; SecondaryLabel.text = "BACK"; break;
                case Page.ConfirmJoin:
                    Title.text = "INVITED BY"; Subtitle.text = "ALEX_M";
                    Body.text = "Private 1 × 1 duel\n\nPush-ups · 60 seconds\nNo ranked trophies\n\nThe duel starts when both players confirm\nand finish setting up their cameras.";
                    PrimaryLabel.text = "JOIN"; SecondaryLabel.text = "CANCEL"; break;
                case Page.Info:
                    LayoutBody(140, 265); Body.fontSize = 16;
                    Title.text = "HOW TO PLAY TOGETHER"; Subtitle.text = "No need to search for your friend's name.";
                    Body.text = "Inviting a friend?\nCreate an invitation and share the code.\nIt lasts for 5 minutes.\n\nGot an invitation?\nTap the plus on the left, then ENTER CODE.\nEnter 6 digits, check the name, and confirm.\n\nOnce you're together, both tap BATTLE.\nPosition your phones and set up the cameras.\nThe countdown starts when both are ready.";
                    PrimaryLabel.text = "GOT IT"; Secondary.gameObject.SetActive(false); break;
                case Page.Leave:
                    Title.text = "LEAVE ROOM?"; Subtitle.text = "Your friend will see that you left.";
                    Body.text = "Your invitation and ready status will be reset.\n\nCreate a new code to play again later.\n\nYour ranked trophies won't change.";
                    PrimaryLabel.text = "EXIT"; SecondaryLabel.text = "STAY"; break;
                case Page.Room: RefreshRoom(); break;
            }
        }

        private void RefreshRoom()
        {
            var phase = Session.State;
            Title.text = phase == FriendDuelSession.Phase.Waiting ? "INVITATION" : phase == FriendDuelSession.Phase.Expired ? "ROOM CLOSED" : "FRIEND DUEL";
            Subtitle.text = "Push-ups · 60 seconds · No trophies";
            SecondaryLabel.text = "HOME";
            Tertiary.gameObject.SetActive(HasRoom);
            TertiaryLabel.text = phase == FriendDuelSession.Phase.Waiting ? "CANCEL INVITATION" : "LEAVE ROOM";
            int left = Mathf.Max(0, (int)System.Math.Ceiling(Session.Deadline - Time.realtimeSinceStartupAsDouble));
            if (phase == FriendDuelSession.Phase.Waiting)
            {
                CodeGroup.SetActive(true); CodeLabel.text = Session.Code.Substring(0, 3) + " " + Session.Code.Substring(3);
                LayoutBody(246, 125);
                Body.text = $"Code expires in {left / 60}:{left % 60:00}\n\nYour friend taps plus, then ENTER CODE.\nYou can close this window while we wait.";
                PrimaryLabel.text = "COPY CODE"; return;
            }
            if (phase == FriendDuelSession.Phase.Expired)
            { Body.text = Session.Notice; PrimaryLabel.text = "NEW INVITATION"; return; }
            PlayersGroup.SetActive(true);
            LayoutBody(319, 55); Body.fontSize = 14;
            MeLabel.text = Session.IsHost ? "YOU · HOST" : "YOU · GUEST";
            FriendLabel.text = Session.FriendName;
            MeStatus.text = Session.LocalReady ? "Ready to battle" : "In room";
            FriendStatus.text = Session.FriendReady ? "Ready to battle" : "In room";
            if (phase == FriendDuelSession.Phase.Reconnecting)
            {
                MeStatus.text = FriendStatus.text = "Disconnected";
                Body.text = $"Reconnecting… {left} sec\nConfirm the duel again once connected.";
                PrimaryLabel.text = "RECONNECTING…"; Primary.interactable = false;
            }
            else if (phase == FriendDuelSession.Phase.Preparing)
            {
                Body.text = "Both players confirmed!\nNext: cameras and a shared 3–2–1 countdown.";
                PrimaryLabel.text = "GET READY";
            }
            else
            {
                Body.text = Session.LocalReady ? $"Waiting for your friend · {left} sec" : Session.FriendReady ? $"Your friend is ready! Confirm · {left} sec" : "Tap BATTLE when you're ready to play.";
                PrimaryLabel.text = Session.LocalReady ? "CANCEL READY" : "BATTLE";
                if (!string.IsNullOrEmpty(Session.Notice)) Body.text += "\n" + Session.Notice;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && HasRoom) { Session.Disconnect(Time.realtimeSinceStartupAsDouble); Refresh(); }
        }
        private void OnDisable() { if (Overlay != null) Overlay.SetActive(false); }
        private void OnEnable() { if (_wired) Refresh(); }
        private void LayoutBody(float top, float height)
        {
            Body.rectTransform.anchoredPosition = new Vector2(24, -top);
            Body.rectTransform.sizeDelta = new Vector2(342, height);
            Body.fontSize = 17;
        }
    }
}
