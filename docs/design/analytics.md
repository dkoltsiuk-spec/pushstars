# Аналитика удержания (PostHog)

Шаг 5 плана ежедневного ритма (`docs/plan/phase-11.5-daily-challenge.md`). Цель одна: видеть,
возвращаются ли игроки на следующий день / через неделю и где теряем их в первом запуске.

## Как устроено

- Отдельный проект PostHog **«PushUp Stars»** (id 310522, регион EU, IP анонимизируются). Проект
  «Default project» (159310) — другого приложения, туда игру не пишем.
- Дашборд: https://eu.posthog.com/project/310522/dashboard/1033585 — возвраты D1–D7 и по неделям,
  воронка первого запуска, возврат на следующий день после первого боя. Все плитки отсекают `is_editor`.
- Клиент без SDK: `Core/Analytics/Analytics.cs` (события) + `PostHogSender.cs` (POST на
  `https://eu.i.posthog.com/batch/`, очередь в `persistentDataPath/analytics_queue.jsonl`,
  отправка через ~3 с после события, на старте и при уходе в фон; ошибки — повтор с паузой до 10 мин).
- `distinct_id` — случайный id установки (`PlayerPrefs analytics.install_id`), не аккаунт Firebase.
- Отказ игрока: `Analytics.Enabled = false` (PlayerPrefs `settings.analytics`) — сразу чистит очередь.
  **Переключателя в Настройках пока нет** — добавить строку «Анонимная статистика» в Settings.
- Ключ: `Assets/_Project/Resources/PostHogConfig.json` (gitignored):
  ```json
  { "apiKey": "phc_…", "host": "https://eu.i.posthog.com", "sendFromEditor": false }
  ```
  Нет файла — ничего не отправляется. **CI (Codemagic): записать файл из секрета перед сборкой**,
  иначе релиз уйдёт без аналитики.
- Editor: отправляет только при `sendFromEditor: true` (несколько сессий гоняют Play Mode в одном
  Editor — иначе это «один очень верный игрок»). Все события несут `is_editor`, `dev_build`;
  плитки дашборда фильтруют `is_editor` сами (фильтр тестовых аккаунтов проекта не трогали — у ключа MCP нет права project:write).

## События

| Событие | Когда | Свойства |
|---|---|---|
| `app_open` | холодный старт (AppBootstrap); возврат из фона через ≥30 мин | `cold`, `route` (onboarding/assessment/main), `away_min` |
| `onboarding_step` | `intro_started`, `intro_done`, `assessment_skipped`, `first_fight` | `step` |
| `assessment_done` | тест 60 с дал результат (OnboardingState.CompleteLevelTest) | `reps`, `tier`, `first` |
| `first_fight_done` | первый бой с гостом или боссом, раз на установку | `mode`, `won`, `reps` |
| `fight_finished` | каждый подход (FightController.PresentResults) | `mode`, `reps`, `ranked`; для боёв `won`, `draw`, `opp_reps` |
| `daily_goal_done` | цель дня выполнена, раз в местный день | `target`, `streak_days` |
| `streak_freeze_used` | заготовка — вызвать, когда появится заморозка | `streak_days`, `freezes_left` |
| `notif_permission` | заготовка — ответ на системный запрос уведомлений | `granted` |
| `push_opened` | заготовка — открытие из напоминания | `kind` |

Общие свойства: `$app_version`, `$os`, `platform`, `is_editor`, `dev_build`, `$lib`.

## Что теперь должна сказать политика конфиденциальности

**Не выкладывать legal_site без «да» владельца.** Правки в `legal_site/privacy/index.html`:

1. Раздел 2, новый абзац после «Diagnostics»:
   > **Usage statistics.** The App sends anonymous usage events — for example that the App was
   > opened, that the assessment or a battle was finished, the number of repetitions and whether
   > you won — together with a random identifier created on your device, the app version and the
   > operating system. These events are not linked to your name, email or account, contain no
   > camera data, and we do not store your IP address with them. We use them only to understand
   > whether players come back and where the first launch is confusing. *(После переключателя:
   > You can turn this off in Settings → Anonymous statistics.)*
2. Раздел 3: пункт «To understand how the App is used so we can improve it (anonymous usage statistics).»
3. Раздел 4: «We use PostHog (PostHog Inc.) to process anonymous usage statistics on servers in the
   European Union.»
4. Раздел 6: конкретный срок хранения статистики, например «Usage statistics are kept for up to
   2 years and then deleted» — срок выбирает владелец, он должен совпадать с настройкой хранения
   в PostHog (зависит от тарифа).
5. Раздел 7 (основание): «…and on the basis of our legitimate interest in keeping the App secure,
   working and improving it» — для ЕС этого достаточно при анонимном id и возможности отказаться;
   переключатель в Настройках закрывает вопрос «right to object».

## Google Play — Data safety

Обновить анкету (Play Console → App content → Data safety):

- **Data collected → App activity → «App interactions»**: Collected = Yes; Shared = No;
  Processed ephemerally = No; Required (если нет переключателя) / Optional (когда появится);
  Purpose = **Analytics**.
- **App info and performance → «Diagnostics»** уже должно стоять из-за Crashlytics — без изменений.
- **Device or other IDs**: случайный id установки по определению Google — «Device or other IDs»
  (идентификатор, созданный приложением). Отметить Collected = Yes, Purpose = Analytics, Shared = No.
- Data is encrypted in transit = Yes (HTTPS). Users can request deletion = Yes (через почту поддержки;
  в PostHog удаление персоны по `distinct_id`).
- App Store (App Privacy), когда дойдёт до iOS: «Usage Data → Product Interaction», «Identifiers →
  Device ID», Not linked to user, Not used for tracking.
