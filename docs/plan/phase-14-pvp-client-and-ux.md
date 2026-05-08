# Фаза 14 — Клиент PvP и UX дуэли

## Цель фазы

Полный клиентский поток: главный экран → постановка в очередь RTDB с heartbeat → экран поиска (экран 2) с **таймаутом 30 с** и предложением Ghost/Тренировки → присоединение к Photon комнате → экран Ready (экран 3) → синхронный countdown по `PhotonNetwork.ServerTimestamp` → HUD (экран 4): сериализация скелета **OnPhotonSerializeView** ~30 FPS, репы через **RaiseEvent** или надёжный канал → обработка дисконнекта **10 сек** → финиш → экран результата (5) и наград (6). Показать **онлайн-счётчик** игроков в очереди (агрегация RTDB `queue` или CF).

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

## Acceptance criteria

- [ ] Время от «искать» до старта матча ≤ целевое из ТЗ при наличии пары.
- [ ] Нет рассинхрона таймера между устройствами > 200 мс (проверить).
- [ ] Disconnect правило продукта: победа оставшемуся после таймаута — согласовано с webhook payload (`disconnectWinnerUid`).

## Тестирование

Два реальных устройства + kill network mid-match.

## Связь с дизайном

Экраны 1–6.
