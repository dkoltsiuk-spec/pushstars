using System;
using System.Text;

namespace PushStars.UI
{
    /// <summary>Local room state; remote changes must come from an authoritative room adapter.</summary>
    public sealed class FriendDuelSession
    {
        public enum Phase { Alone, Waiting, Joined, ReadyCheck, Preparing, Reconnecting, Expired }
        public Phase State { get; private set; }
        public bool IsHost { get; private set; }
        public string Code { get; private set; } = "";
        public string FriendName { get; private set; } = "";
        public string FriendAvatarId { get; private set; } = "male";
        public string Notice { get; private set; } = "";
        public bool LocalReady { get; private set; }
        public bool FriendReady { get; private set; }
        public double Deadline { get; private set; }
        private double _invitationDeadline;
        public bool HasRoom => State != Phase.Alone && State != Phase.Expired;
        public bool HasFriend => !string.IsNullOrEmpty(FriendName);

        public static bool TryCode(string value, out string code)
        {
            var digits = new StringBuilder();
            foreach (char c in value ?? "")
            {
                if (c >= '0' && c <= '9') digits.Append(c);
                else if (!char.IsWhiteSpace(c) && c != '-') { code = ""; return false; }
            }
            code = digits.ToString();
            return code.Length == 6;
        }

        public void Create(string code, double now)
        {
            if (HasRoom) return;
            if (!TryCode(code, out var normalized)) throw new ArgumentException("Expected six digits.");
            Reset(); Code = normalized; IsHost = true; Deadline = _invitationDeadline = now + 300; State = Phase.Waiting;
        }

        public void Join(string code, string host, string avatarId = "male")
        {
            if (HasRoom) return;
            if (!TryCode(code, out var normalized)) throw new ArgumentException("Expected six digits.");
            Reset(); Code = normalized; FriendName = host; FriendAvatarId = avatarId; State = Phase.Joined;
        }

        public void FriendJoined(string name, string avatarId = "male")
        {
            if (State != Phase.Waiting || string.IsNullOrWhiteSpace(name)) return;
            FriendName = name; FriendAvatarId = avatarId; Deadline = 0; State = Phase.Joined; Notice = "Друг подключился";
        }

        public void UpdateFriendAppearance(string avatarId)
        { if (HasRoom && HasFriend) FriendAvatarId = avatarId; }

        public void Ready(double now)
        {
            Tick(now);
            if (State != Phase.Joined && State != Phase.ReadyCheck) return;
            if (LocalReady) return;
            LocalReady = true;
            if (State == Phase.Joined) Deadline = now + 30;
            State = Phase.ReadyCheck; Notice = "";
            CheckReady();
        }

        public void SetFriendReady(double now)
        {
            bool stale = State == Phase.ReadyCheck && now >= Deadline;
            Tick(now);
            if (stale) return;
            if (State != Phase.Joined && State != Phase.ReadyCheck) return;
            if (FriendReady) return;
            FriendReady = true;
            if (State == Phase.Joined) Deadline = now + 30;
            State = Phase.ReadyCheck; CheckReady();
        }

        private void CheckReady()
        {
            if (!LocalReady || !FriendReady) return;
            State = Phase.Preparing; Deadline = 0;
        }

        public void CancelReady()
        {
            if (State != Phase.ReadyCheck && State != Phase.Preparing) return;
            LocalReady = FriendReady = false; Deadline = 0; State = Phase.Joined;
            Notice = "Готовность сброшена. Можно начать снова.";
        }

        public void Disconnect(double now)
        {
            if (!HasRoom || State == Phase.Reconnecting) return;
            LocalReady = FriendReady = false; Deadline = now + 10;
            State = Phase.Reconnecting; Notice = "Восстанавливаем связь…";
        }

        public void Reconnect(double now)
        {
            if (State != Phase.Reconnecting || now >= Deadline) { Tick(now); return; }
            State = HasFriend ? Phase.Joined : Phase.Waiting;
            Deadline = HasFriend ? 0 : _invitationDeadline;
            Notice = "Связь восстановлена. Подтверди готовность снова.";
            Tick(now);
        }

        public void FriendLeft()
        {
            if (!HasRoom) return;
            State = Phase.Expired; Deadline = 0; LocalReady = FriendReady = false;
            Notice = "Друг вышел из комнаты. Можешь пригласить его снова.";
        }

        public void Tick(double now)
        {
            if (Deadline <= 0 || now < Deadline) return;
            if (State == Phase.ReadyCheck) { CancelReady(); Notice = "Время подтверждения вышло. Попробуйте снова."; }
            else if (State == Phase.Waiting || State == Phase.Reconnecting)
            {
                Notice = State == Phase.Waiting ? "Срок кода истёк. Создай новое приглашение." : "Связь потеряна. Комната закрыта.";
                State = Phase.Expired; Deadline = 0; LocalReady = FriendReady = false;
            }
        }

        public void Reset()
        {
            State = Phase.Alone; IsHost = false; Code = FriendName = Notice = "";
            FriendAvatarId = "male";
            LocalReady = FriendReady = false; Deadline = _invitationDeadline = 0;
        }
    }
}
