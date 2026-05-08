# Firebase — архитектура и оптимизация затрат
## Calisthenics PvP — v2 (исправленная)

> **Что изменилось относительно v1:**
> - `matches` — добавлено поле `playerUids` для корректного OR-запроса
> - `users/{uid}` — добавлены `leaderboardRank`, `leaderboardLeague`, `offlineXpSyncToken`
> - Security Rules — клиент теперь может обновлять `fcmToken` и `displayName` напрямую
> - `onMatchFinished` — выбран механизм Photon Webhook вместо клиентского вызова
> - Античит — лимит репов зафиксирован как константа `MAX_REPS_PER_MATCH = 65`
> - Ghost Mode — добавлен полный флоу: поиск соперника, воспроизведение, синхронизация
> - `syncOfflineXp` — добавлен `offlineXpSyncToken` для идемпотентности
> - **v3 (Push Stars):** Ghost Mode — дуэль против **собственного лучшего рекорда**; трофеи ±половина от PvP; детали в [ghost-mode-spec.md](ghost-mode-spec.md) и числа в [constants.md](constants.md).

---

## Какие сервисы используем и зачем

| Сервис | Для чего |
|---|---|
| Firebase Auth | Авторизация игроков |
| Cloud Firestore | Профили, рейтинг, история матчей |
| Realtime Database | Матчмейкинг-очередь (только очередь, не дуэль) |
| Firebase Storage | Записи скелета для Ghost Mode |
| Cloud Messaging (FCM) | Push-уведомления |
| Crashlytics | Крэш-репорты |
| Analytics | Воронки, retention, события |
| Cloud Functions | Серверная валидация, античит, matchmaking |
| **Photon PUN2** | **Синхронизация скелета и репов во время дуэли** |

---

## Константы (единственный источник правды)

Полная таблица и пороги лиг: **[constants.md](constants.md)**.

```javascript
// functions/src/constants.js — синхронизировать с constants.md
const MAX_REPS_PER_MATCH = 65
const MATCH_DURATION_SEC = 60
const TROPHY_WIN = 25
const TROPHY_LOSS = 15
const TROPHY_GHOST_WIN = 12        // floor(TROPHY_WIN / 2)
const TROPHY_GHOST_LOSS = 7       // floor(TROPHY_LOSS / 2)
const MATCHMAKING_TIMEOUT_SEC = 35
const GHOST_MAX_FILE_BYTES = 204800 // 200 КБ
const LEADERBOARD_CACHE_SEC = 60
// см. ghost-mode-spec: один «best» на упражнение, без лимита «5 сессий» в MVP
```

---

## Cloud Firestore — структура коллекций

### `users/{uid}`

```
users/{uid}
  displayName: string
  avatarState: map            // состояние Genies-аватара (JSON)
  rank: string                // "bronze" | "silver" | "gold" | "diamond"
  trophies: number
  xp: number
  winStreak: number
  totalWins: number
  totalLosses: number
  totalReps: number
  winRate: number             // пересчитывается Cloud Function после каждого матча
  leaderboardRank: number     // позиция в своей лиге, обновляется recalculateLeaderboard
  leaderboardLeague: string   // "bronze" | "silver" | ... — копия rank на момент последнего пересчёта
  lastMatchAt: timestamp
  createdAt: timestamp
  fcmToken: string
  offlineXpPending: number
  offlineXpSyncToken: string  // UUID, генерируется клиентом при накоплении оффлайн-XP
                              // Cloud Function проверяет что токен не был использован ранее
```

**Нюанс:** `avatarState` — один большой map, не подколлекция. Обновление аватара = одна операция записи.

**Поля только для чтения клиентом** (пишет только Cloud Function): `rank`, `trophies`, `xp`, `winStreak`, `totalWins`, `totalLosses`, `totalReps`, `winRate`, `leaderboardRank`, `leaderboardLeague`, `lastMatchAt`.

**Поля которые клиент может писать напрямую**: `fcmToken`, `displayName`, `offlineXpPending`, `offlineXpSyncToken`.

---

### `seasons/{seasonId}`

```
seasons/{seasonId}
  startAt: timestamp
  endAt: timestamp
  isActive: boolean
  name: string              // "Season 1"
```

---

### `leagues/{leagueId}`

```
leagues/bronze
  name: string
  minTrophies: number       // 0
  maxTrophies: number       // 399
  iconUrl: string

leagues/silver
  minTrophies: number       // 400
  maxTrophies: number       // 799
  ...
```

---

### `leaderboard/{leagueId}/players/{uid}`

⚠️ Самая опасная коллекция по стоимости. Клиент читает только через кеш 60 сек. Пишет только Cloud Function.

```
leaderboard/bronze/players/{uid}
  displayName: string
  trophies: number
  winStreak: number
  leaderboardRank: number     // позиция, пересчитывается recalculateLeaderboard каждые 5 мин
  updatedAt: timestamp
```

---

### `matches/{matchId}`

```
matches/{matchId}
  playerAUid: string
  playerBUid: string
  playerUids: array           // [playerAUid, playerBUid] — для array-contains запроса
  winnerUid: string
  mode: string                // "pvp" | "ghost"
  exercise: string            // "pushups"
  durationSec: number         // 60
  playerAReps: number
  playerBReps: number
  playerATrophyDelta: number
  playerBTrophyDelta: number
  seasonId: string
  photonRoomId: string        // ID Photon-комнаты для аудита и отладки
  createdAt: timestamp
```

**Индексы Firestore (создать в консоли):**
- `matches` → составной: `playerUids ARRAY, createdAt DESC`

Один индекс вместо двух. Запрос истории матчей игрока:
```csharp
db.Collection("matches")
  .WhereArrayContains("playerUids", uid)
  .OrderBy("createdAt", descending: true)
  .Limit(20)
  .GetAsync();
```

> ❌ **Не делать так:**
> ```csharp
> // Не работает в Firestore — OR-запросов нет
> .WhereEqualTo("playerAUid", uid) // плюс .WhereEqualTo("playerBUid", uid)
> ```
> Два отдельных запроса + мерж на клиенте = сломанная пагинация.

---

### `ghost_sessions/{uid}/sessions/{sessionId}` (MVP: свой лучший рекорд)

Для MVP достаточно **одного** документа на упражнение (например id документа `pushups`):

```
ghost_sessions/{uid}/sessions/pushups
  exercise: "pushups"
  storagePath: string         // Firebase Storage: ghost/{uid}/.../best.bin
  bestRepsAtRecord: number    // репы той записи, против которой играем
  durationSec: number         // обычно 60
  recordedAt: timestamp
```

Подробнее — **[ghost-mode-spec.md](ghost-mode-spec.md)**. Поля `isPublic`, `trophiesAtRecord` и поиск чужих сессий **не используются** в MVP Ghost vs own best.

---

## Ghost Mode — полный флоу

Ghost Mode решает проблему пустого матчмейкинга на старте: игрок соревнуется с **записью своего лучшего результата** (скелет + счётчик репов для матча).

### Загрузка и воспроизведение .bin файла

```csharp
async Task<GhostData> LoadGhostFile(string storagePath) {
    var storageRef = FirebaseStorage.DefaultInstance.GetReference(storagePath);
    byte[] rawBytes = await storageRef.GetBytesAsync(GHOST_MAX_FILE_BYTES);
    return ParseGhostBinary(rawBytes);
}

GhostData ParseGhostBinary(byte[] data) {
    var frames = new List<GhostFrame>();
    int i = 0;
    while (i < data.Length) {
        uint timestamp = BitConverter.ToUInt32(data, i); i += 4;
        byte pointCount = data[i]; i += 1;
        var points = new Vector3[pointCount];
        for (int p = 0; p < pointCount; p++) {
            short x = BitConverter.ToInt16(data, i); i += 2;
            short y = BitConverter.ToInt16(data, i); i += 2;
            short z = BitConverter.ToInt16(data, i); i += 2;
            points[p] = new Vector3(x / 32767f, y / 32767f, z / 32767f);
        }
        frames.Add(new GhostFrame { TimestampMs = timestamp, Points = points });
    }
    return new GhostData { Frames = frames };
}
```

### Синхронизация воспроизведения с таймером матча (без Photon)

```csharp
// Ghost воспроизводится локально по времени с начала матча (60 сек)
void Update() {
    if (matchStatus != "active") return;

    long elapsedMs = (long)(Time.realtimeSinceStartup * 1000f) - matchStartLocalMs;

    var frame = ghostData.Frames
        .LastOrDefault(f => f.TimestampMs <= elapsedMs);

    if (frame != null) ApplySkeletonToAvatar(ghostAvatar, frame.Points);
}
```

**Photon не используется** в Ghost. Итог матча фиксирует **`onGhostMatchFinished`** (callable), включая трофеи ±ghost и запись в `matches` с `mode: "ghost"`. См. [ghost-mode-spec.md](ghost-mode-spec.md).

---

## Realtime Database — матчмейкинг

```
/matchmaking
  /queue
    /{uid}
      trophies: number
      rank: string
      joinedAt: number        // unix timestamp ms, heartbeat каждые 2 сек
      deviceId: string
```

Только для PvP-очереди. Ghost Mode не использует RTDB.

---

## Photon PUN2 — синхронизация дуэли

### Поток PvP-дуэли

```
1. Оба игрока в очереди RTDB
2. Cloud Function matchPlayers находит пару → создаёт Photon Room через Photon REST API
3. Оба клиента получают roomId из Firestore (Cloud Function записывает в matches/{matchId})
4. Клиенты присоединяются к комнате, она закрывается для новых участников
5. Матч 60 сек — всё через Photon
6. Комната закрывается → Photon сервер вызывает Webhook → Cloud Function onMatchFinished
```

> ✅ **onMatchFinished вызывает Photon сервер через Webhook, не клиент.**
> Это единственный надёжный механизм. Клиент не может подделать результат.

### Настройка Photon Webhook

В Photon Dashboard → Your App → Webhooks:

```
RoomClosed URL: https://{region}-{project}.cloudfunctions.net/onMatchFinished
PathPrefix: /
AuthCookie: {секретный токен, проверяется в Cloud Function}
```

Cloud Function проверяет заголовок `X-Photon-Token` при каждом вызове.

### Что синхронизируется через Photon

```csharp
// Custom Properties комнаты (медленные обновления — статус, таймер)
{
    "status": "countdown" | "active" | "finished",
    "startAt": long,
    "endAt": long,
    "repsA": int,           // обновляется через RaiseEvent, не SetCustomProperties
    "repsB": int,
    "disconnectedA": bool,
    "disconnectedB": bool
}

// Скелет — через PhotonView + OnPhotonSerializeView, 30 fps
void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info) {
    if (stream.IsWriting) {
        for (int i = 0; i < 17; i++) {
            stream.SendNext((short)(skeleton[i].x * 32767));
            stream.SendNext((short)(skeleton[i].y * 32767));
            stream.SendNext((short)(skeleton[i].z * 32767));
        }
    } else {
        for (int i = 0; i < 17; i++) {
            opponentSkeleton[i].x = (short)stream.ReceiveNext() / 32767f;
            opponentSkeleton[i].y = (short)stream.ReceiveNext() / 32767f;
            opponentSkeleton[i].z = (short)stream.ReceiveNext() / 32767f;
        }
    }
}
```

### Обработка разрыва соединения

```csharp
void OnPlayerLeftRoom(Player otherPlayer) {
    StartCoroutine(HandleDisconnectTimeout(otherPlayer.UserId, 10f));
}

IEnumerator HandleDisconnectTimeout(string disconnectedUid, float timeout) {
    yield return new WaitForSeconds(timeout);
    // Если не вернулся — комната закрывается, Photon Webhook сам вызовет onMatchFinished
    // с флагом disconnectWin в CustomProperties комнаты
    if (!PlayerReconnected(disconnectedUid)) {
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable {
            { "disconnectWinnerUid", PhotonNetwork.LocalPlayer.UserId }
        });
        PhotonNetwork.CurrentRoom.IsOpen = false;  // триггерит RoomClosed Webhook
    }
}
```

### Таймер без дрейфа

```csharp
void StartCountdown() {
    if (PhotonNetwork.IsMasterClient) {
        long startAt = PhotonNetwork.ServerTimestamp + 4000;
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable {
            { "startAt", startAt },
            { "status", "countdown" }
        });
    }
}

void Update() {
    long now = PhotonNetwork.ServerTimestamp;
    long startAt = (long)PhotonNetwork.CurrentRoom.CustomProperties["startAt"];
    float remaining = (startAt - now) / 1000f;
}
```

---

## Cloud Functions

### `onMatchFinished` (Photon Webhook — вызывается Photon сервером)

```javascript
// functions/src/onMatchFinished.js
const { MAX_REPS_PER_MATCH, TROPHY_WIN, TROPHY_LOSS } = require('./constants');

exports.onMatchFinished = functions.https.onRequest(async (req, res) => {
    // 1. Проверяем подпись Photon
    const token = req.headers['x-photon-token'];
    if (token !== process.env.PHOTON_WEBHOOK_SECRET) {
        return res.status(401).send('Unauthorized');
    }

    const { RoomName, Properties } = req.body;
    const { playerAUid, playerBUid, repsA, repsB, disconnectWinnerUid } = Properties;

    // 2. Античит: лимит репов
    if (repsA > MAX_REPS_PER_MATCH || repsB > MAX_REPS_PER_MATCH) {
        await flagSuspiciousMatch(RoomName, playerAUid, playerBUid);
        return res.status(200).send('Flagged');
    }

    // 3. Определяем победителя
    let winnerUid, loserUid;
    if (disconnectWinnerUid) {
        winnerUid = disconnectWinnerUid;
        loserUid = winnerUid === playerAUid ? playerBUid : playerAUid;
    } else {
        const aWins = repsA >= repsB;
        winnerUid = aWins ? playerAUid : playerBUid;
        loserUid  = aWins ? playerBUid : playerAUid;
    }

    // 4. Batch write: оба профиля + запись матча
    const batch = db.batch();

    batch.update(db.doc(`users/${winnerUid}`), {
        trophies: FieldValue.increment(TROPHY_WIN),
        totalWins: FieldValue.increment(1),
        winStreak: FieldValue.increment(1),
        totalReps: FieldValue.increment(winnerUid === playerAUid ? repsA : repsB),
        lastMatchAt: FieldValue.serverTimestamp(),
    });

    batch.update(db.doc(`users/${loserUid}`), {
        trophies: FieldValue.increment(-TROPHY_LOSS),
        totalLosses: FieldValue.increment(1),
        winStreak: 0,
        totalReps: FieldValue.increment(loserUid === playerAUid ? repsA : repsB),
        lastMatchAt: FieldValue.serverTimestamp(),
    });

    const matchRef = db.collection('matches').doc();
    batch.set(matchRef, {
        playerAUid, playerBUid,
        playerUids: [playerAUid, playerBUid],   // для array-contains запроса
        winnerUid,
        photonRoomId: RoomName,
        mode: 'pvp',
        exercise: 'pushups',
        durationSec: MATCH_DURATION_SEC,
        playerAReps: repsA,
        playerBReps: repsB,
        playerATrophyDelta: playerAUid === winnerUid ? TROPHY_WIN : -TROPHY_LOSS,
        playerBTrophyDelta: playerBUid === winnerUid ? TROPHY_WIN : -TROPHY_LOSS,
        createdAt: FieldValue.serverTimestamp(),
    });

    await batch.commit();

    // 5. Обновляем leaderboard и проверяем смену ранга (async, не блокируем ответ)
    updateLeaderboardEntry(winnerUid);
    updateLeaderboardEntry(loserUid);

    // 6. Push-уведомления
    sendMatchResultPush(winnerUid, loserUid);

    res.status(200).send('OK');
});
```

### `onGhostMatchFinished` (HTTP callable — вызывает клиент)

Ghost не проходит через Photon Webhook. Клиент вызывает callable; доверие ограничено античит-порогом `MAX_REPS_PER_MATCH`, идемпотентностью и последующим усилением (сверка с файлом — позже). В MVP Ghost — **игрок против своего рекорда**: начисляются **трофеи ± `TROPHY_GHOST_WIN` / `TROPHY_GHOST_LOSS`**, XP, streak, документ `matches`.

```javascript
const { MAX_REPS_PER_MATCH, TROPHY_GHOST_WIN, TROPHY_GHOST_LOSS } = require('./constants');

exports.onGhostMatchFinished = functions.https.onCall(async (data, context) => {
    if (!context.auth) throw new functions.https.HttpsError('unauthenticated');
    const { reps, ghostReps, matchNonce } = data; // ghostReps — счёт из записи best

    if (reps > MAX_REPS_PER_MATCH || ghostReps > MAX_REPS_PER_MATCH) {
        throw new functions.https.HttpsError('invalid-argument', 'Reps exceed maximum');
    }

    const uid = context.auth.uid;
    const won = reps > ghostReps;

    const trophyDelta = won ? TROPHY_GHOST_WIN : -TROPHY_GHOST_LOSS;

    await db.doc(`users/${uid}`).update({
        trophies: FieldValue.increment(trophyDelta),
        totalReps: FieldValue.increment(reps),
        xp: FieldValue.increment(reps * 10),
        totalWins: FieldValue.increment(won ? 1 : 0),
        totalLosses: FieldValue.increment(won ? 0 : 1),
        winStreak: won ? FieldValue.increment(1) : 0,
        lastMatchAt: FieldValue.serverTimestamp(),
    });

    // + batch.set matches/{id}) mode: 'ghost', playerUids: [uid], winnerUid, reps vs ghostReps
});
```

Полная бизнес-логика и поля матча — **[ghost-mode-spec.md](ghost-mode-spec.md)**.

### `cleanupMatchmaking` (scheduled, каждые 30 сек)

```javascript
exports.cleanupMatchmaking = functions.pubsub
  .schedule('every 1 minutes').onRun(async () => {
    const cutoff = Date.now() - (MATCHMAKING_TIMEOUT_SEC * 1000);
    const snap = await rtdb.ref('/matchmaking/queue')
      .orderByChild('joinedAt').endAt(cutoff).once('value');
    const updates = {};
    snap.forEach(child => { updates[child.key] = null; });
    if (Object.keys(updates).length > 0) {
        await rtdb.ref('/matchmaking/queue').update(updates);
    }
});
```

### `recalculateLeaderboard` (scheduled, каждые 5 минут)

Пересчитывает позиции в таблице лиги. Не в реальном времени — главная защита от дорогих reads.

### `syncOfflineXp` (HTTP callable)

```javascript
exports.syncOfflineXp = functions.https.onCall(async (data, context) => {
    if (!context.auth) throw new functions.https.HttpsError('unauthenticated');
    const { xpAmount, syncToken } = data;

    const userRef = db.doc(`users/${context.auth.uid}`);

    // Идемпотентность: проверяем что токен не был использован
    const userDoc = await userRef.get();
    if (userDoc.data().offlineXpSyncToken === syncToken) {
        return { status: 'already_synced' };   // дублированный вызов — ничего не делаем
    }

    await userRef.update({
        xp: FieldValue.increment(xpAmount),
        offlineXpPending: 0,
        offlineXpSyncToken: syncToken,          // запоминаем использованный токен
    });

    return { status: 'ok' };
});
```

Клиент генерирует `syncToken` один раз при накоплении XP и хранит в PlayerPrefs до успешной синхронизации:

```csharp
void AccumulateOfflineXp(int amount) {
    var currentXp = PlayerPrefs.GetInt("offlineXpPending", 0);
    if (currentXp == 0) {
        // Первое накопление — генерируем новый токен
        PlayerPrefs.SetString("offlineXpSyncToken", System.Guid.NewGuid().ToString());
    }
    PlayerPrefs.SetInt("offlineXpPending", currentXp + amount);
}

async void SyncOnReconnect() {
    var xp = PlayerPrefs.GetInt("offlineXpPending", 0);
    if (xp == 0) return;

    var token = PlayerPrefs.GetString("offlineXpSyncToken");
    var result = await FirebaseFunctions.DefaultInstance
        .GetHttpsCallable("syncOfflineXp")
        .CallAsync(new { xpAmount = xp, syncToken = token });

    // Сбрасываем только после подтверждённой записи
    PlayerPrefs.SetInt("offlineXpPending", 0);
    PlayerPrefs.DeleteKey("offlineXpSyncToken");
}
```

### `onUserDeleted` (Auth trigger)

Удаляет все данные пользователя. Обязательно для GDPR.

---

## Security Rules Firestore

```javascript
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {

    // Профиль — читает любой авторизованный
    // Клиент может обновлять только fcmToken, displayName, offlineXpPending, offlineXpSyncToken
    // Всё остальное пишет только Cloud Function
    match /users/{uid} {
      allow read: if request.auth != null;

      allow update: if request.auth.uid == uid
        && request.resource.data.diff(resource.data).affectedKeys()
           .hasOnly(['fcmToken', 'displayName', 'offlineXpPending', 'offlineXpSyncToken']);

      allow create: if false;  // создаёт Auth trigger Cloud Function
      allow delete: if false;  // удаляет onUserDeleted Cloud Function
    }

    // Таблица лиги — только чтение
    match /leaderboard/{league}/players/{uid} {
      allow read: if request.auth != null;
      allow write: if false;
    }

    // История матчей — читаем только свои матчи
    match /matches/{matchId} {
      allow read: if request.auth != null
        && request.auth.uid in resource.data.playerUids;
      allow write: if false;
    }

    // Ghost-сессии MVP — только свои метаданные (best); запись через CF / Storage rules
    match /ghost_sessions/{uid}/sessions/{sessionId} {
      allow read: if request.auth != null && uid == request.auth.uid;
      allow write: if false;
    }

    // Сезоны и лиги — только чтение
    match /seasons/{seasonId} {
      allow read: if request.auth != null;
      allow write: if false;
    }

    match /leagues/{leagueId} {
      allow read: if request.auth != null;
      allow write: if false;
    }
  }
}
```

---

## Security Rules RTDB

```json
{
  "rules": {
    "matchmaking": {
      "queue": {
        "$uid": {
          ".read": "auth != null",
          ".write": "auth != null && auth.uid === $uid",
          ".validate": "newData.hasChildren(['trophies', 'rank', 'joinedAt'])"
        }
      }
    }
  }
}
```

---

## Security Rules Storage

```javascript
rules_version = '2';
service firebase.storage {
  match /b/{bucket}/o {
    match /ghost/{uid}/{sessionId} {
      allow read: if request.auth != null;
      allow write: if false;  // только Cloud Functions (service account)
    }
  }
}
```

---

## История матчей — правильный запрос

```csharp
DocumentSnapshot lastVisible = null;

async Task LoadMatchHistory(bool loadMore = false) {
    var query = db.Collection("matches")
        .WhereArrayContains("playerUids", uid)   // один запрос вместо двух
        .OrderBy("createdAt", descending: true)
        .Limit(20);

    if (loadMore && lastVisible != null)
        query = query.StartAfterDocument(lastVisible);

    var snapshot = await query.GetAsync();
    lastVisible = snapshot.Documents.LastOrDefault();
    AppendToUI(snapshot);
}
```

---

## Обновление displayName (batch — атомарно)

```csharp
async Task UpdateDisplayName(string newName) {
    var rank = PlayerPrefs.GetString("playerRank", "bronze");
    var batch = db.StartBatch();

    // Security Rules разрешают клиенту писать displayName
    batch.Update(db.Document($"users/{uid}"),
        new Dictionary<string, object> { ["displayName"] = newName });

    // leaderboard пишет тоже клиент — нужно добавить разрешение в Rules
    // или вынести в Cloud Function updateDisplayName
    batch.Update(db.Document($"leaderboard/{rank}/players/{uid}"),
        new Dictionary<string, object> { ["displayName"] = newName });

    await batch.CommitAsync();
}
```

> ⚠️ **Внимание:** текущие Security Rules запрещают клиенту писать в `leaderboard`.
> Варианта два: добавить разрешение аналогичное `users` — или создать
> Cloud Function `updateDisplayName` которая пишет в оба места атомарно.
> Рекомендуем Cloud Function — меньше логики на клиенте.

---

## FCM — обновление токена (без лишних writes)

```csharp
void Start() {
    FirebaseMessaging.TokenReceived += OnTokenReceived;
}

void OnTokenReceived(object sender, TokenReceivedEventArgs e) {
    var savedToken = PlayerPrefs.GetString("fcm_token");
    if (e.Token != savedToken) {
        // Security Rules разрешают клиенту обновлять fcmToken напрямую
        db.Document($"users/{uid}").UpdateAsync(
            "fcmToken", e.Token
        );
        PlayerPrefs.SetString("fcm_token", e.Token);
    }
}
```

---

## FCM — payload шаблоны

```javascript
// Победа в дуэли
{
  notification: { title: "Победа! 🏆", body: `+${TROPHY_WIN} кубков` },
  data: { type: "match_result", result: "win", matchId: matchId }
}

// Поражение
{
  notification: { title: "Поражение", body: `${TROPHY_LOSS} кубков` },
  data: { type: "match_result", result: "loss", matchId: matchId }
}

// Стрик под угрозой
{
  notification: { title: "Серия под угрозой!", body: "Сыграй сегодня — не потеряй стрик" },
  data: { type: "streak_reminder" }
}

// Вызов от друга (будущий функционал)
{
  notification: { title: "{name} вызывает тебя!", body: "Прими вызов на дуэль" },
  data: { type: "friend_challenge", challengerUid: uid }
}

// Конец сезона
{
  notification: { title: "Сезон заканчивается", body: "Осталось 24 часа — зафиксируй ранг!" },
  data: { type: "season_ending", seasonId: seasonId }
}
```

---

## Оффлайн-режим

```csharp
FirebaseFirestore.DefaultInstance.Settings = new FirebaseFirestoreSettings {
    PersistenceEnabled = true,
    CacheSizeBytes = 10 * 1024 * 1024  // 10 МБ
};
```

Что работает оффлайн: чтение профиля (из кеша), накопление XP локально, тренировка без ранга.

Что не работает: дуэли (Photon PUN2 нужен интернет), Ghost Mode (нужна загрузка записи из Storage), обновление рейтинга в реальном времени.

---

## Бюджет reads/day при 500 DAU

| Событие | Reads | В день (500 DAU) |
|---|---|---|
| Открытие приложения (профиль) | 1 | 500 |
| Таблица лиги (раз в 60 сек, ~10 мин сессия) | 10 | 5 000 |
| История матчей (постранично) | 20 | 2 000 |
| Метаданные своей ghost-записи | 1 | 500 |
| Проверка сезона (кеш, раз в сессию) | 1 | 500 |
| **Итого** | | **~9 000 reads/day** |

Лимит Spark: 50 000 reads/day. Запас 5x при 500 DAU.

---

## Чеклист перед запуском

**Firebase:**
- [ ] Создать Firebase проект → получить `google-services.json` (Android) и `GoogleService-Info.plist` (iOS)
- [ ] Включить `PersistenceEnabled` для оффлайн-кеша
- [ ] Создать составной индекс: `matches` → `playerUids ARRAY, createdAt DESC`
- [ ] Задеплоить Security Rules для Firestore, RTDB и Storage
- [ ] Задеплоить все Cloud Functions
- [ ] Установить `PHOTON_WEBHOOK_SECRET` в Firebase environment config
- [ ] Установить бюджетный алерт в Google Cloud Console на $10/месяц
- [ ] Включить Crashlytics в Firebase Console
- [ ] Настроить APNs-сертификат → Firebase Console → Cloud Messaging → Apple

**Photon:**
- [ ] Зарегистрировать приложение на dashboard.photonengine.com → получить AppId
- [ ] Добавить `PhotonServerSettings` в Unity с AppId и регионом
- [ ] Настроить Photon Webhook → RoomClosed → URL Cloud Function `onMatchFinished`
- [ ] Добавить заголовок `X-Photon-Token` в Webhook и сохранить секрет в Firebase env
- [ ] Проверить лимит 20 CCU на бесплатном плане перед публичным запуском

**Unity:**
- [ ] Подключить MediaPipe Unity Plugin (Homuler/MediaPipeUnityPlugin)
- [ ] Подключить Genies SDK
- [ ] Реализовать алгоритм счёта репов
- [ ] Реализовать экран калибровки камеры
- [ ] Реализовать экран результата матча
- [ ] Реализовать онбординг с обучающей тренировкой
