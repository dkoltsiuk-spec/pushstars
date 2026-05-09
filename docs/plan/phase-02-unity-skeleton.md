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

## Статус реализации

Выполнено агентом (phase-02):
- [x] `ProjectSettings/` — полный набор `.asset`-файлов для Unity 2022.3.61f1
- [x] `Packages/manifest.json` — URP 14, TextMeshPro 3, UniTask 2.3.3 (OpenUPM)
- [x] `Assets/_Project/Scenes/Boot.unity` — сцена с AppBootstrap
- [x] `Assets/_Project/Scenes/Main.unity` — shell с LeaguePanel / DuelPanel / ProfilePanel
- [x] `Assets/_Project/Scripts/App/AppBootstrap.cs` + `PushStars.App.asmdef`
- [x] `Assets/_Project/Scripts/Core/` — `IInitializable`, `IService`, `ServiceLocator` + `PushStars.Core.asmdef`
- [x] `Assets/_Project/Scripts/UI/MainNavigation/` — `TabId`, `TabButton`, `MainShellView` + `PushStars.UI.asmdef`
- [x] `Assets/_Project/Scripts/UI/League/LeagueView.cs`
- [x] `Assets/_Project/Scripts/UI/Duel/DuelView.cs`
- [x] `Assets/_Project/Scripts/UI/Profile/ProfileView.cs`
- [x] Все `.meta`-файлы для перечисленных ассетов
- [x] `.gitignore` обновлён

Требует ручной настройки в Unity Editor:
- Назначить сцены Boot и Main в Build Settings
- Привязать `LeaguePanel`, `DuelPanel`, `ProfilePanel` в Inspector `MainShellView`
- Добавить три `TabButton` в нижнюю навигацию и заполнить `_tabButtons[]`
- Настроить URP Asset через Edit → Project Settings → Graphics

## Тестирование

Ручной прогон на одном Android и одном iOS устройстве (или симулятор).

## Связь с дизайном

Нижняя pill-nav как на экране 1 и 7 — место под префабы фазы 03.
