# Фаза 01 — внешние сервисы (чеклист)

Заполняется вручную. Секреты и ключи — только локально, менеджер паролей или CI secrets; **в git не коммитить** значения и файлы из блока «Не коммитить».

Связанный план: [phase-01-external-services.md](../plan/phase-01-external-services.md).

---

## Не коммитить в репозиторий

| Артефакт | Где хранить |
|----------|-------------|
| `GoogleService-Info.plist` | Локально в Unity `Assets/...` или secrets CI |
| `google-services.json` | Аналогично |
| `.env` / `functions/.env` | Только локально / CI |
| APNs `.p8`, пароли keystore | Только локально / CI |
| Photon webhook secret | `firebase functions:config:set` или Secret Manager |

Шаблон переменных без значений: [`functions/.env.example`](../../functions/.env.example).

---

## Firebase — по шагам

1. [Firebase Console](https://console.firebase.google.com/) → «Добавить проект» → имя (например `push-stars`).
2. Включить **Google Analytics** (удобно для фазы 17) или отключить осознанно.
3. **План Blaze** (обязателен для Cloud Functions + некоторых квот): «Upgrade» → привязать биллинг → **Budget alert** (например $10/мес) в Google Cloud Console → Billing → Budgets.
4. Добавить приложение **iOS**  
   - Bundle ID (MVP): `com.pushstars.app` (или финальный; один раз зафиксировать везде).  
   - Скачать `GoogleService-Info.plist` → положить в безопасное место (позже в Unity, фаза 04).
5. Добавить приложение **Android**  
   - Package name = тот же идентификатор, что в Unity Player Settings.  
   - Скачать `google-services.json`.
6. Включить продукты (меню сборки слева):

   - [ ] **Authentication** — провайдер **Anonymous** (остальные позже).
   - [ ] **Firestore** — создать БД (режим production → правила позже, фаза 05).
   - [ ] **Realtime Database** — создать экземпляр (правила позже).
   - [ ] **Storage** — включить (правила позже).
   - [ ] **Functions** — включить (деплой в фазе 05).
   - [ ] **Cloud Messaging** — для FCM (фаза 15).
   - [ ] **Crashlytics** — подключить приложения.
   - [ ] **Analytics** — если включён при создании проекта.

7. **Индекс Firestore** (создать заранее или по ссылке из ошибки клиента): коллекция `matches`, составной индекс `playerUids` **Array** + `createdAt` **Descending** (см. [firebase-architecture-v2.md](firebase-architecture-v2.md)).

8. Регион Cloud Functions выбрать ближе к аудитории (US/Europe) и не менять без миграции.

### Firebase — короткий чеклист полей

- [ ] Project ID: _______________
- [ ] Номер проекта Google Cloud: _______________
- [ ] Blaze включён, budget alert настроен
- [ ] iOS bundle ID: _______________
- [ ] Android package name: _______________
- [ ] Платформы Firebase из списка выше включены
- [ ] Индекс `matches` создан

---

## Photon PUN2

1. [Photon Dashboard](https://dashboard.photonengine.com/) → Create Application → тип **Photon PUN**.
2. Записать **App ID**, выбрать **Fixed Region** или Auto по стратегии.
3. Для фазы 13: API для создания комнат через REST — уточнить в документации Photon (Application Secret / Token по типу плана).
4. **Webhooks** (после первого деплоя `onMatchFinished`, фаза 13):  
   - Тип: HTTP webhook на событие закрытия комнаты (Room Closed / Empty Room Livecycle — актуальный тип уточнить в документации Photon 2).  
   - URL вида: `https://<REGION>-<PROJECT_ID>.cloudfunctions.net/onMatchFinished` (точный URL взять из Firebase после деплоя).  
   - Сгенерировать случайный длинный секрет → передавать в заголовке (например `X-Photon-Token`) → тот же секрет в **`PHOTON_WEBHOOK_SECRET`** для Functions.

### Photon — чеклист

- [ ] PUN2 App ID: _______________
- [ ] Регион по умолчанию: _______________
- [ ] REST / Server credentials записаны (не в git): _______________
- [ ] Webhook URL заполнен после деплоя Functions
- [ ] `PHOTON_WEBHOOK_SECRET` сохранён и будет задан в Firebase config / Secret Manager

---

## Apple Developer

1. [Apple Developer](https://developer.apple.com/) — программа платная, аккаунт команды.
2. Зарегистрировать App ID с Bundle ID `com.pushstars.app`, включить **Push Notifications**.
3. Certificates, Identifiers & Profiles → **Keys** → создать ключ **Apple Push Notifications service (APNs)** → скачать `.p8` → записать **Key ID** и **Team ID**.
4. Firebase Console → Project Settings → Cloud Messaging → **Apple app configuration** → загрузить APNs key (.p8).

### Apple — чеклист

- [ ] Team ID: _______________
- [ ] APNs Key ID: _______________
- [ ] Push capability у App ID
- [ ] APNs ключ загружен в Firebase

---

## Google Play Console

1. Создать приложение с тем же package name, что в Firebase/Android Unity.
2. Для OAuth/Firebase иногда нужны **SHA-1 / SHA-256** сертификатов: debug keystore и release keystore → добавить в Firebase → настройки Android-приложения.

### Google Play — чеклист

- [ ] Приложение создано в консоли
- [ ] SHA-1/256 debug добавлены в Firebase (если требуется): да / нет
- [ ] SHA-1/256 release: после первого release-keystore

---

## Branch или AppsFlyer (фаза 16, зарезервировать сейчас)

- [ ] Выбран провайдер: Branch / AppsFlyer / другое: _______________
- [ ] Аккаунт создан, приложения iOS/Android заведены черновиком
- [ ] Live / Test keys записаны локально (не в git)

---

## Приёмочные критерии фазы 01

- [ ] Все ID из блоков выше заполнены **вне git** (или в wiki с доступом только команде).
- [ ] Firebase billing alert активен.
- [ ] Ручная проверка: Firebase Auth — тестовый анонимный пользователь создаётся (консоль или короткий скрипт).
- [ ] Photon Dashboard показывает приложение Online.

После этого можно переходить к **фазе 02** (Unity) и **фазе 04** (Firebase SDK), когда plist/json лежат в проекте локально.
