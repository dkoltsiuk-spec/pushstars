# Фаза 17 — Полировка, локализация, стор

## Цель фазы

Подготовка к релизу: полная **локализация RU/EN** строк, иконка и splash, список **Analytics** событий (воронка первой дуэли, retention D1/D3), краш-тест Crashlytics, списки Privacy/Terms, возрастной рейтинг, **скриншоты стора** на базе 7 дизайн-мокапов, опционально Fastlane/TestFlight/Internal testing.

## Что НЕ делаем в этой фазе

- Новый игровой функционал.
- Battle Pass.

## Предусловия

Фазы 14–15–16 (минимально 14).

## Файлы

- `Assets/_Project/Locale/` — таблицы или Unity Localization Package
- `docs/store/store-listing-en.md`, `store-listing-ru.md`
- CI скрипты при необходимости

## Acceptance criteria

- [ ] Нет блокирующих крешей на smoke-сценарии.
- [ ] Все обязательные разрешения объяснены в UI и в App Store Connect notes.
- [ ] Скриншоты и промо-тексты готовы.

## Тестирование

TestFlight / Internal App Sharing полный цикл установки.

## Связь с дизайном

Все 7 PNG как основа маркетинговых кадров.
