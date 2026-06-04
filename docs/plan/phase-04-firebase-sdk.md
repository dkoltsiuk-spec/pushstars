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

- [x] При старте приложение входит анонимно (или в связке с будущим Apple/Google). ✅ `uid` в логах, юзер виден в Console.
- [x] Firestore `PersistenceEnabled = true`, лимит кеша по архитектуре. ✅ persistence включён; self-test (запись+чтение `_diagnostics/ping`) проходит.
- [ ] FCM запрос разрешений на iOS в нужном месте жизненного цикла. ⏭️ отложено — минимальный набор SDK; FCM в фазе 15.
- [ ] Crashlytics инициализирован; тестовый crash за флагом dev. ⏭️ пакет импортирован, init/тестовый crash добавим позже.

> **Решения фазы 04:** минимальный набор SDK (Auth + Firestore + Crashlytics), bundle id `com.pushstars.app`,
> dev-правило Firestore `allow read, write: if request.auth != null` (настоящие правила — фаза 05),
> конфиги (`GoogleService-Info.plist` / `google-services.json` / `*-desktop.json`) — в `.gitignore`, не коммитятся.
> Реализация: `Assets/_Project/Scripts/Firebase/` (`FirebaseService`, `FirebaseAuthService`, `FirestoreService`),
> вызов из `AppBootstrap.InitServicesAsync()`.

## Тестирование

Логи: uid пользователя, успешный `GetSnapshotAsync` на тестовый документ (создать в консоли).

## Связь с дизайном

Нет.
