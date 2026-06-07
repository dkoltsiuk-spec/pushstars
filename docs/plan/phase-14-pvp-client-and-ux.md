# Фаза 14 — Клиент PvP и UX дуэли

## Цель фазы

Полный клиентский поток: главный экран → постановка в очередь RTDB с heartbeat → экран поиска (экран 2) с **таймаутом 30 с** → при отсутствии живой пары переход в async-дуэль ([фаза 12.5](phase-12.5-async-ghost-duels.md): чужой ghost → бот), Ghost-против-себя/тренировка как доп. варианты → присоединение к Photon комнате → экран Ready (экран 3) → синхронный countdown по `PhotonNetwork.ServerTimestamp` → HUD (экран 4): сериализация скелета **OnPhotonSerializeView** ~30 FPS, репы через **RaiseEvent** или надёжный канал → обработка дисконнекта **10 сек** → финиш → экран результата (5) и наград (6). Показать **онлайн-счётчик** игроков в очереди (агрегация RTDB `queue` или CF).

## Что НЕ делаем в этой фазе

- Друзья и deep links (фаза 16).
- FCM кампании (фаза 15).

## Предусловия

Фазы 08–10–12–13.

## Затрагиваемые экраны

1–6 полностью.

## Файлы

- `Assets/_Project/Scripts/Networking/PunConnectionService.cs`
- `Assets/_Project/Scripts/Networking/MatchmakingController.cs`
- `Assets/_Project/Scripts/Gameplay/DuelGameplayController.cs`
- `Assets/_Project/Scripts/UI/Duel/*` — поиск, VS, HUD, результаты

## Anti-cheat UI hooks (вынесено из phase-08.1, делается ЗДЕСЬ)

Phase-08.1 собрала всю CV-логику античита (`PushStars.CV.AntiCheat.*`) и debug HUD (`PushupDebugHud`). UI-слой дуэли — реальный HUD матча, тосты при reject-репах, 3-2-1 countdown — собирается в этой фазе, потому что должен жить в `Assets/_Project/Scripts/UI/Duel/` рядом с остальным дуэль-UX. Все нужные API/события на стороне CV уже есть.

### 14.A — Pre-match plank arming + 3-2-1 countdown

Решение пользователя (зафиксировано в phase-08.1 §Решения, пункт 5): после `PlankArmer.OnArmed` запускается визуальный 3 → 2 → 1 → GO, и **только** после «GO» `PushupRepCounter` начинает считать.

UX-поток (между «найден соперник» и «бой пошёл»):
1. Экран 3 (Ready) показывает соперника + надпись «**Встань в планку**» + большая иконка планки.
2. Юзер заходит в планку → `PushupSession.Armer.State` идёт `Disarmed → Arming`. На UI рисуется progress-ring заполняющийся за `PlankArmHoldSec=1.0s` (биндинг на `Armer.ArmingProgress01`).
3. Срабатывает `Armer.OnArmed` → запускается **3-2-1 countdown** (большие цифры на экране, каждая 1с, плюс звук тика).
4. Во время countdown продолжаем требовать валидную планку. Если `Armer.State` упал в `Disarmed` (грубый exit, не Cooling) → countdown отменяется, возврат в шаг 2 (без штрафа, просто перезапуск).
5. После «GO» → выставить `PushupRepCounter.CountingEnabled = true` (НОВЫЙ флаг — добавить в phase-14, в Stage 2 не добавлен) → счёт начинает идти, `OnRep` отрабатывается.

Создать:
- `Assets/_Project/Scripts/UI/Duel/MatchStartCountdown.cs` — MonoBehaviour-компонент. SerializeField на `PushupSession`. Подписан на `Armer.OnArmed` / `Armer.OnDisarmed`. Использует Coroutine для 3-секундного тика. Эмитит `OnCountdownComplete`.
- `Assets/_Project/Scripts/UI/Duel/PlankArmingPrompt.cs` — рендерит «Встань в планку» + progress-ring (биндинг `Armer.ArmingProgress01`). Скрывается когда `Armer.IsArmed`.

Добавить в CV (минимальная правка):
- `PushupRepCounter.CountingEnabled` (bool, default true для совместимости с тестами). В `Process`: если `!CountingEnabled` — early return (как с `isArmed`). `MatchStartCountdown` ставит `false` в Awake матча, `true` после countdown.

Координация с сетью: 3-2-1 countdown LOCAL для каждого игрока (стартует когда **этот** игрок armed). Photon-синхронизация (`PhotonNetwork.ServerTimestamp`) уже была в phase-14 как «синхронный countdown» — теперь это countdown про **start of match timer**, а не про plank arming. Уточнить с дизайном: ждём ли мы пока **оба** игрока armed → потом синхронный 3-2-1, или каждый стартует независимо. **Рекомендация:** ждём обоих → синхронный 3-2-1 → match timer стартует. Если кто-то слишком долго (>20с) не вооружается — соперник засчитывает walkover.

### 14.B — Per-rep reject feedback (тост «Грудь не опустилась!»)

Решение пользователя (phase-08.1 §Решения, пункт 3): subtle text-only flash + нейтральный «бзз» звук. Полноценный coaching overlay только при >3 rejected подряд.

`PushupSession.OnRepRejected(RepVote vote)` уже эмитится. `RepVote.Reason` это enum (`ChestNotLowered`, `Asymmetric`, `TooSlow`, `LowVisibility`, soft: `SlightAsymmetry`, `HipDecoupled`, `PoorTracking`). Также `PushupSession.Armer.OnDisarmed(PlankRejectReason reason)` для арминг-rejection (`KneesBent`, `BodySagging`, `WristsAirborne`, etc.).

Создать:
- `Assets/_Project/Scripts/UI/Duel/RepRejectToast.cs` — MonoBehaviour. SerializeField `PushupSession` + `AudioClip _buzzSound`. Подписан на `OnRepRejected`. Маппит `RepVote.Reason` → короткая локализованная строка («Грудь не опустилась!», «Слишком медленно!», «Руки в воздухе!», «Колени!», «Перекос!»). Показывает короткий fade-in/fade-out text в углу экрана (1.5с), играет «бзз».
- `Assets/_Project/Scripts/UI/Duel/CoachingHintOverlay.cs` — следит за последовательностью rejected, при ≥3 подряд показывает полноценный oversized hint в центре с пошаговой инструкцией («Опускайся ниже!» с иконкой). Сбрасывается на любой успешный реп.

Маппинг reason → строка (i18n-готовый, хранить в JSON под `Assets/_Project/Resources/i18n/` или ScriptableObject):

| RepVote.Reason | Тост ру |
|----------------|---------|
| `ChestNotLowered` | «Опускай грудь ниже» |
| `TooSlow` | «Не задерживайся» |
| `Asymmetric` | «Работай обеими руками» |
| `LowVisibility` | «Стой ровно в кадре» |
| `SlightAsymmetry` | (soft — без тоста, только дёрнуть FORM в углу) |
| `HipDecoupled` | (soft) |
| `PoorTracking` | (soft) |

| PlankRejectReason | Тост ру (показывается под progress-ring во время arming) |
|-------------------|-----------------------------------------------------------|
| `KneesBent` | «Встань на носки» |
| `BodySagging` | «Выпрями тело» |
| `WristsAirborne` | «Положи руки на пол» |
| `LowerBodyNotVisible` | «Отодвинься назад, видны должны быть ноги» |
| `NotAtTop` | «Выпрями руки» |
| `TrackingLost` | «Не вижу тебя» |

### 14.C — Live anti-cheat status в HUD матча

Не критично для MVP, можно отложить — но полезно для дебага и юзер-feedback:
- В углу HUD матча маленький индикатор «AC: OK» (зелёный) / «AC: WARNING» (жёлтый, если за последние 5 репов был хотя бы один SoftDock) / «AC: VETO» (красный flash на 1с при HardVeto).
- При расчёте финального счёта учитывать `vetoedReps` в телеметрии (не показывать юзеру, но логировать).

### Acceptance criteria для anti-cheat UI hooks
- [ ] Match не стартует пока юзер не вооружил планку.
- [ ] Countdown 3-2-1 отменяется при потере планки и перезапускается.
- [ ] При HardVeto репа юзер видит тост с причиной + слышит «бзз».
- [ ] При 3 подряд rejected — coaching hint в центре.
- [ ] Локализованные строки в i18n-файлах (рус — обязательно, англ — для App Store).

## Acceptance criteria

- [ ] Время от «искать» до старта матча ≤ целевое из ТЗ при наличии пары.
- [ ] Нет рассинхрона таймера между устройствами > 200 мс (проверить).
- [ ] Disconnect правило продукта: победа оставшемуся после таймаута — согласовано с webhook payload (`disconnectWinnerUid`).

## Тестирование

Два реальных устройства + kill network mid-match.

## Связь с дизайном

Экраны 1–6.
