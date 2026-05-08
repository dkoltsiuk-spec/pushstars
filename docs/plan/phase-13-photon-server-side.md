# Фаза 13 — Photon и серверный матчмейкинг

## Цель фазы

Настроить **Photon PUN2** (AppId, регион). Реализовать **Cloud Function `matchPlayers`** (или аналог): периодический или триггерный разбор очереди RTDB `/matchmaking/queue/{uid}`, создание комнаты через **Photon REST API**, запись **`matches/{matchId}`** с room name и Uid игроков. Реализовать **`onMatchFinished`** HTTP webhook с проверкой `X-Photon-Token`, античитом по репам, обновлением профилей и созданием документа матча. **`cleanupMatchmaking`** по расписанию.

## Что НЕ делаем в этой фазе

- Полный клиентский UX поиска (фаза 14).
- Ghost callable (фаза 12).

## Предусловия

Фазы 01–05.

## Затрагиваемые системы

Photon Dashboard Webhooks → RoomClosed URL на `onMatchFinished`.

## Файлы

- `functions/src/matchPlayers.js` — защита от двойного матча, транзакции RTDB при необходимости
- `functions/src/onMatchFinished.js`
- `functions/src/cleanupMatchmaking.js`
- Документировать env: `PHOTON_APP_ID`, `PHOTON_REST_SECRET`, `PHOTON_WEBHOOK_SECRET`

## Acceptance criteria

- [ ] Два staging клиента попадают в одну комнату по CF-сценарию (можно тестовым скриптом).
- [ ] Закрытие комнаты вызывает webhook и создаёт документ матча.
- [ ] Неверный токен webhook → 401.

## Тестирование

Photon sandbox + логи Functions + искусственное закрытие комнаты.

## Связь с дизайном

Нет (инфраструктура).
