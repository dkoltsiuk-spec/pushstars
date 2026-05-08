# Фаза 12 — Ghost Mode (против своего лучшего рекорда)

## Цель фазы

Реализовать запись **best** сессии (скелет сжатый, см. архитектуру), загрузку в Storage, метаданные в Firestore `ghost_sessions/{uid}/sessions/pushups`. Игровой режим **Ghost**: локальный таймер 60 с, воспроизведение записи на втором аватаре, подсчёт живых репов, финальный экран сравнения. Вызов **`onGhostMatchFinished`**: трофеи ±ghost, XP, streak, запись `matches` с `mode: "ghost"` по [ghost-mode-spec.md](../architecture/ghost-mode-spec.md).

## Что НЕ делаем в этой фазе

- Поиск чужих ghost-записей.
- Photon для Ghost.

## Предусловия

Фазы 08–09–10–05.

## Затрагиваемые экраны

Экран поиска соперника — fallback CTA; главный экран — вход в Ghost; HUD как у дуэли.

## Файлы

- `Assets/_Project/Scripts/Ghost/GhostRecorder.cs`, `GhostPlayback.cs`
- `functions/src/onGhostMatchFinished.js` — полная логика + идемпотентность
- Обновление Storage rules / upload path `ghost/{uid}/...`

## Acceptance criteria

- [ ] Нет best-файла → Ghost недоступен с понятным сообщением.
- [ ] Размер файла ≤ `GHOST_MAX_FILE_BYTES`.
- [ ] Победа/поражение определяется сравнением с `bestRepsAtRecord`.
- [ ] Трофеи на staging соответствуют constants.

## Тестирование

Игра против искусственно заниженной записи и завышенной.

## Связь с дизайном

Экран 4 (HUD), экран 5–6 (итог и награды) — те же шаблоны что PvP.
