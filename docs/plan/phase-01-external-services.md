# Фаза 01 — Внешние сервисы и аккаунты

## Цель фазы

Создать и настроить все облачные и стор-аккаунты так, чтобы в фазе 04 можно было импортировать конфиги в Unity без блокеров.

## Что НЕ делаем в этой фазе

- Не интегрируем SDK в Unity.
- Не деплоим код Functions (фаза 05).

## Предусловия

Фаза 00.

## Затрагиваемые системы

Firebase, Photon, Apple Developer, Google Play Console, Branch/AppsFlyer (зарезервировать аккаунт).

## Чеклист (артефакт)

Шаблон уже в репозитории: **[credentials-checklist.md](../architecture/credentials-checklist.md)** — скопировать локально или заполнять в wiki и **не коммитить секреты**.

Заполнить пункты:

- [ ] Firebase проект (Blaze), приложения iOS + Android, bundle id `com.pushstars.app` (или финальный).
- [ ] Скачать `GoogleService-Info.plist`, `google-services.json` (хранить безопасно).
- [ ] Включить Auth (Anonymous + провайдеры позже), Firestore, RTDB, Storage, Functions, FCM, Crashlytics, Analytics.
- [ ] Photon приложение PUN2, AppId, регион по умолчанию.
- [ ] Сгенерировать секрет для Photon webhook → сохранить для Firebase env `PHOTON_WEBHOOK_SECRET`.
- [ ] Apple: Team ID, push capability, APNs key для FCM.
- [ ] Google Play: консольный доступ, SHA-1/256 для debug/release при необходимости.

## Acceptance criteria

- Все ID и пути к plist/json задокументированы внутри команды (не обязательно в git).
- Firebase billing alerts настроены (напр. $10/мес).

## Тестирование

- Firebase Console: тестовое создание пользователя Auth вручную.
- Photon Dashboard: видимость приложения Online.

## Связь с дизайном

Нет.
