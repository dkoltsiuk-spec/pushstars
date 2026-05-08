# Фаза 02 — Каркас Unity

## Цель фазы

Создать новый Unity-проект (2022 LTS), URP, базовые пакеты, структуру папок, загрузочную сцену и главную оболочку с **тремя вкладками**: Лига, Дуэль (VS), Профиль — с placeholder-контентом.

## Что НЕ делаем в этой фазе

- Нет MediaPipe, Photon gameplay, Firebase вызовов.
- Нет финальной графики из макетов (только серые боксы допустимы).

## Предусловия

Фаза 01 (желательно для bundle id).

## Затрагиваемые экраны

Экран 1 (структура табов), навигация к заглушкам экранов 6–7.

## Файлы / модули

- `Assets/_Project/Scenes/Boot.unity` — инициализация DI/сервис-локатор при необходимости.
- `Assets/_Project/Scenes/Main.unity` — shell + tab bar.
- `Assets/_Project/Scripts/App/AppBootstrap.cs`
- `Assets/_Project/Scripts/UI/MainNavigation/MainShellView.cs`
- Assembly Definition `PushStars.Core`

Пакеты через Package Manager:

- TextMeshPro
- **UniTask**
- **DOTween** (опционально локально)

## Acceptance criteria

- [ ] Проект открывается на Windows/Mac.
- [ ] Сборка на Android и iOS без ошибок (пустой билд или smoke scene).
- [ ] Переключение трёх табов без утечек сцен (additive или canvas swap).

## Тестирование

Ручной прогон на одном Android и одном iOS устройстве (или симулятор).

## Связь с дизайном

Нижняя pill-nav как на экране 1 и 7 — место под префабы фазы 03.
