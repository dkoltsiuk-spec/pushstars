# Фаза 08 — MediaPipe: трекинг и счётчик повторений

## Цель фазы

Интегрировать MediaPipe Pose (33 точки), работу с фронтальной камерой, **FSM** для фаз отжимания (eccentric / bottom pause / concentric), подсчёт **репов**, оценку **FORM** (углы локтя, линия тела, глубина), **TEMPO**. Добавить **экран калибровки**: расстояние, свет, ракурс; индикатор качества трекинга (ОК / потеря скелета); короткие текстовые и опционально голосовые подсказки.

## Что НЕ делаем в этой фазе

- Не подключаем Photon сеть.
- Не финализируем Genies риг (фаза 10).

## Предусловия

Фазы 02–03.

## Затрагиваемые экраны

HUD дуэли (метрики), калибровка перед боем (экран без отдельного PNG).

## Бэкенд

MediaPipe (Homuler `MediaPipeUnityPlugin`) — как в плане. Плагин тяжёлый (нативные либы под платформу),
ставится вручную в Unity. Поэтому CV-ядро написано против абстракции `IPoseSource`, а Homuler-адаптер
закрыт дефайном `PUSHSTARS_MEDIAPIPE` — проект собирается и тестируется (на `MockPoseSource`) до установки.

## Файлы

CV-ядро (сборка `Assets/_Project/Scripts/CV/PushStars.CV.asmdef`):
- `PoseFrame.cs` — `PoseLandmark` (33 BlazePose), `Landmark`, `PoseFrame`.
- `IPoseSource.cs` — абстракция бэкенда + `TrackingQuality`.
- `CVConstants.cs` — пороги углов/видимости/формы.
- `PoseMath.cs` — углы (локоть, линия тела). `PoseQuality.cs` — классификатор качества.
- `PushupRepCounter.cs` — FSM фаз + счёт репов (гистерезис, потолок 65) + TEMPO.
- `FormScoreCalculator.cs` — `FormReading` (глубина + линия тела → FORM 0..100).
- `MockPoseSource.cs` — синтетический источник для тестов без камеры/плагина.
- `MediaPipePoseSource.cs` — Homuler-адаптер (под `#if PUSHSTARS_MEDIAPIPE`).
- `PushupSession.cs` — рантайм-связка источник→счётчик→FORM (к ней цепляется HUD/тренировка).
- `Assets/_Project/Scripts/UI/Calibration/CalibrationScreen.cs` — статус трекинга + подсказки + кнопка «Начать».

> Для реального трекинга: импортировать плагин → добавить дефайн `PUSHSTARS_MEDIAPIPE` → ссылку на
> сборку плагина в `PushStars.CV.asmdef` → модель Pose Landmarker в StreamingAssets (см. шапку `MediaPipePoseSource`).

## Acceptance criteria

- [x] При потере скелета UI явно показывает причину и что делать. *(CalibrationScreen: ОК / слабый / не найден + подсказка; счёт репов заблокирован на `Lost`)*
- [~] Репы совпадают с ручным счётом ±допуск. *(логика FSM готова и проверяема на `MockPoseSource`; сверка на реальном видео — после установки плагина)*
- [ ] Стабильный FPS на целевых устройствах. *(меряется только с реальным MediaPipe на устройстве)*

## Как проверить сейчас (без плагина)

Создать GameObject со `MockPoseSource` + `PushupSession` (включить `_logReps`), задать в `PushupSession`
ссылку на мок → Play: в консоль пойдут репы с фазой/формой/темпом. Сменив `_tempoRpm`/`simulateLost`,
проверить темп и блокировку счёта при потере скелета.

## Тестирование

Набор коротких записей с камеры + регрессионный чеклист поз.

## Связь с дизайном

Экран 4 — числа репов, FORM, TEMPO.
