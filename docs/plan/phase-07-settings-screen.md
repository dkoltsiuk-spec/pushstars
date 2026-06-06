# Фаза 07 — Настройки

## Цель фазы

Экран настроек по требованиям стора: звук, вибрация, язык (RU/EN), уведомления (флаг + переход в системные настройки где нужно), разрешение камеры / объяснение зачем, удаление аккаунта, Privacy Policy и Terms (URL), версия приложения.

## Что НЕ делаем в этой фазе

- Полная локализация всех строк приложения (фаза 17).
- Серверная логика удаления поверх `onUserDeleted` не дублировать.

## Предусловия

Фазы 03, 06 (точка входа — иконка на профиле).

## Затрагиваемые экраны

Профиль → Настройки (новый экран).

## Файлы

- `Assets/_Project/Scripts/Core/ISettingsStore.cs` — интерфейс настроек (звук/вибрация/уведомления/язык).
- `Assets/_Project/Scripts/Core/PlayerPrefsSettingsStore.cs` — реализация поверх `PlayerPrefs`.
- `Assets/_Project/Scripts/UI/Settings/SettingsScreen.cs` — контроллер оверлея (тумблеры, язык, ссылки, версия, удаление).
- `Assets/_Project/Scripts/Firebase/FirebaseAuthService.cs` — добавлен `DeleteAccountAsync()` (триггерит `onUserDeleted` CF).
- `Assets/_Project/Scripts/UI/Theme/PushStarsTheme.cs` — слот `IconSettings` (gear).
- `Assets/_Project/Editor/UIGallerySetup.cs` — привязка `gear.png` → `IconSettings`.
- `Assets/_Project/Editor/MainVsScreenSetup.cs` — шестерёнка в хедере профиля + `BuildSettingsOverlay` + проводка `SettingsScreen`.

Точка входа: иконка-шестерёнка в правом верхнем углу экрана профиля → full-screen оверлей в Main-сцене
(тот же паттерн, что `SearchOpponentController`). Кнопка «Удалить аккаунт» → подтверждение → Firebase Auth
`DeleteAccountAsync()` → перезапуск с Boot (там новый анонимный вход).

> Художнику: положить `gear.png` в папку спрайтов (`Assets/_Project/Art/Sprites/`, где остальные иконки —
> поиск по имени найдёт её в любой подпапке), затем Tools → Push Stars → Build Main VS Screen.
> Без него шестерёнка рисуется глифом-заглушкой.

## Acceptance criteria

- [x] Все тумблеры сохраняются между сессиями. *(PlayerPrefs, запись по каждому изменению)*
- [~] Удаление аккаунта вызывает CF очистку. *(клиент вызывает `DeleteAsync`; ручная проверка на staging — при следующем прогоне)*
- [x] Ссылки на Privacy/Terms открываются. *(`Application.OpenURL` → системный браузер; in-app WebView отложен. URL — плейсхолдеры в `SettingsScreen`, заменить перед стором)*

## Тестирование

Сценарии на iOS и Android (разные диалоги разрешений).

## Связь с дизайном

Иконка шестерёнки на экране профиля из ТЗ пользователя.
