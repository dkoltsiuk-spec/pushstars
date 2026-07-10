# Push Stars — игровые и технические константы

Единый источник правды для клиента (Unity) и Cloud Functions (Node). Дублировать значения только через генерацию или ручную синхронизацию — комментарии в коде должны ссылаться на этот файл.

## Матч и античит

| Константа | Значение | Примечание |
|-----------|----------|------------|
| `MATCH_DURATION_SEC` | `60` | Длительность дуэли MVP |
| `MAX_REPS_PER_MATCH` | `65` | Жёсткий потолок репов за матч (античит) |
| `MATCHMAKING_TIMEOUT_SEC` | `35` | После этого `cleanupMatchmaking` удаляет запись из очереди RTDB |
| `MATCHMAKING_UI_TIMEOUT_SEC` | `30` | UX: предложить Ghost / тренировку, если UI «ждёт» дольше |

## CV-античит фазы 08.1 (per-frame / per-rep)

Полная спецификация и обоснование — [phase-08.1-pushup-anticheat.md](../plan/phase-08.1-pushup-anticheat.md). Все живут в `Assets/_Project/Scripts/CV/CVConstants.cs`.

### Plank arming (`PlankArmer`)

| Константа | Значение | Примечание |
|-----------|----------|------------|
| `PlankArmHoldSec` | `1.0f` | Сколько секунд держать валидную планку до старта счёта (зафиксировано пользователем) |
| `PlankDisarmGraceSec` | `2.5f` | Окно `Armed → Cooling → Disarmed`; покрывает глюки скелета на дне репа |
| `ArmingBodyLineAngle` | `160f` | Угол shoulder-hip-ankle для arming (строже legacy `MinPlankBodyLine=140`) |
| `ArmingElbowTopAngle` | `150f` | Локоть выпрямлен — arming с верхней позиции |
| `PlankLowerBodyVisibility` | `0.7f` | Порог видимости для нижней части тела (хотя бы одна точка из ankle/foot/knee) |

### Knee-bend detector (`KneeBendDetector`)

| Константа | Значение | Примечание |
|-----------|----------|------------|
| `KneeBentMaxAngle` | `145f` | Hip-knee-ankle ≤ этого → raw Bent (запас под перспективу) |
| `KneeStraightMinAngle` | `160f` | ≥ этого → raw Straight; разрыв = hysteresis dead zone |
| `KneeClassificationRibbonFrames` | `5` | Подряд кадров до смены сглаженной классификации |

### Wrist-anchor monitor (`WristAnchorMonitor`)

| Константа | Значение | Примечание |
|-----------|----------|------------|
| `WristAnchorWindowFrames` | `12` | Sliding window ≈ 400ms @ 30fps |
| `WristAnchorSoftFrac` | `0.10f` | Drift < этого (от длины торса) → Anchored |
| `WristAnchorHardFrac` | `0.20f` | Drift ≥ этого → Airborne (hard veto); между — Drifting (soft dock в Stage 2) |
| `WristAnchorGraceFramesAfterRep` | `45` | Grace после credited rep ≈ 1.5с — юзер может поправить руки |
| `WristAnchorGraceFramesAfterArm` | `30` | Grace после `OnArmed` ≈ 1с — даём встать в позу |

### Frontal addendum (фронтальная камера + точность верх/низ)

Полная спецификация — [phase-08.1-frontal-addendum.md](../plan/phase-08.1-frontal-addendum.md).
Неизменные анти-чит полы: `TopElbowAngle = 160`, `BottomElbowAngle = 95` — единственный источник латчей в этом релизе.
**Изменено:** `MinRepSeconds` 0.45 → **0.30** (0.45 молча резал честных 1.5 повт/с).

| Константа | Значение | Смысл |
|-----------|----------|-------|
| `ElbowFilterMinCutoffHz` | `2.5f` | One-Euro срез в покое (поднят с 1.5 — быстрый темп) |
| `ElbowFilterBeta` / `DerivCutoffHz` | `0.05f` / `1.0f` | One-Euro параметры |
| `ElbowSpikeClampDegPerFrame` | `40f` | Hampel-кламп выброса |
| `TrackerRebaseAfterLostSec` | `0.5f` | пере-сид фильтра + дуга в Idle |
| `ZoneLatchSec` / `ZoneDeepLatchMarginDeg` | `0.07f` / `4f` | дебаунс латча / однокадровый глубокий латч |
| `ZoneExitHysteresisDeg` | `6f` | Enter→Exit гистерезис |
| `GraceLatchMaxGapSec` / `NearZoneDeg` | `0.5f` / `3f` | ретро-латч Bottom при потере трекинга у дна |
| `AdaptiveZonesAffectLatch` | `false` | адаптация — только HUD-полосы в этом релизе |
| `AmplitudeGaugeTopDeg` / `BottomDeg` | `175f` / `75f` | фиксированная шкала d01 |
| `BottomAltChannelEnabled` | `false` | канал B (прижатые локти) — после acceptance-записей |
| `BottomTickFreqHz` / `RejectBuzzFreqHz` | `1320f` / `220f` | нижний тик / buzz на veto |
| `ViewFrontalMaxRatio` / `ViewSideMinRatio` | `0.7f` / `1.6f` | гистерезис R_med классификатора вида |
| `ViewSwitchVotes` / `ViewSwitchWindow` | `20` / `30` | «20 из 30 голосующих» для смены вида |
| `FrontalMaxBodyInclineKappa` | `0.35f` | F3: κ армирования (поднят с 0.28) |
| `KappaDriftSoftDock` / `HardVeto` | `0.08f` / `0.15f` | пер-реп κ-drift от baseline |
| `SetupMinShoulderWidthImg` / `MaxImg` | `0.17f` / `0.38f` | F0: коридор дистанции ~1.3–2.3 м |
| `SetupMaxPhonePitchDeg` | `30f` | F0: IMU-гейт наклона телефона |
| `FrontalArmingHipAvailabilityMin` | `0.7f` | F0: hip fail-closed на армировании |
| `MinChestTravelFracHard` / `Soft` | `0.25f` / `0.40f` | FullRom v2: HardVeto ниже 0.25, SoftDock в [0.25, 0.40) |
| `BodySwingWidthRatioMin` / `MaxTravelFrac` | `1.15f` / `0.30f` | BodySwing: рост ширины при малом y-ходе → veto |
| `WristDriftAbsDeadband` | `0.008f` | абсолютный deadband дрейфа запястий |
| `KneeDropDeltaDisarm` / `HardVeto` / `SoftDock` | `0.12f` / `0.15f` / `0.10f` | S-KNEE-1 |
| `FootVanishHighVis` / `LowVis` | `0.6f` / `0.35f` | S-KNEE-2 FootVanish |
| `FootDriftEventFrac` | `0.25f` | S-KNEE-2 FootDrift |
| `SupportWristBelowShoulderFrac` / `BelowHipFrac` | `0.15f` / `0.15f` | S-AIR-1 P1/P2 (стол/стена/воздух) |
| `FrontalMinHipShoulderCorr` | `0.45f` | HipDecoupling frontal (перспектива давит ход таза) |
| `HipDropRatioMin` / `Max` | `0.15f` / `1.1f` | HipDecoupling frontal полоса |
| `KneeBendSideProjMinFrac` | `0.8f` | KneeBend — hard только при Side и ноге «в плоскости» |

### Per-rep auditor (Stage 2, `AntiCheatAuditor` + 5 валидаторов)

| Константа | Значение | Примечание |
|-----------|----------|------------|
| `RepWindowMaxFrames` | `512` | Ring-буфер samples ≈ 17с @ 30fps (запас над `MaxRepSeconds`) |
| `RepBodyAxisLeadFrames` | `5` | Сколько первых Top-кадров усреднять для body-axis оценки |
| `MaxRepSeconds` | `12f` | `TempoSanityGate`: дольше — HardVeto TooSlow |
| `RepWindowMinVisibilityAvg` | `0.60f` | `RepVisibilityGate`: ниже — HardVeto LowVisibility |
| `RepWindowSoftDockVisibilityAvg` | `0.70f` | Между этим и hard floor — SoftDock |
| `PoorTrackingPenalty` | `0.15f` | Penalty для PoorTracking |
| `MinChestTravelFracBody` | `0.30f` | `FullRomGate`: chest travel ≥ 30% длины торса; иначе HardVeto ChestNotLowered |
| `MinBilateralAmplitudeRatio` | `0.50f` | `BilateralSymmetryGate`: ниже + обе руки видны → HardVeto Asymmetric |
| `SymmetryArmVisibilityThreshold` | `0.75f` | Доля кадров каждой руки до применения symmetry-чека (side-camera friendly) |
| `MaxBilateralMeanAbsDiffDeg` | `20f` | Mean \|L−R\| выше → SoftDock SlightAsymmetry |
| `SlightAsymmetryPenalty` | `0.20f` | Penalty для SlightAsymmetry |
| `MinHipShoulderCorrelation` | `0.60f` | `HipDecouplingGate`: Pearson corr ниже → SoftDock HipDecoupled |
| `HipDecouplingPenalty` | `0.25f` | Penalty для HipDecoupled |
| `MaxAggregatedSoftDockPenalty` | `0.80f` | Cap агрегированных soft-dock penalty (FORM не уйдёт в 0) |

## Трофеи (PvP)

| Константа | Значение |
|-----------|----------|
| `TROPHY_WIN` | `25` |
| `TROPHY_LOSS` | `15` |

## Трофеи (Ghost vs собственный лучший рекорд)

Половина от PvP (округление вниз для победы, вверх для поражения — зафиксировать в коде один раз):

| Константа | Значение | Примечание |
|-----------|----------|------------|
| `TROPHY_GHOST_WIN` | `12` | `Math.floor(TROPHY_WIN / 2)` |
| `TROPHY_GHOST_LOSS` | `7` | `Math.floor(TROPHY_LOSS / 2)` |

**Решение проекта:** `TROPHY_GHOST_WIN = 12`, `TROPHY_GHOST_LOSS = 7` — явно в `onGhostMatchFinished` и в клиентских подсказках UI.

## Ghost recording

| Константа | Значение | Примечание |
|-----------|----------|------------|
| `GHOST_MAX_FILE_BYTES` | `204800` | 200 КБ |
| `GHOST_SKELETON_HZ` | `30` | Целевая частота записи/воспроизведения |
| `GHOST_KEYPOINT_COUNT` | `17` | Сжатый скелет для сети/файла (не все 33 точки MediaPipe) |
| `GHOST_STORAGE_PREFIX` | `ghost/{uid}/best.bin` | Один файл «лучшей» записи на упражнение (см. ghost-mode-spec) |

## Async ghost-дуэли и боты (фаза 12.5)

Подбор соперника без онлайна: чужая ghost-запись из пула по трофейному диапазону, иначе бот-пейсер. См. [phase-12.5-async-ghost-duels.md](../plan/phase-12.5-async-ghost-duels.md). Трофеи — те же `TROPHY_GHOST_WIN/LOSS`.

| Константа | Значение | Примечание |
|-----------|----------|------------|
| `GHOST_POOL_TROPHY_BAND` | `150` | Полуширина диапазона подбора чужой записи `[мои ± band]` |
| `GHOST_POOL_SAMPLE_LIMIT` | `20` | Сколько кандидатов читать перед выбором одного |
| `BOT_PACER_PROFILES` | `slow / medium / fast` | Пресеты темпа на пустой пул (неотличимы в UI) |

## Лиги (MVP)

Пороги по трофеям:

| Ранг | minTrophies | maxTrophies |
|------|-------------|-------------|
| bronze | 0 | 399 |
| silver | 400 | 799 |
| gold | 800 | 1199 |
| diamond | 1200 | ∞ |

## Экономика XP, уровней, Ауры (MVP)

Клиентский источник правды — `Assets/_Project/Scripts/Core/Economy/EconomyConfig.cs` (зеркалит эту таблицу).
Полное обоснование чисел — `docs/design/economy.md`. Значения, помеченные **(сервер)**, также живут в
`functions/src/constants.js` и должны совпадать.

### XP за действия

| Правило | Значение | Примечание |
|---------|----------|------------|
| `XP_PER_REP` **(сервер)** | `10` | базовый XP за реп при form = 100% |
| Множитель формы | `0.5 … 1.5` | линейно по FORM; ниже `FORM_XP_THRESHOLD` (40) — `0` XP |
| `DAILY_REP_XP_CAP` | `150` | репов/день, дающих XP (анти-гринд); сверх — идут только в статистику |
| `VS_WIN_XP` | `150` | победа в дуэли |
| `VS_LOSS_XP` | `50` | «утешительный» XP за поражение |
| `SESSION_COMPLETE_XP` | `100` | за тренировку ≥ `MIN_SESSION_REPS` (20) |
| `DAILY_LOGIN_XP` | `50` | первый вход за день |
| Оффлайн-тренировка | — | локальный буфер → `syncOfflineXp` (идемпотентный токен) |

### Кривая уровней

`cost(L→L+1) = round(LEVEL_COST_BASE × L^LEVEL_COST_EXPONENT)`

| Константа | Значение | Примечание |
|-----------|----------|------------|
| `LEVEL_COST_BASE` | `120` | масштаб кривой |
| `LEVEL_COST_EXPONENT` | `1.5` | «быстрый старт, потом круче» |
| `MAX_LEVEL` | `100` | потолок |

Ориентир: ур.2 = 120 XP, ур.5 ≈ 2 043, ур.10 ≈ 13 326, ур.20 ≈ 78 000 (накопительно). XP не теряется.

### Стрик (дни подряд)

| Константа | Значение |
|-----------|----------|
| `STREAK_XP_BONUS_PER_DAY` | `0.05` (+5%/день к XP тренировки) |
| `STREAK_XP_BONUS_CAP` | `0.50` (потолок +50% на 10-й день) |
| Вехи | `7` и `30` дней (награда Аурой) |

### Аура (премиум-валюта) — только за прогресс, не за репы

| Источник | Значение |
|----------|----------|
| `AURA_PER_LEVEL` | `5` за уровень |
| Юбилейный уровень (кратный 5) | `+25` |
| Промо в лигу (первый раз) | Silver `50`, Gold `75`, Diamond `100` |
| Стрик-веха | 7 дн → `30`, 30 дн → `150` |
| IAP | основной денежный источник (магазин — позже) |

> TODO: при появлении серверной логики уровней/Ауры (награды на бэкенде) — отразить новые
> константы в `functions/src/constants.js`. Сейчас они клиентские.

### Ежедневная цель и недельный зачёт (фаза 11.5)

См. [phase-11.5-daily-challenge.md](../plan/phase-11.5-daily-challenge.md). Бонус за цель не обходит `DAILY_REP_XP_CAP`.

| Константа | Значение | Примечание |
|-----------|----------|------------|
| `DAILY_GOAL_BASE_REPS` | `20` | Базовый дневной таргет (масштабируется уровнем/лигой) |
| `DAILY_GOAL_XP_BONUS` | `75` | Разовый XP за выполнение цели дня |
| `WEEKLY_BOARD_RESET_DOW` | `Mon` | День недели для обнуления недельного зачёта (TZ проекта — UTC) |

## Клиентское кеширование

| Константа | Значение |
|-----------|----------|
| `LEADERBOARD_CACHE_SEC` | `60` |

## Photon

| Константа | Примечание |
|-----------|------------|
| Webhook заголовок | `X-Photon-Token` = секрет из env |
| Отключение соперника | Окно реконнекта `10` сек (клиент + финализация комнаты → webhook) |

---

Импорт в Cloud Functions: `functions/src/constants.js` должен отражать таблицы выше.
