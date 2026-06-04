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

- [x] Новый пользователь получает документ `users/{uid}` с дефолтами (rank bronze, 0 trophies, …). ✅ лог `Seeded profile`.
- [x] Удаление Auth удаляет пользовательские данные (`onUserDeleted`). ✅ функция ACTIVE, delete триггерит.
- [x] `syncOfflineXp` корректно отклоняет повтор с тем же токеном. ✅ `ok` → `already_synced`.
- [x] Правила: клиент не может писать в защищённые поля профиля. ✅ `trophies` → 403, `displayName` → 200.

> **Решения/состояние фазы 05 (задеплоено в `push-stars-d620e`):** Cloud Functions v1 (Gen-1, нужно для
> auth-триггеров), Node 22, JS. Функции: `onUserCreated`/`onUserDeleted` (us-east1), `syncOfflineXp`/
> `updateDisplayName` (us-central1, callable). Строгие `firestore.rules` + индекс `matches` задеплоены —
> заменили временное dev-правило фазы 04. `database.rules.json` / `storage.rules` написаны, но НЕ подключены
> в `firebase.json` (RTDB/Storage не включены — фазы 12/13). Деплой: `firebase deploy --only firestore,functions`.
> Блокер первого деплоя: дефолтный Compute SA не существовал → включили **Compute Engine API**.
> Конфиги функций секретов не содержат; `PHOTON_WEBHOOK_SECRET` появится в фазе 13.

## Тестирование

Firebase Emulator Suite или staging проект; unit-тесты на callable при возможности.

## Связь с дизайном

Данные для экранов 6–7 готовы на уровне схемы.
