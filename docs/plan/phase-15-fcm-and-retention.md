# Фаза 15 — FCM, APNs и retention-события

## Цель фазы

Завершить push-инфраструктуру: **APNs key** в Firebase, обновление **`fcmToken`** в `users/{uid}` только при изменении. Шаблоны payload из архитектуры: результат матча, стрик под угрозой, конец сезона за 24 ч, вызов друга (после фазы 16). Scheduled Functions: напоминание стрика, напоминание о сезоне. Локальные уведомления как fallback когда FCM недоступен.

## Что НЕ делаем в этой фазе

- Email-маркетинг.
- Rich push с картинками (опционально позже).

## Предусловия

Фазы 04–05–14 (для событий результата матча из CF).

## Файлы

- `functions/src/notifications/sendMatchResultPush.js`
- `functions/src/notifications/streakReminder.js`
- `functions/src/notifications/seasonEndingSoon.js`
- Клиент: обработка `data.type` в foreground → роутинг на экран.

## Acceptance criteria

- [ ] iOS и Android получают тестовое сообщение из консоли Firebase.
- [ ] Нет лишних записей Firestore на каждый старт приложения.

## Тестирование

Физические устройства; отзыв разрешений на уведомления.

## Связь с дизайном

Нет.
