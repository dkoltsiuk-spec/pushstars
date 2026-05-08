# Фаза 05 — Backend core (Cloud Functions + правила)

## Цель фазы

Поднять проект `functions/` (Node.js), задеплоить базовые триггеры и callable, настроить **Security Rules** Firestore / RTDB / Storage согласно [firebase-architecture-v2.md](../architecture/firebase-architecture-v2.md). Реализовать **идемпотентный** `syncOfflineXp` и `updateDisplayName` (рекомендуется CF вместо записи в leaderboard с клиента).

## Что НЕ делаем в этой фазе

- **`matchPlayers`**, **`onMatchFinished` webhook** — фаза 13.
- **`onGhostMatchFinished`** — фаза 12 (можно заглушку с `HttpsError('unimplemented')` если нужна сборка).

## Предусловия

Фаза 04.

## Затрагиваемые системы

Firestore schema users/matches placeholder, RTDB rules skeleton, Storage rules для `ghost/`.

## Файлы

- `functions/package.json`, `functions/src/index.js`
- `functions/src/constants.js` — импорт значений из таблицы [constants.md](../architecture/constants.md)
- `functions/src/onUserCreated.js`, `onUserDeleted.js`
- `functions/src/syncOfflineXp.js`
- `functions/src/updateDisplayName.js` — атомарно users + leaderboard doc если решено писать leaderboard из CF только при матче — уточнить; минимум: только users + очередь на пересчёт.
- `firestore.rules`, `database.rules.json`, `storage.rules`

Индекс Firestore: `matches`: `playerUids` ARRAY + `createdAt` DESC.

## Acceptance criteria

- [ ] Новый пользователь получает документ `users/{uid}` с дефолтами (rank bronze, 0 trophies, …).
- [ ] Удаление Auth удаляет пользовательские данные (`onUserDeleted`).
- [ ] `syncOfflineXp` корректно отклоняет повтор с тем же токеном.
- [ ] Правила: клиент не может писать в защищённые поля профиля.

## Тестирование

Firebase Emulator Suite или staging проект; unit-тесты на callable при возможности.

## Связь с дизайном

Данные для экранов 6–7 готовы на уровне схемы.
