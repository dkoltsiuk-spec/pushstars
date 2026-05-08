# Фаза 04 — Firebase SDK в клиенте

## Цель фазы

Подключить Firebase Unity SDK: Auth, Firestore, RTDB, Storage, Functions, Messaging, Crashlytics, Analytics. Реализовать минимальный **FirebaseService** с инициализацией, анонимным входом и включением Firestore persistence.

## Что НЕ делаем в этой фазе

- Не деплоим Cloud Functions (фаза 05).
- Не реализуем матчмейкинг и webhook.

## Предусловия

Фазы 01–02.

## Затрагиваемые системы

Глобальная инициализация приложения.

## Файлы

- `Assets/_Project/Plugins/Firebase/` — импорт `.unitypackage` или UPM.
- `Assets/_Project/Scripts/Firebase/FirebaseBootstrap.cs`
- `Assets/_Project/Scripts/Firebase/FirebaseAuthService.cs`
- `Assets/_Project/Scripts/Firebase/FirestoreService.cs` — обёртка с логированием ошибок.

## Acceptance criteria

- [ ] При старте приложение входит анонимно (или в связке с будущим Apple/Google).
- [ ] Firestore `PersistenceEnabled = true`, лимит кеша по архитектуре.
- [ ] FCM запрос разрешений на iOS в нужном месте жизненного цикла.
- [ ] Crashlytics инициализирован; тестовый crash за флагом dev.

## Тестирование

Логи: uid пользователя, успешный `GetSnapshotAsync` на тестовый документ (создать в консоли).

## Связь с дизайном

Нет.
