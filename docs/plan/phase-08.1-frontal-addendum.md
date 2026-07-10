# Phase 08.1 addendum — фронтальная камера: консолидированная спецификация

Статус: ФИНАЛ для имплементации. Консолидирует 4 дизайн-линзы и 3 адверсарных ревью; все конфликты
разрешены, решения зафиксированы. Ссылки на код — `Assets/_Project/Scripts/CV/`.

Сеттинг: телефон на полу (или низко подпёрт), 1.5–2 м ПЕРЕД пользователем, лицом к нему.
Голова/лицо крупно внизу кадра, запястья на полу слева/справа от головы, торс уходит вглубь
(перспективное укорочение), ноги почти не видны. На устройстве подтверждено: image-space локтевой
угол работает фронтально — TOP ≈ 166°, BOTTOM ≈ 92°.

---

## Контекст: фронтальная камера

**Сквозной факт №0 — анизотропия координат.** Нормализованные x,y BlazePose анизотропны (x — доля
ширины, y — доля высоты; портрет ≈ ×1.78 искажение между осями). Все НОВЫЕ метрики считаются в
аспект-корректированном («sq») пространстве: `x' = x · (W/H)` (утилита в `PoseMath`, аспект
приходит из `PoseFrame`). FSM локтевого угла остаётся в legacy-пространстве — пороги 160/95
выверены на устройстве именно в нём. Второй факт: знак (hipY − shoulderY) МЕНЯЕТСЯ внутри повтора
(в TOP таз чуть ниже плеч в кадре, в BOTTOM чуть выше) — «таз выше плеч» как признак использовать
НЕЛЬЗЯ; надёжный фронтальный признак — коллапс `torsoLen_img` ≈ 0.30 (сбоку) → 0.02–0.05 (фронт).

**H1 (FullRomGate) — ПОДТВЕРЖДЕНА, с уточнением.** Фронтально `spineDir` ≈ вертикаль, его
перпендикуляр (gravityProxy) ≈ горизонталь, а реальный ход груди вертикален → проекция ≈ 0. Но
отказ не детерминированный: при длине вектора торса ~0.05 и джиттере ~0.01 ось шумит ±15°, и
travelFrac скачет 0.2–0.7 вокруг порога — «шумовая рулетка»: честные повторы ветируются случайно,
читы случайно проходят. Вторичный признак «рост ширины плеч внизу» слаб (+4–8%, не +20%) — только
tie-breaker. Фикс: view-адаптивная ось проекции (Frontal → вертикаль кадра; Side → как сейчас;
Unknown/Ambiguous → PCA), масштаб `S = max(torsoLen, shoulderWidth)`, см. раздел «FullRomGate v2».

**H2 (PlankArmer) — ПОДТВЕРЖДЕНА, доминирует другой отказ + найден третий.** Главный блокер —
пункт 1 (`PlankLowerBodyVisibility = 0.7`): фронтально vis коленей 0.2–0.6, голеностопов 0.1–0.4 →
перманентный `LowerBodyNotVisible`, армер не взводится вообще. BodyLineAngle вырожден иначе, чем
ожидалось: формально проходит (~170–180°), но шумит ±15° на базе 0.12 ед. и ничего не
дискриминирует (колено-планка так же коллинеарна). Третий, незаявленный блокер: WristAnchorMonitor
нормирует дрейф на torsoScale, который фронтально коллапсирует ×6 → ложный `Airborne` → блок
армирования при идеально стоящих руках. Фикс: view-ветка предиката (F0–F6) + безусловный фикс
масштаба WristAnchor.

**H3 (KneeBendDetector) — ПОДТВЕРЖДЕНА; полноценного image-space фикса НЕТ.** Сгиб колена лежит в
сагиттальной (глубинной) плоскости: проекция hip-knee-ankle почти коллинеарна при ЛЮБОМ реальном
сгибе, vis ниже порога большую часть времени, проецированный угол — шум. Детектор фронтально не
слабый — неинформативный. Что реально работает: κ-наклон корпуса при армировании
(`κ = (hipMid_y − shoulderMid_y)/sw`) отсекает старт с коленей/сидя/стоя; **KneeDropDelta**
(колено относительно бедра vs baseline армирования) — единственный физически неустранимый признак
опускания на колени (колени падают ~13 см → ~0.24·sw в кадре). Опускание на колени в середине
подхода с отравленным baseline — принятый MVP-риск (см. античит-раздел).

---

## ViewClassifier

Новый класс `ViewClassifier.cs` (pure C#, PushStars.CV). Тикается в `PushupSession.HandleFrame`
ПЕРВЫМ — до WristAnchor/Knee/Armer/Tracker.

**Метрика (каждый кадр, sq-пространство):**

```
shoulderWidth = |Lsh' − Rsh'|
torsoLen      = |shoulderMid' − hipMid'|
R             = torsoLen / max(shoulderWidth, 1e-3)
```

Кадр «голосует», если vis обоих плеч ≥ 0.5 И хотя бы ОДНОГО таза ≥ 0.35 (порог таза снижен: R —
грубая метрика; фронтальная vis таза 0.40–0.75 — рутинно ниже 0.5). Неголосующий кадр НЕ сбрасывает
статистику — просто не двигает её (фикс ревью: правило «20 подряд + сброс» приводило к вечному
Unknown у заметной доли фронтальных пользователей).

**Сглаживание и переключение:**
- R → медиана по `RingBuffer<float>` из 9 голосующих кадров.
- Raw-класс: `Frontal` если R_med < 0.7; `Side` если R_med > 1.6; иначе `Ambiguous` (зазор =
  гистерезис; диагональ ~45° намеренно попадает в Ambiguous).
- Смена состояния: **20 из последних 30 голосующих кадров** согласны с новым классом, И
  (`PushupRepCounter.Phase == Top && !_reachedBottom`) ЛИБО армер Disarmed — вид не переключается
  посреди повтора.
- Старт сессии и потеря трекинга > 1 с → `Unknown`.
- Экспонирует `View`, `R_med`, счётчик голосов — в `PushupDebugHud`.

**Таблица потребителей (все 4 состояния определены явно):**

| Потребитель | Frontal | Side | Ambiguous | Unknown |
|---|---|---|---|---|
| PlankArmer предикат | F0–F6 | текущие пункты 1–5 | OR(F0–F6, Side-предикат) | OR |
| FullRomGate ось | вертикаль (0,1) | perp(bodyAxis), как сейчас | PCA | PCA |
| HipDecouplingGate | y-корреляция | текущая логика | y-корреляция | y-корреляция |
| KneeBendDetector (hard-консюм) | НЕТ (κ + KneeDrop) | ДА | НЕТ | НЕТ |
| WristAnchorMonitor масштаб | S = max(torso, sw) — безусловно, view-независимо | ← | ← | ← |

Обоснование дефолтов Ambiguous/Unknown: камера продукта — на полу, фронтальные ветки безопаснее
(y-корреляция и PCA деградируют мягко; боковая ось у фронтального пользователя — рутинный ложный
SoftDock).

---

## Точное определение верх/низ (AmplitudeTracker)

Приоритет №1 владельца. Новые файлы `OneEuroFilter.cs`, `AmplitudeTracker.cs`.

### Конвейер сигнала

```
θraw (PoseMath.TryElbowAngle) → медиана-из-3 → θm  ──→  ЛАТЧИ ЗОН (по θm)
                                              └─→ One-Euro → θs → HUD, Phase, статистика
```

**Решение по сглаживанию: One-Euro** (не EMA — EMA не может одновременно дать чистый сигнал у
точек разворота и лаг < 70 мс в движении). Параметры **ужесточены после ревью быстрого темпа**
(при каденсе 1.5 повт/с срез 1.5 Гц поднимал сглаженный минимум над зоной):

```
MinCutoffHz = 2.5   Beta = 0.05   DerivCutoffHz = 1.0
τ(fc) = 1/(2π·fc);  α(fc,dt) = 1/(1 + τ/dt)
v̂  = lowpass(Δθ/dt, α(DerivCutoffHz, dt))
fc = MinCutoffHz + Beta·|v̂|
θs = lowpass(θm, α(fc, dt))
dt из frame.TimestampSec, кламп [0.0167, 0.10] с
```

**Гейтинг входа (критично — фикс сентинеля 180):** `PoseMath.ElbowAngle` при потере обеих рук
возвращает 180f — однокадровый vis-провал у дна давал бы скачок 92→180→92, One-Euro (быстрый на
скачках) пробил бы ложный Top-латч. Обязательные меры:
1. Новый `PoseMath.TryElbowAngle(in PoseFrame, out float)` — false, если ни одна полная рука
   (плечо-локоть-запястье) не проходит vis ≥ 0.5. Сентинель в конвейер НЕ попадает никогда.
2. Медиана-из-3 перед One-Euro (3 float, ноль аллокаций) — гасит любые однокадровые спайки.
3. Hampel-кламп: |θraw − θs| > 40° за кадр → кадр — выброс, θs/θm держатся.
4. Невалидный кадр → θs/θm заморожены, gap-таймер; gap > `TrackerRebaseAfterLostSec = 0.5` →
   пере-сид фильтра на следующем валидном кадре, дуга → Idle.

**Латчи считаются по θm (спайк-фри raw), а не по θs** — асимметричное решение из ревью темпа: лаг
фильтра не съедает впадину на 1.5 повт/с, а шумовой вброс уже убит медианой + двухкадровым
дебаунсом. θs — только HUD, Phase (знак дельты) и пер-реп статистика.

### Шкала глубины (фиксированная — шкала HUD не «дышит»)

```
d01 = Clamp01( (175 − θs) / (175 − 75) )
// 175° → 0.00, 160° → 0.15, 95° → 0.80, 75° → 1.00
```

### Зоны: абсолютный конверт — единственный источник латчей в этой фазе

**РЕШЕНИЕ (разрешение конфликта адаптивных зон):** адверсарное ревью нашло в адаптивном латчинге
тихий дедлок-храповик (свежие глубокие повторы ужесточают зоны → усталые честные повторы не латчатся
→ окно медиан заморожено → счётчик молчит; top-decay недостижим из AwaitTop). Поэтому:

- **Латчи в этом релизе — ТОЛЬКО по абсолютным порогам: `TopEnter = 160`, `BottomEnter = 95`.**
- Адаптивное ужесточение (медианы окна 5 засчитанных, margin 8°, потолок ужесточения низа 3° /
  верха 7°, decay-клапан ±3° к полу, rate-limit 1°/повтор, явный top-decay: заход в
  [TopEnter−10, TopEnter) со спуском ≥ 15° без латча ×2 → TopEnter −3) — реализуется в трекере,
  но влияет **ТОЛЬКО на HUD-полосы** (`AdaptiveZonesAffectLatch = false`). Включение латчей по
  адаптивным зонам — после телеметрии распределений θtop/θbot, отдельным решением.
- Анти-чит гарантия остаётся структурной: адаптация умеет только СТРОЖЕ (TopEnter ≥ 160,
  BottomEnter ≤ 95), окно — только из засчитанных повторов, decay не пробивает пол. Micro-bob
  математически не расширяет зачётные зоны.

**Канал B нижнего латча (фикс КРИТИЧНОГО false negative: прижатые локти / широкий хват).**
Трицепсовый/алмазный стиль гнёт локоть в глубинной плоскости — проецированный угол в честном низе
читается 100–140° и НИКОГДА не пересекает 95; широкий хват механически останавливается на 100–115°.
Второй канал с ФИКСИРОВАННЫМИ (не адаптивными — не калибруется пользователем, анти-чит принцип
не нарушен) порогами:

```
BottomLatch = (θm ≤ 95)                                          // канал A
           ИЛИ (θm ≤ 120
                И  вертикальный ход shoulderMid_y от TOP ≥ 0.6·sw
                И  |nose_y − wristMid_y| ≤ 0.15·sw)              // канал B
```

«Голова у линии ладоней + реальный спуск плеч» — физика, не изображаемая без опускания груди;
клевок головой режется требованием θm ≤ 120 + ходом плеч; складка с коленей — KneeDropDelta.
Канал B за флагом `BottomAltChannelEnabled` = **false до acceptance-записей** трицепсового,
алмазного и широкого стилей (пороги 120 / 0.6 / 0.15 вербуются по записям, не по модели). До
включения: детект паттерна «глубокий ход плеч при θm, застрявшем в 100–125» → HUD-подсказка
«разведите локти шире» вместо молчаливого несчитания.

### Латч / дебаунс / арочный автомат

- **Latch = 2 последовательных валидных кадра** с θm в зоне (`ZoneLatchSec = 0.07`, считается по
  таймстампам, не по кадрам — устойчиво к 25 fps), ЛИБО **1 кадр глубоко в зоне**:
  θm ≤ BottomEnter − 4 (симметрично θm ≥ TopEnter + 4).
- Гистерезис выхода: `BottomExit = BottomEnter + 6`, `TopExit = TopEnter − 6` (по θm).
- **Арочный автомат:** `Idle → AwaitBottom → AwaitTop → AwaitBottom …`. BottomLatched стреляет
  только в AwaitBottom, TopLatched — только в AwaitTop; болтанка у края зоны не рефайрит. Из Idle
  в AwaitBottom — после 2 кадров в верхней зоне (старт строго сверху, согласовано с
  `ArmingElbowTopAngle = 150`).
- **Grace-латч при потере трекинга у дна** (фикс «лучшие повторы не считаются»): если θm монотонно
  падал ≥ 4 кадра, последний валидный кадр был ≤ BottomEnter + 3, трекинг потерян < 0.5 с, после
  восстановления θm растёт, И WristAnchor был Anchored до и после провала → Bottom засчитывается
  задним числом временем последнего валидного кадра.
- **`MinRepSeconds`: 0.45 → 0.30** (решение по ревью темпа: 0.45 уже сегодня молча режет честные
  1.5 повт/с; взмахи руками режутся конвертом 95↔160 + армером + SupportGeometryGate — 0.45 больше
  не единственная защита). Меряется `TopLatchTime − BottomLatchTime` (оба латча задержаны на
  ~одинаковые ~80–100 мс → интервал несмещённый).
- Дизарм / потеря > 0.5 с → автомат в Idle, ватермарки дуги сброшены, фильтр на пере-сид.
  Адаптивное окно (HUD) переживает короткий дизарм, чистится только в `Reset()`.

### Интеграция со счётчиком — ВЫБРАННЫЙ вариант

**Трекер — зависимость конструктора `PushupRepCounter` (nullable); владеет им и тикает его
`PushupSession` СТРОГО до `Counter.Process`.** Отвергнута альтернатива «pre-computed angle
parameter»: она продублировала бы зонную логику в двух автоматах → дрейф и двойной источник истины.
Единственный владелец латчей — трекер; счётчик сохраняет дугу повтора, аудит-хук, MinRepSeconds и
темп. `tracker == null` → легаси-путь на сыром угле нетронут (юнит-тесты Stage 1/2 без изменений).

```csharp
public struct OneEuroFilter
{
    public float MinCutoffHz, Beta, DerivCutoffHz;
    public bool  IsInitialized { get; }
    public float LastSpeedDegPerSec { get; }          // |v̂| для HUD
    public void  Reset();                             // следующий Filter() = сид
    public float Filter(float raw, float dtSec);      // dt клампится внутри
}

public enum DepthArcState { Idle = 0, AwaitBottom = 1, AwaitTop = 2 }

public sealed class AmplitudeTracker
{
    // пофреймовые выходы
    public float SmoothedElbowDeg { get; }            // θs (HUD/Phase/статистика)
    public float MedianElbowDeg { get; }              // θm (латчи)
    public float CurrentDepth01 { get; }
    public DepthArcState ArcState { get; }
    public bool  InTopZone { get; }                   // θm ≥ TopEnterDeg
    public bool  InBottomZone { get; }                // θm ≤ BottomEnterDeg
    public bool  BottomLatched { get; }               // уровень в текущей дуге
    public bool  BottomLatchedThisTick { get; }       // импульс
    public bool  TopLatchedThisTick { get; }          // импульс
    public float BottomLatchTimeSec { get; }          // -1 если нет

    // зоны (латчи — абсолютные; адаптивные значения — только для HUD-полос)
    public float TopEnterDeg { get; }                 // = 160 (латч)
    public float BottomEnterDeg { get; }              // = 95 (латч)
    public float HudTopEnterDeg { get; }              // ∈ [160, 167], HUD-only
    public float HudBottomEnterDeg { get; }           // ∈ [92, 95], HUD-only

    // ватермарки
    public float RepMinDepth01 { get; }
    public float RepMaxDepth01 { get; }
    public float LastRepMinDepth01 { get; }
    public float LastRepMaxDepth01 { get; }

    // сырьё для пер-реп аудита (FullRomGate frontal)
    public float ArcShoulderWidthMinImg { get; }
    public float ArcShoulderWidthMaxImg { get; }
    public float ArcShoulderMidYMin { get; }
    public float ArcShoulderMidYMax { get; }

    public event Action OnBottomLatched;              // → нижний тик 1320 Гц
    public event Action OnTopLatched;

    public void Tick(in PoseFrame frame, bool trackingOk, bool isArmed, float timeSec);
    public void CommitRepAccepted();                  // экстремумы → адаптивные медианы (HUD)
    public void CommitRepRejected();                  // экстремумы отброшены
    public void Reset();
}
```

`PushupRepCounter` при `tracker != null`: `CurrentElbowAngle = tracker.SmoothedElbowDeg`; «низ
достигнут» = `BottomLatchedThisTick`; «вернулся наверх» = `TopLatchedThisTick`; `_bottomTime =
BottomLatchTimeSec`; Phase — знак дельты θs. Сигнатура `Process` не меняется.

Порядок тика в `PushupSession.HandleFrame`:

```
ViewClassifier.Tick → KneeBend.Tick → WristAnchor.Tick → KneeDrop.Tick → Armer.Tick
→ Tracker.Tick(frame, trackingOk, Armer.IsArmed, Time.time)
→ Auditor.RecordSample(...)                     // RepSample получает θs, θm, sw, kneeMid, κ
→ Counter.Process(frame, trackingOk, Armer.IsArmed)
```

Подписки: `Counter.OnRep → Tracker.CommitRepAccepted()`; `Counter.OnRepRejected →
Tracker.CommitRepRejected()` + RejectBuzz; `Tracker.OnBottomLatched → BottomTick`.
`ResetSession()` += `Tracker.Reset()`. Медианы окна 5 — два преаллоцированных `float[5]`,
сортировка вставками, ноль GC.

---

## Фронтальные фиксы существующих сигналов

### PoseMath / PoseFrame (фундамент)

- `PoseFrame.Aspect` (W/H) от источника; `PoseMath.ToSquare(Vector2, aspect)` → sq-пространство.
- `PoseMath.TryElbowAngle(in PoseFrame, out float)` — см. выше. Старый `ElbowAngle` остаётся для
  легаси-пути.
- **Удалить legacy `LooksLikePushup` из `PushupRepCounter.Process`** (строка ~109): фронтальный
  vis-флаппинг колена ронял PlankBodyLine < 140 и сбрасывал `_reachedBottom` посреди честного
  повтора. Роль полностью перекрыта PlankArmer + аудитором.

### PlankArmer — view-ветка предиката

`IsValidPlank(view)`: Side → текущие пункты 1–5 без изменений. Frontal → **F0–F6**. Ambiguous /
Unknown → OR двух предикатов (fail-open на армирование; античит добирает пер-реп аудитором).

**F0 — SetupGate (единый пре-арм гейт кадрирования/дистанции; фиксы наклона, 1 м, головы за
кадром).** Все условия должны держаться окно Arming; отказ → PlankRejectReason + HUD-подсказка:
- дистанция по sw (sq): `sw ∈ [0.17, 0.38]` (~коридор 1.3–2.3 м) — иначе «подойдите/отойдитесь»;
- `nose_y ≤ 0.85` в TOP — иначе «отодвиньте телефон» (голова уйдёт за кадр внизу повтора);
- оба запястья vis ≥ 0.5 в TOP — иначе подсказка кадрирования;
- **IMU-pitch телефона** (`Input.acceleration`, дешёвый и точный): |наклон камеры| > 30° →
  «положите телефон ниже/ровнее». Снимает целый класс tilt-неопределённостей κ и F-порогов;
- **hip fail-closed (фикс дыры «стол при невидимом тазе»):** hipMid доступен < 70% кадров окна
  Arming → НЕ армировать + подсказка кадрирования. Fail-closed только на армировании; пер-реп
  гейты остаются fail-open.

Остальные пункты (все в sq-пространстве; sw = shoulderWidth_img):
- **F1** оба запястья vis ≥ 0.5 И `wristMid_y − shoulderMid_y ≥ 0.4·sw`. Фолбэк при невидимых
  запястьях, но видимых локтях: `elbowMid_y − shoulderMid_y ≥ 0.25·sw` (F5 тогда Unknown-допуск).
- **F2 (исправлено — алмаз был забанен):** `spread = |xLw − xRw|`;
  `spread ≥ 0.4·sw` (запястья по разные стороны от shoulderMid_x) **ИЛИ**
  (`spread < 0.4·sw` И оба запястья Anchored И `wristMid_y − shoulderMid_y ≥ 0.6·sw`) — узкая
  постановка армится через строгую опору. Требование ≥ 1.1·sw ОТМЕНЕНО.
- **F3** `κ = (hipMid_y − shoulderMid_y)/sw ∈ [−0.35, 0.35]`. Порог поднят 0.28 → **0.35**
  (ревью: узкие плечи ×1.2–1.3 и умеренный tilt ×1.25 ломали честных; зазор до сидя/стоя (κ ≥ 0.8)
  остаётся широким; чувствительность к старту с коленей компенсирует KneeDropDelta, он
  baseline-относительный и от sw не страдает). κ < −0.35 → reject (пик/«домик»).
- **F4** локоть ≥ 150 (без изменений).
- **F5** WristAnchor ≠ Airborne (на исправленном масштабе, см. ниже).
- **F6** `|nose_x − 0.5·(xLw + xRw)| ≤ 0.5·sw` — голова между ладонями.

Пункт 3 (KneeBend ≠ Bent) во фронтальной ветке ЗАМЕНЁН на F3 + KneeDropDelta-disarm (см. античит).

### WristAnchorMonitor — фикс масштаба (безусловный, view-независимый)

`TryTorsoScale` → `S = max(torsoLen_img, shoulderWidth_img)` в sq-пространстве (корректен и сбоку).
Пороги 0.10/0.20 сохранить: джиттер → frac ≈ 0.02–0.03 (Anchored), машущая рука → 0.18–0.5
(Airborne). Плюс **абсолютный deadband**: RMS-дрейф < 0.008 norm → Anchored независимо от
нормировки (защита дальних/маленьких силуэтов, где пиксельный джиттер не масштабируется с телом).

### FullRomGate v2 (фикс H1)

Ось проекции по view:
- **Frontal:** ось = (0, 1) — вертикаль кадра. Закрывает и «отжимания от стены стоя» (плечи не
  движутся по y → veto).
- **Side:** perp(bodyAxis) — текущая логика без изменений.
- **Unknown / Ambiguous:** PCA первой компоненты траектории shoulderMid (sq-пространство).

**PCA в замкнутой форме (ковариация 2×2):** по кадрам повтора p_i = shoulderMid'_i, среднее p̄:

```
Sxx = Σ(x_i−x̄)²   Syy = Σ(y_i−ȳ)²   Sxy = Σ(x_i−x̄)(y_i−ȳ)
θ   = 0.5 · atan2(2·Sxy, Sxx − Syy)          // угол главной оси
u   = (cos θ, sin θ)                          // первый собственный вектор
λ½  = (Sxx+Syy)/2 ± sqrt( ((Sxx−Syy)/2)² + Sxy² )   // собственные значения (для телеметрии)
travel = max_i((p_i − p̄)·u) − min_i((p_i − p̄)·u)
```

Накопление сумм — инкрементально за один проход, ноль аллокаций.

Масштаб: `S = max(torsoLen_img, shoulderWidth_img)` по lead-кадрам TOP (`RepBodyAxisLeadFrames`).
`travelFrac = travel / S`. **Пороги (разрешение конфликта 0.25 vs 0.45):**

```
travelFrac < 0.25              → HardVeto ChestNotLowered
0.25 ≤ travelFrac < 0.40       → SoftDock 0.25 (ShallowTravel)
travelFrac ≥ 0.40              → чисто
```

**Правило BodySwing (обязательное; закрывает дыру «widthRatio-обход» — наклонный чит к камере и
раскачка на коленях имеют сигнатуру «ширина растёт, y стоит», противоположную честному фронтальному
повтору Δy 0.10–0.14 при ширине +4–8%):**

```
widthRatio = ArcShoulderWidthMaxImg / ArcShoulderWidthMinImg
widthRatio ≥ 1.15 И travelFrac < 0.30  → HardVeto BodySwing
```

widthRatio НИКОГДА не спасает повтор в одиночку — только ветирует. Прежняя идея «widthRatio ≥ 1.10
подтверждает ROM» ОТМЕНЕНА (адверсарное ревью: она вайтлистила именно «приближение без опускания»).

### KneeBendDetector

Код без изменений. Консюмится как hard-сигнал ТОЛЬКО при `View == Side` И
`|proj(hip→ankle)| / sw ≥ 0.8` (нога достаточно «в плоскости кадра»). Frontal/Ambiguous/Unknown —
остаётся включённым как бонус (вертикальная голень при поднятых лодыжках внезапно даёт измеримый
угол), но не блокирует и не ветирует.

### BilateralSymmetryGate

**Без изменений.** Фронтально работает лучше, чем сбоку (обе руки видимы, оба локтевых угла в
картинной плоскости). Мониторить телеметрией false-SoftDock по порогу 20° при лёгком развороте
корпуса; менять только по данным.

### HipDecouplingGate

Side — текущая логика. Frontal/Ambiguous/Unknown:

```
corr = Pearson( shoulderMid_y[i], hipMid_y[i] )  по кадрам повтора
corr < 0.45                     → SoftDock 0.25 (HipDecoupled)
hipDropRatio = Δ(hipMid_y) / Δ(shoulderMid_y)    // экскурсии за повтор
hipDropRatio ∉ [0.15, 1.1]      → SoftDock 0.25
```

Порог 0.45 (не 0.6): перспектива давит ход таза до Δy ≈ 0.05. hipDropRatio < 0.15 — также
единственный (мягкий) мид-сет сигнал опускания на колени.

### RepVisibilityGate / TempoSanityGate

**Без изменений** (фронтальное среднее ≈ 0.83 > 0.60). Телеметрия: распределение per-rep vis
таза; если на устройствах vis таза < 0.3 прижимает среднее к 0.70 — запасной вариант (фронтальный
набор суставов: 6 верхних + nose вместо тазов) заготовлен, но не включается без данных.

---

## Новые фронтальные античит-сигналы

Выжили адверсарное ревью три сигнала + одно пер-реп правило. Все в sq-пространстве.

### S-KNEE-1 — KneeDropDetector (главный; ловит опускание на колени при чистом baseline)

Новый `AntiCheat/KneeDropDetector.cs` (per-frame) + вклад в пер-реп гейт `KneeCheatGate`.
- Входы: kneeMid (среднее видимых коленей vis ≥ 0.5; одно видимое — оно), hipMid, sw; baseline из
  окна Arming.
- `kneeRel = (kneeMid_y − hipMid_y) / sw`; `kneeRel_base` = среднее за окно Arming;
  `kneeRel_now` = EMA(α = 0.2) по кадрам с локтем ≥ 150° (только Top-фаза — иначе ловим кинематику
  спуска); `Δ = kneeRel_now − kneeRel_base` (y вниз → Δ > 0 = колени опустились относительно бёдер;
  ожидание чита ≈ +0.17…0.24 против джиттера 0.02–0.04).
- **Per-frame:** Δ ≥ 0.12 лентой 10 подряд кадров → disarm (KneesBent); отпускание Δ ≤ 0.06.
- **Per-rep (`KneeCheatGate : IRepValidator`):** средний Δ по Top-кадрам окна ≥ 0.15 → HardVeto
  `KneeCheat`; 0.10–0.15 → SoftDock 0.25. Требует kneeMid + hasKneeMid в `RepSample`.
- **κ-drift (второй вклад того же гейта; работает БЕЗ коленных лендмарков):**
  `κ_rep − κ_arm > 0.15 → HardVeto KneeCheat; > 0.08 → SoftDock 0.25`, где κ_rep — средний κ по
  Top-кадрам окна, κ_arm — baseline армирования. Ловит «встал честно → опустился на колени», даже
  когда колени невидимы.
- Колени невидимы весь подход → сигнал fail-open + телеметрия-флаг (см. риски).

### S-KNEE-2 — FootEventMonitor (корроборация, вариант «лодыжки подняты»)

Новый `AntiCheat/FootEventMonitor.cs`.
- **FootVanish:** visEMA(α = 0.1) от max(vis лодыжек/стоп); событие = держался ≥ 0.6 не менее 2 с
  после армирования, затем < 0.35 на ≥ 1 с при живом трекинге остального тела.
- **FootDrift:** RMS-дрейф лодыжки в 12-кадровом окне ОТНОСИТЕЛЬНО wristMid (вычитает тряску
  камеры), нормированный на sw; событие ≥ 0.25.
- Вердикты: одиночное событие → SoftDock 0.25 на последующие повторы + HUD-предупреждение;
  событие + подпороговый KneeDropDelta ≥ 0.08 → HardVeto `KneeCheat`.
- Стопы никогда не были видны → монитор не активируется (корректный fail-open для честного
  фронтала).

### S-AIR-1 — SupportGeometryGate («запястья — опора»; закрывает воздушные/наклонные)

Новый `AntiCheat/SupportGeometryGate.cs` (пер-реп + участвует в disarm-мониторинге). Только
ordering-сравнения по y — инвариантны к наклону телефона. Все проверки на Top-кадрах:
- **P1:** `wristMid_y − shoulderMid_y ≥ 0.15·sw` (запястья ниже плеч);
- **P2:** `wristMid_y − hipMid_y ≥ 0.15·sw` (запястья ниже бёдер) — убивает стол/стену;
- **P3** (только если колено/лодыжка видимы vis ≥ 0.5): `wristMid_y ≥ knee_y − 0.10·sw` И
  `wristMid_y ≥ ankle_y − 0.10·sw`.
- Провал P1/P2 → HardVeto `SupportGeometry`; провал P3 → SoftDock 0.25.
- Вместе с F3 (κ) и BodySwing закрывает: стоя-боб с приседом (после PCA-фикса FullRom его
  случайная защита исчезла — S-AIR-1 обязателен в ТОЙ ЖЕ фазе, что и FullRom v2), стол, стену,
  «локти на столе, качает голову» (плюс конверт локтя: статичный локоть → FSM вообще не даёт
  кандидата; масштаб лица не входит ни в один сигнал).

### ПРИНЯТЫЕ MVP-РИСКИ (сознательно не ловим; телеметрия обязательна)

| # | Риск | Почему принят | Телеметрия-маркер |
|---|---|---|---|
| 1 | Армирование сразу с коленей, колени/стопы вне кадра весь подход (baseline KneeDrop отравлен) | Единственная оборона — κ ≤ 0.35 (длинная коленная планка ≈ 0.29–0.45 — на грани); полное закрытие только floor-line/world-landmarks (08.2+). Полноамплитудное коленное отжимание — реальная работа; ложный veto честных хуже | `ArmedWithoutLowerBodyEver` — ни колено, ни лодыжка не достигли vis 0.5 за подход |
| 2 | Опускание на колени в СЕРЕДИНЕ подхода при невидимых коленях | Image-space почти не видит; ловим только SoftDock по hipDropRatio < 0.15 и κ-drift | счётчик SoftDock HipDecoupled/κ-drift за подход |
| 3 | Повторы делает друг | BlazePose не несёт идентичности; face-embedding — 08.2+ с privacy-ревью | скачок производительности против истории (count/темп/глубинный профиль) |
| 4 | Видео в камеру (replay) | Liveness — 08.2+; нижний тик 1320 Гц — готовая инфраструктура темпо-челленджа | `ReplaySuspect`: corr соседних θs-траекторий RepWindow > 0.98 И дисперсия длительности ≈ 0 |
| 5 | Отжимания на кулаках/упорах 10–15 см | Проходит по margins; это честная работа | нет |
| 6 | Камера на полувысоте (0.8–1.2 м) уплощает перспективу, κ сползает | F0-IMU-гейт (30°) режет грубые случаи; точная перекалибровка κ по h_cam — после телеметрии | R_med + κ_arm распределения |
| 7 | Ненадёжная ангулярка при коллапсе предплечья | MediaPipe галлюцинирует при предплечьях в камеру | пометка повторов с локоть–запястье < 0.3·sw на Top-кадрах |
| 8 | Прощупывание decay-клапана | Худший исход decay = сегодняшний абсолютный конверт (профит нулевой); в этой фазе латчи вообще абсолютные | счётчик decay-событий за подход |

---

## Шкала амплитуды (debug HUD)

`PushupDebugHud.cs`, IMGUI OnGUI. Утилитарный тюнинг-инструмент, полировка позже.

**Раскладка (в единицах `Screen.height`; ниже s = Screen.height/100):**
- Вертикальный бар у правого края: ширина 3·s, высота 55·s, отступ справа 2·s, центр по вертикали.
- Фон бара: rgba(0.1, 0.1, 0.1, 0.75), рамка 1 px серая.
- d01 = 0 — ВЕРХ бара (локаут), d01 = 1 — низ.
- **TOP-зона:** полоса [0 .. 0.15] (θ ≥ 160). **BOTTOM-зона:** полоса [0.80 .. 1.0] (θ ≤ 95).
  Цвет зоны: тусклый зелёный rgba(0.2, 0.6, 0.2, 0.35); когда θm В зоне — яркий rgba(0.2, 0.9,
  0.2, 0.8); на латче — вспышка белым 150 мс.
- **HUD-адаптивные полосы** (`HudTopEnterDeg`/`HudBottomEnterDeg`): более яркая внутренняя кромка
  внутри абсолютных полос — визуально «зона сжимается», тренируя пользователя; на латчи не влияет.
- **Маркер текущей глубины:** горизонтальная линия 0.5·s, белая, по `CurrentDepth01` (θs); при
  замороженном сигнале (невалидные кадры) — красная.
- **Ватермарки:** текущая дуга min/max — жёлтые тики слева от бара; последний завершённый повтор
  (`LastRepMin/MaxDepth01`) — серые тики справа.
- Подписи под баром: `θs / θm` (1 знак), `ArcState`, `View + R_med`, `κ`, `Δknee`, WristAnchor
  вердикт + drift frac.

**Байндинги:** `Tracker.CurrentDepth01, SmoothedElbowDeg, MedianElbowDeg, ArcState, InTop/BottomZone,
RepMin/MaxDepth01, LastRepMin/MaxDepth01, HudTop/BottomEnterDeg`; `ViewClassifier.View, R_med`;
`KneeDropDetector.Delta`; `PlankArmer` причина отказа F0–F6 (текстом — подсказки SetupGate).

---

## Звук

Все клипы процедурные (`AudioClip.Create` в Awake, 44100 Гц, моно), без ассетов.

| Клип | Частота | Длительность | Огибающая | Триггер | Дебаунс |
|---|---|---|---|---|---|
| RepBeep (есть) | 880 Гц | как есть | как есть | Counter.OnRep | — |
| **BottomTick** | 1320 Гц (E6, отличим от 880) | 40 мс | атака 2 мс, exp-спад τ = 15 мс | Tracker.OnBottomLatched | 1/дугу (гарантирует автомат) + мин. интервал 0.15 с |
| **RejectBuzz** | 220 Гц | 250 мс | атака 5 мс, линейный спад последние 100 мс | Counter.OnRepRejected | мин. интервал 0.5 с |

Решение по конфликту линз (440 vs 1320 Гц для нижнего тика): **1320 Гц** — короткий высокий тик
перцептивно резче отделяется от 880 Гц rep-бипа, чем низкий 440 при 40 мс. Бюджет задержки нижнего
тика: медиана-3 (~33 мс) + дебаунс 2 кадра (~66 мс) ≈ 100 мс — субъективно мгновенно (< 150 мс).

---

## Файлы и порядок коммитов

### Новые файлы

| Файл | Ответственность |
|---|---|
| `CV/OneEuroFilter.cs` | struct: 2 однополюсных lowpass, Reset/Filter, ноль аллокаций |
| `CV/AmplitudeTracker.cs` | θm/θs, зоны, арочный автомат, латчи (+канал B за флагом), ватермарки, HUD-адаптация, события |
| `CV/ViewClassifier.cs` | R-метрика, медиана-9, голосование 20-из-30, состояние View |
| `CV/AntiCheat/KneeDropDetector.cs` | kneeRel baseline/EMA, Δ, per-frame disarm-сигнал |
| `CV/AntiCheat/KneeCheatGate.cs` | IRepValidator: пер-реп Δknee + κ-drift → HardVeto/SoftDock |
| `CV/AntiCheat/FootEventMonitor.cs` | FootVanish + FootDrift, события → dock/эскалация |
| `CV/AntiCheat/SupportGeometryGate.cs` | IRepValidator: P1–P3 ordering-проверки опоры |
| `CV/MockFrontalPushupPoseSource.cs` | честный фронтальный мок (таблица ниже) |
| `CV/MockFrontalKneePoseSource.cs` | фронтальное коленное (колени падают на ~0.24·sw) |
| `CV/MockInclineTablePoseSource.cs` | стол/наклонный чит (запястья выше бёдер, widthRatio 1.3+) |
| `CV/MockStandingBobPoseSource.cs` | стоя-боб с приседом (κ >> 1, y-ход плеч без опоры) |

### Изменяемые файлы

| Файл | Изменение |
|---|---|
| `CV/PoseMath.cs` | ToSquare, TryElbowAngle |
| `CV/PoseFrame.cs` | поле Aspect (W/H) |
| `CV/PushupRepCounter.cs` | ctor(tracker), ветка латчей трекера; УДАЛИТЬ LooksLikePushup из Process |
| `CV/PushupSession.cs` | тик-цепочка (ViewClassifier первым, Tracker перед Counter), подписки, звуки, Reset |
| `CV/CVConstants.cs` | таблица ниже; MinRepSeconds 0.45 → 0.30 |
| `CV/AntiCheat/WristAnchorMonitor.cs` | TryTorsoScale → max(torso, sw) sq + deadband 0.008 |
| `CV/AntiCheat/PlankArmer.cs` | view-ветка: F0 (SetupGate, IMU, hip fail-closed) + F1–F6 |
| `CV/AntiCheat/FullRomGate.cs` | ось по view, PCA closed-form, пороги 0.25/0.40, BodySwing |
| `CV/AntiCheat/HipDecouplingGate.cs` | frontal-ветка: y-corr 0.45 + hipDropRatio [0.15, 1.1] |
| `CV/AntiCheat/RepSample.cs` / `RepWindow.cs` | + θs, θm, kneeMid+has, sw, κ; TryComputeBodyAxis возвращает и sw |
| `CV/AntiCheat/RepRejectReason.cs` | + KneeCheat, BodySwing, SupportGeometry, ShallowTravel |
| `CV/AntiCheat/PlankRejectReason.cs` | + BadFraming, TooCloseOrFar, PhoneTilted, HipNotVisible, BodyIncline |
| `CV/PushupDebugHud.cs` | amplitude gauge, View/κ/Δ readouts, подсказки SetupGate |
| `docs/architecture/constants.md` | зеркало новых констант |

### Порядок коммитов (каждый — компилируется, тесты зелёные)

1. **C1 foundation:** PoseMath.ToSquare + TryElbowAngle + PoseFrame.Aspect; удаление
   LooksLikePushup из Process; тесты на MockPoseSource (регресс бокового пути).
2. **C2 tracker core:** OneEuroFilter + AmplitudeTracker (канал A, автомат, grace-латч);
   юнит-тесты на синтетических трассах (медленный/быстрый темп 1.5 повт/с, спайк 180,
   потеря трекинга у дна).
3. **C3 wiring + UX:** ctor-интеграция счётчика, тик-цепочка PushupSession, BottomTick +
   RejectBuzz, HUD gauge. MinRepSeconds → 0.30.
4. **C4 wrist scale:** WristAnchorMonitor S = max(torso, sw) + deadband; тесты фронтального
   джиттера (frac ≤ 0.03) и маха рукой (≥ 0.18).
5. **C5 view:** ViewClassifier + HUD-экспозиция; тесты: фронтальный мок → Frontal ≤ 1.5 с,
   боковой мок → Side, диагональ → Ambiguous, hip-флаппинг НЕ приводит к вечному Unknown.
6. **C6 armer:** PlankArmer F0–F6 (SetupGate, IMU, hip fail-closed); тесты: фронтальный мок
   армится ≤ 2.5 с; стол/стоя/колени — не армятся; алмаз армится.
7. **C7 rep gates:** FullRomGate v2 (ось/PCA/BodySwing) + HipDecoupling frontal + расширение
   RepSample; тесты: честный фронтальный повтор чисто; стоя-боб → BodySwing; наклонный →
   SupportGeometry/BodySwing.
8. **C8 anti-cheat:** KneeDropDetector + KneeCheatGate (Δ + κ-drift) + FootEventMonitor +
   SupportGeometryGate; тесты на MockFrontalKneePoseSource (чистый baseline → veto; отравленный
   baseline → документированный проход = принятый риск №1).
9. **C9 канал B + телеметрия:** BottomAlt-латч за флагом (включение после acceptance-записей),
   HUD-подсказка «разведите локти», телеметрия-флаги (ArmedWithoutLowerBodyEver, ReplaySuspect,
   decay-счётчик, forearm-collapse).

**Acceptance-записи для тюнинга порогов (обязательный список — все пороги сейчас калиброваны по
одному телу/стилю):** трицепсовый стиль, алмаз, широкий хват, женщина, подросток, темп ≥ 1.5
повт/с, наклон телефона 30°, дистанции 1.3 / 2.0 / 2.3 м.

### Семейство фронтальных моков — опорная таблица лендмарков

Модель: камера 0.30 м над полом, наклон +10°, vFOV 70° (f_y ≈ 0.71), hFOV 45° (f_x ≈ 1.2),
человек: нос 1.4–1.5 м, плечи 1.7 м, таз 2.3 м, колени 2.6 м, голеностопы 3.0 м.
`MockFrontalPushupPoseSource` интерполирует TOP↔BOTTOM косинусом, темп параметризуем; джиттер —
гаусс σ = 0.006 на все точки; vis — из таблицы с флаппингом ±0.15 на тазах/ногах.

| Landmark | TOP (x, y) | vis TOP | BOTTOM (x, y) | vis BOT |
|---|---|---|---|---|
| Nose | (0.50, 0.55) | 0.99 | (0.50, 0.68) | 0.99 |
| Shoulder L/R | (0.36 / 0.64, 0.54) | 0.95–0.99 | (0.35 / 0.65, 0.66) | 0.95–0.99 |
| Elbow L/R | (0.31 / 0.69, 0.66) | 0.90 | (0.24 / 0.76, 0.64) | 0.95 |
| Wrist L/R | (0.25 / 0.75, 0.77) | 0.85–0.95 | (0.25 / 0.75, 0.77) — неподвижны | 0.85–0.95 |
| Hip L/R | (0.41 / 0.59, 0.59) | 0.40–0.75 | (0.42 / 0.58, 0.64) | 0.40–0.75 |
| Knee L/R | (0.44 / 0.56, 0.64) | 0.20–0.60 | (0.44 / 0.56, 0.65) | 0.20–0.60 |
| Ankle L/R | (0.46 / 0.54, 0.66) | 0.10–0.40 | (0.46 / 0.54, 0.66) | 0.10–0.40 |

Производные для ассертов: sw 0.28 → 0.30 (+4–8%); shoulderMid Δy ≈ 0.12; torsoLen 0.05 → 0.02;
локоть 166° → 92°; κ_arm ≈ 0.18. Вариант «колено»: hipMid TOP (0.5, 0.64) → κ ≈ 0.36; колени при
опускании +0.045 norm по y относительно бедра (Δ ≈ +0.24·sw... порог 0.12 берётся с запасом ×2).
Вариант «стол»: запястья выше бёдер (P2 fail), widthRatio за повтор ≈ 1.3, travelFrac < 0.15.
Вариант «стоя-боб»: κ ≈ 1.2, ноги полностью видимы, приседной y-ход плеч ≈ 0.2.

---

## Константы

Все новые — в `CVConstants.cs` + зеркало в `docs/architecture/constants.md`.
**Неизменные анти-чит полы:** `TopElbowAngle = 160`, `BottomElbowAngle = 95`.
**Изменяемая:** `MinRepSeconds` **0.45 → 0.30** (см. обоснование в разделе латчей).

| Константа | Значение | Смысл |
|---|---|---|
| `ElbowFilterMinCutoffHz` | 2.5f | One-Euro срез в покое (поднят с 1.5 — быстрый темп) |
| `ElbowFilterBeta` | 0.05f | One-Euro Гц/(°/с) |
| `ElbowFilterDerivCutoffHz` | 1.0f | срез фильтра производной |
| `FilterDtClampMinSec` / `MaxSec` | 0.0167f / 0.10f | кламп dt |
| `ElbowSpikeClampDegPerFrame` | 40f | Hampel-кламп выброса |
| `TrackerRebaseAfterLostSec` | 0.5f | пере-сид фильтра + дуга в Idle |
| `ZoneLatchSec` | 0.07f | дебаунс латча (по таймстампам) |
| `ZoneDeepLatchMarginDeg` | 4f | однокадровый латч глубоко в зоне |
| `ZoneExitHysteresisDeg` | 6f | Enter→Exit гистерезис |
| `GraceLatchMaxGapSec` | 0.5f | ретро-латч Bottom при потере трекинга |
| `GraceLatchNearZoneDeg` | 3f | «был ≤ BottomEnter+3 перед провалом» |
| `AdaptiveZonesAffectLatch` | false | адаптация — только HUD в этом релизе |
| `AdaptiveMarginDeg` | 8f | HUD-адаптация: отступ от медианы |
| `AdaptiveMaxTightenTopDeg` / `BottomDeg` | 7f / 3f | асимметричный потолок ужесточения |
| `AdaptiveMinReps` / `WindowReps` | 3 / 5 | окно медиан |
| `AdaptiveDecayStepDeg` / `AfterMissedAttempts` | 3f / 2 | decay к абсолютному полу |
| `FailedAttemptBandDeg` | 10f | определение проваленной попытки (низ И верх) |
| `AmplitudeGaugeTopDeg` / `BottomDeg` | 175f / 75f | фиксированная шкала d01 |
| `BottomAltChannelEnabled` | false | канал B — после acceptance-записей |
| `BottomAltMaxElbowDeg` | 120f | канал B: потолок θm |
| `BottomAltShoulderDropFracSw` | 0.6f | канал B: ход плеч от TOP |
| `BottomAltNoseWristBandFracSw` | 0.15f | канал B: голова у линии ладоней |
| `BottomTickFreqHz` / `DurSec` | 1320f / 0.04f | нижний тик |
| `RejectBuzzFreqHz` / `DurSec` | 220f / 0.25f | buzz на veto |
| `ViewFrontalMaxRatio` / `SideMinRatio` | 0.7f / 1.6f | гистерезис R_med |
| `ViewMedianWindowFrames` | 9 | медиана R |
| `ViewSwitchVotes` / `ViewSwitchWindow` | 20 / 30 | «20 из 30 голосующих» |
| `ViewHipVoteVisibility` | 0.35f | порог vis таза для голоса (один таз достаточно) |
| `FrontalMaxBodyInclineKappa` | 0.35f | κ армирования (поднят с 0.28 — узкие плечи/tilt) |
| `FrontalMinBodyInclineKappa` | −0.35f | пик/«домик» |
| `KappaDriftSoftDock` / `HardVeto` | 0.08f / 0.15f | пер-реп κ-drift от baseline |
| `FrontalWristBelowShoulderFrac` | 0.4f | F1 |
| `FrontalElbowBelowShoulderFrac` | 0.25f | F1-фолбэк по локтям |
| `FrontalWristSpreadMinFrac` | 0.4f | F2 (двусторонний; 1.1 отменено — алмаз) |
| `FrontalNarrowGripWristDropFrac` | 0.6f | F2 узкая ветка |
| `FrontalNoseBetweenPalmsFrac` | 0.5f | F6 |
| `FrontalArmingHipAvailabilityMin` | 0.7f | hip fail-closed на армировании |
| `SetupMinShoulderWidthImg` / `MaxImg` | 0.17f / 0.38f | коридор дистанции ~1.3–2.3 м |
| `SetupMaxNoseY` | 0.85f | голова не у нижнего края |
| `SetupMaxPhonePitchDeg` | 30f | IMU-гейт наклона |
| `MinChestTravelFracHard` | 0.25f | FullRom v2: HardVeto ниже (конфликт 0.25/0.45 разрешён) |
| `MinChestTravelFracSoft` | 0.40f | FullRom v2: SoftDock в [0.25, 0.40) |
| `BodySwingWidthRatioMin` | 1.15f | BodySwing: рост ширины |
| `BodySwingMaxTravelFrac` | 0.30f | BodySwing: при y-ходе ниже — HardVeto |
| `WristDriftAbsDeadband` | 0.008f | абсолютный deadband дрейфа (norm, sq) |
| `KneeDropDeltaDisarm` / `Release` | 0.12f / 0.06f | S-KNEE-1 per-frame (лента 10 кадров) |
| `KneeDropDeltaHardVeto` / `SoftDock` | 0.15f / 0.10f | S-KNEE-1 per-rep |
| `FootVanishHighVis` / `LowVis` | 0.6f / 0.35f | S-KNEE-2 FootVanish |
| `FootDriftEventFrac` | 0.25f | S-KNEE-2 FootDrift (окно 12, отн. wristMid, / sw) |
| `SupportWristBelowShoulderFrac` | 0.15f | S-AIR-1 P1 |
| `SupportWristBelowHipFrac` | 0.15f | S-AIR-1 P2 |
| `SupportWristVsLegMarginFrac` | 0.10f | S-AIR-1 P3 |
| `FrontalMinHipShoulderCorr` | 0.45f | HipDecoupling frontal |
| `HipDropRatioMin` / `Max` | 0.15f / 1.1f | HipDecoupling frontal полоса |
| `KneeBendSideProjMinFrac` | 0.8f | view-гейт консюма KneeBendDetector (Side) |

`WristAnchorSoftFrac = 0.10` / `HardFrac = 0.20` — сохранены, но на новом масштабе
`S = max(torsoLen, shoulderWidth)` (sq).
