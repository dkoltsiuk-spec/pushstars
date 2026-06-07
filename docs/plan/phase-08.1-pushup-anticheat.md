# Phase 08.1 — Push-up Anti-Cheat

Под-фаза к phase-08 (MediaPipe rep counter). Не блокирует phase-09+, но **обязательна перед PvP-релизом** — без неё экономика XP/трофеев ломается.

---

## Цель и контекст

В phase-08 (`Assets/_Project/Scripts/CV/`) собран рабочий конвейер MediaPipe → `PoseFrame` → `PushupRepCounter` (FSM Top/Descending/Bottom/Ascending) → `FormScoreCalculator`. На реальном iPhone счёт настоящих отжиманий работает (TOP ~166°, BOTTOM ~92°), но **гейт перед FSM (`PoseMath.LooksLikePushup`, `Assets/_Project/Scripts/CV/PoseMath.cs`) слишком мягкий**: проверяется только видимость плеч/одной руки + опциональная нижняя часть (плэнк ≥140° при наличии лодыжек/коленей). На практике это означает: **лёжа на спине и качая руками — рэп засчитывается** (torso + arm видны, elbow swing проходит). Автор фазы 08 явно это пометил в memory как «известную слабость». Однорукие имитации, отжимания на коленях, «червяк» бёдрами, отжимания в воздухе сидя — всё это сегодня даёт reps.

**Что чиним в фазе 08.1**:
- Жёсткий predicate-gate перед FSM (PlankArmer) с явными причинами отказа.
- Per-rep аудит (chest travel, symmetry, hip decoupling, tempo, visibility).
- Per-frame anchor для запястий (image-space + body-frame fallback).
- Knee-vs-toe детектор как самостоятельный блок.
- Расширение `PoseFrame` для чтения `result.poseWorldLandmarks` — это **prerequisite** для S9/S10 и улучшает все signals, опирающиеся на body-relative геометрию.

**Что НЕ чиним здесь**:
- Видео-плейбек атак (`ScreenArtifactDetection`, moire/bezel/Z-flatness) — отдельная фаза 08.2.
- Серверная пост-валидация (replay сессии на бэке) — фаза PvP-anti-cheat.
- Распознавание упражнений отличных от push-up (squats, sit-ups) — другая фича.
- UI-полировка тостов «руки не там» — минимальный HUD-индикатор в этой фазе, дизайн полноценного coaching-overlay позже.

---

## Принципы

**1. Two-tier defense.** Каждый сигнал даёт один из трёх вердиктов:
- `HardVeto(reason)` — рэп **не засчитан**, FSM сбрасывает состояние, телеметрия пишет причину.
- `SoftDock(penalty 0..1, reason)` — рэп засчитан, но `FormScore` множится на `(1 - penalty)`. UI может показать «Форма: рука гуляет».
- `Pass` — без замечаний.

Hard veto только когда **физически нельзя** считать движение отжиманием (нет планки, нет ROM груди, колени согнуты). Всё остальное — soft dock, потому что precision важнее, но не за счёт frustration на legit-юзерах.

**2. Per-frame vs per-rep.** Per-frame сигналы дешёвые и работают как arming-gate (PlankArmer, Wrist-anchor, Knee-angle). Per-rep сигналы требуют буфер кадров от Top до возврата в Top — там живут Symmetry, Full-ROM, Hip-decoupling, Tempo, Window-visibility.

**3. Arm/disarm lifecycle.** Счётчик не работает «по дефолту». Юзер должен встать в валидную планку и продержаться `PlankArmHoldSec`. Один раз armed → считаем; потеря плэнка дольше `PlankDisarmGraceSec` → disarm, нужно re-arm. Это решает «лёжа на спине маханием» одним движением: тело **никогда не станет валидной планкой**, FSM просто не включится.

**4. World-landmarks-first.** Image-Y нестабильна из-за ориентации камеры (iPhone в landscape на полу, `WebCamPreview` вращает picture, но landmarks идут UN-rotated — см. `Assets/_Project/Scripts/CV/MediaPipe/MediaPipePoseSource.cs:OnPoseResult`). MediaPipe Pose Landmarker отдаёт `result.poseWorldLandmarks` (метры, hip-centered), но текущий адаптер их игнорирует. Расширение `PoseFrame` для их чтения — **prerequisite** этой фазы (~2-3h). Что world даёт: **метрические единицы** (пороги в сантиметрах вместо «доли тела»), **камеро-зум-инвариантность** (origin в midhip), **консистентный масштаб между кадрами**. Чего world НЕ даёт — см. ниже.

**Координатная безопасность (важно — гайды и доки расходятся).** Конвенция осей `poseWorldLandmarks` зависит от версии модели / pipeline (старый MediaPipe Pose Solution делал face-aligned crop → world был **частично body-aligned**; новый Tasks-API Pose Landmarker может отдавать **image-aligned** оси в метрах). **Поэтому код НЕ должен полагаться на конкретную ось как «вверх по гравитации»** — это верифицируется в Stage 0 пробником на устройстве (см. ниже), а сами сигналы пишутся convention-agnostic. Всё что мы гарантированно получаем от world: (1) метры; (2) origin в midhip; (3) consistent масштаб между кадрами. «Down toward floor» выводим из позы:

«Вниз к полу» выводим из самой позы — нормаль торса:

```
n_torso = normalize( cross(shoulderMid - hipMid, leftShoulder - rightShoulder) )
// У человека в планке n_torso выходит из груди и смотрит в пол.
// Знак нормализуем через dot(n_torso, anyDownHint), где anyDownHint =
// midHip → midFoot если ноги видны, иначе предыдущий стабильный n_torso.
```

Это работает в любом camera-frame и используется во ВСЕХ сигналах, где есть концепция «вниз/вверх» (S1, S5, S6, S9, S10). Конкретно:
- **S5 (Full-ROM)**: chest travel = проекция движения midShoulder на `n_torso` за окно репа. Метры через world даёт абсолютный порог (~10cm); без world — fraction of `||shoulderMid - hipMid||`.
- **S9 (Support-Plane Coplanarity)**: фит плоскости по 4 опорным точкам, нормаль плоскости сравнивается с `-n_torso` (должны совпадать в плэнке).
- **S10 (Body-Horizontal)**: проверка что body axis (shoulderMid→hipMid) перпендикулярен `n_torso` И что `n_torso` стабилен между кадрами (если торс крутится в пространстве — это не плэнк).

Что НЕ делать: НЕ инвертировать оси world в адаптере «чтобы Y стал гравитацией». Это сломает downstream и создаст false mental model. Храним world as-is.

**Stage 0 axis probe (обязательно).** Перед началом Stage 1 — добавить в `PushupDebugHud` строку с `World(LeftWrist).x/y/z` и `World(LeftShoulder).x/y/z` (когда `HasWorldLandmarks=true`). На устройстве встать в плэнк, прочитать значения. Если world-Y запястий монотонно отличается от world-Y плеч на ~30-50см — оси частично body-aligned (старая конвенция). Если world-Y близок к нулю и значимое отличие в world-X или world-Z — оси image-aligned (новая конвенция). Цифры фиксируются в этом доке и в memory; downstream код остаётся agnostic, но мы знаем что показывать в дебаг-логах.

---

## Архитектура

```
Assets/_Project/Scripts/CV/AntiCheat/
├── IRepValidator.cs             # interface RepVote Validate(in RepWindow w)
├── RepVote.cs                   # struct {HardVeto, SoftDock(penalty), Pass} + reason
├── RepWindow.cs                 # struct {ReadOnlySpan<RepSample> Samples, Duration, BottomDwell, ...}
├── RepSample.cs                 # per-frame snapshot: t, elbows L/R, shoulderMid/hipMid img+body, vis
├── AntiCheatAuditor.cs          # owns validators list, ring-buffer RepSample[256], event OnRepVerdict
├── PlankArmer.cs                # per-frame FSM Disarmed/Arming/Armed/Cooling, IsValidPlank
├── PlankRejectReason.cs         # enum NoLowerBody, NoKnees, BodySagging, KneesBent, NotAtTop, WristsNotPlanted
├── WristAnchorMonitor.cs        # per-frame sliding window drift detector
├── KneeBendDetector.cs          # per-frame + per-rep knee angle audit
├── BilateralSymmetryGate.cs     # per-rep IRepValidator
├── FullRomGate.cs               # per-rep IRepValidator (chest/shoulder travel)
├── HipDecouplingGate.cs         # per-rep IRepValidator (shoulder vs hip correlation)
├── TempoSanityGate.cs           # per-rep IRepValidator (upper duration + bottom dwell)
├── RepVisibilityGate.cs         # per-rep IRepValidator (windowed avg key-joint vis)
├── SupportPlaneCoplanarityGate.cs  # per-rep, world-only (skipped if !HasWorldLandmarks)
└── BodyHorizontalGate.cs        # per-frame, world-only, fed into PlankArmer
```

```
Assets/_Project/Scripts/CV/Util/
└── RingBuffer.cs                # zero-alloc fixed-capacity ring (replaces Queue<T> in hot paths)
```

**Class responsibilities**:
- `PushupSession` (modified) — на каждый `OnFrame` зовёт `_armer.Tick(frame, now)` ДО `_counter.Process(frame, trackingOk, _armer.IsArmed)`. Подписан на `_counter.OnRepCandidate(window)` → дёргает `_auditor.Audit(window)` → либо `_counter.AcceptRep()` либо `_counter.RejectRep(reason)`.
- `PlankArmer` — FSM, ничего не считает, только переключает `IsArmed`. Внутри использует `WristAnchorMonitor` и `KneeBendDetector` как зависимости.
- `AntiCheatAuditor` — owner ring-буфера `RepSample[256]` (8 сек @30fps). Заполняется из `PushupRepCounter` через метод `RecordSample(...)` на каждом кадре между Top и Top. На `Audit(window)` прогоняет все зарегистрированные `IRepValidator` в фиксированном порядке (hard-первые), агрегирует `RepVote`.
- `PushupRepCounter` (modified) — убираем вызов `PoseMath.LooksLikePushup` (gating переходит в PlankArmer). Добавляем `event Action<RepWindow> OnRepCandidate` вместо прямого инкремента в `CreditRep`. Финальный коммит rep делается извне после аудита.

**Threading**: всё работает на main thread, после того как `MediaPipePoseSource.Update()` поднял буфер из callback-lock. Все ring-буферы пред-аллоцированы в конструкторах, никаких `new` per-frame. Аллокация одного `RepSample[]` snapshot на rep (~раз в секунду) приемлема — это не hot path.

**Slot-in pipeline**:
```
PushupSession.HandleFrame(frame)
  └─ _armer.Tick(frame, now)
       └─ updates _armer.IsArmed (Disarmed/Arming/Armed/Cooling)
  └─ _counter.Process(frame, trackingOk, _armer.IsArmed)
       ├─ if !IsArmed → clear state, return
       ├─ _auditor.RecordSample(frame, _counter.Phase)
       └─ on Top→Bottom→Top arc → _counter.OnRepCandidate(window)
  └─ subscribed: _auditor.Audit(window) → RepVerdict
       ├─ Accepted  → _counter.AcceptRep() → OnRep(reps)
       └─ Rejected  → _counter.RejectRep(reason) → OnRepRejected(reason)
```

---

## Расширение PoseFrame для world landmarks

**Prerequisite**, делается ОТДЕЛЬНЫМ коммитом перед anti-cheat работой.

### Изменения

**`Assets/_Project/Scripts/CV/PoseFrame.cs`** — добавить параллельный массив. **Y НЕ инвертируем** — храним ровно как отдаёт MediaPipe (body-frame, +Y от головы к ногам). Любая инверсия в адаптере создаёт ложное ощущение «теперь +Y вверх по гравитации» и провоцирует системные ошибки в downstream-сигналах.

```csharp
public readonly struct PoseFrame
{
    public readonly Landmark[] Landmarks;        // image-space, нормализованные [0,1], origin top-left, Y down
    public readonly Landmark[] WorldLandmarks;   // body-frame, метры, hip-centered, +Y от головы к ногам. null если backend не отдал.
    public readonly float TimestampSec;

    public bool HasWorldLandmarks =>
        WorldLandmarks != null && WorldLandmarks.Length == PoseLandmarks.Count;

    public Landmark World(PoseLandmark id) => WorldLandmarks[(int)id];

    public PoseFrame(Landmark[] image, Landmark[] world, float timestamp)
    {
        Landmarks = image;
        WorldLandmarks = world;
        TimestampSec = timestamp;
    }
}
```

**`Assets/_Project/Scripts/CV/MediaPipe/MediaPipePoseSource.cs`** — патч `OnPoseResult`:

```csharp
private void OnPoseResult(PoseLandmarkerResult result, Image image, long timestamp)
{
    Landmark[] arr = null;
    Landmark[] worldArr = null;

    var poses = result.poseLandmarks;
    if (poses != null && poses.Count > 0)
    {
        var lms = poses[0].landmarks;
        if (lms != null && lms.Count >= PoseLandmarks.Count)
        {
            arr = new Landmark[PoseLandmarks.Count];
            for (int i = 0; i < PoseLandmarks.Count; i++)
            {
                var lm = lms[i];
                float vis = lm.visibility ?? lm.presence ?? 1f;
                arr[i] = new Landmark(lm.x, lm.y, lm.z, vis);
            }
        }
    }

    // NEW: дочитываем world landmarks. Y НЕ инвертируем — храним body-frame as-is.
    var worldPoses = result.poseWorldLandmarks;
    if (worldPoses != null && worldPoses.Count > 0)
    {
        var wlms = worldPoses[0].landmarks;
        if (wlms != null && wlms.Count >= PoseLandmarks.Count)
        {
            worldArr = new Landmark[PoseLandmarks.Count];
            for (int i = 0; i < PoseLandmarks.Count; i++)
            {
                var w = wlms[i];
                float vis = w.visibility ?? w.presence ?? 1f;
                worldArr[i] = new Landmark(w.x, w.y, w.z, vis);
            }
        }
    }

    lock (_gate) { _pending = arr; _pendingWorld = worldArr; _pendingTime = timestamp / 1000f; _hasPending = true; }
}
```

Соответственно `_pendingWorld` поле + поднимать в `Update()` парой с `_pending` в `new PoseFrame(arr, worldArr, t)`.

**`Assets/_Project/Scripts/CV/MockPoseSource.cs`** — синтезировать body-frame для perfect-plank сценария (тестам нужно покрыть code path с `HasWorldLandmarks=true`):

```csharp
// Body-frame plank: hip в origin, плечи в -Y body (к голове), стопы в +Y body (к ногам).
// X-axis через плечи, Z вперёд от субъекта.
// Body scale ~0.5m (длина hip→shoulder). Plank плоский, Z ≈ 0.
worldArr[(int)PoseLandmark.LeftHip]       = new Landmark(-0.10f,  0.00f, 0f, 0.95f);
worldArr[(int)PoseLandmark.RightHip]      = new Landmark(+0.10f,  0.00f, 0f, 0.95f);
worldArr[(int)PoseLandmark.LeftShoulder]  = new Landmark(-0.18f, -0.50f, 0f, 0.95f);
worldArr[(int)PoseLandmark.RightShoulder] = new Landmark(+0.18f, -0.50f, 0f, 0.95f);
worldArr[(int)PoseLandmark.LeftElbow]     = new Landmark(-0.20f, -0.50f, 0.30f, 0.90f); // вперёд по +Z, опираясь
worldArr[(int)PoseLandmark.RightElbow]    = new Landmark(+0.20f, -0.50f, 0.30f, 0.90f);
worldArr[(int)PoseLandmark.LeftWrist]     = new Landmark(-0.20f, -0.50f, 0.60f, 0.90f);
worldArr[(int)PoseLandmark.RightWrist]    = new Landmark(+0.20f, -0.50f, 0.60f, 0.90f);
worldArr[(int)PoseLandmark.LeftKnee]      = new Landmark(-0.10f, +0.40f, 0f, 0.85f);
worldArr[(int)PoseLandmark.RightKnee]     = new Landmark(+0.10f, +0.40f, 0f, 0.85f);
worldArr[(int)PoseLandmark.LeftAnkle]     = new Landmark(-0.10f, +0.80f, 0f, 0.80f);
worldArr[(int)PoseLandmark.RightAnkle]    = new Landmark(+0.10f, +0.80f, 0f, 0.80f);
worldArr[(int)PoseLandmark.LeftFootIndex] = new Landmark(-0.10f, +0.85f, 0.05f, 0.75f);
worldArr[(int)PoseLandmark.RightFootIndex]= new Landmark(+0.10f, +0.85f, 0.05f, 0.75f);
// нос/глаза/уши — приближения от LeftShoulder/RightShoulder, не критичны для anti-cheat
```

Анимация push-up в Mock: меняем только image-space elbow angles + опционально немного «качаем» Z запястий/локтей в body-frame, чтобы FullRomGate не получал нулевой chest travel в image-fallback режиме. Mock остаётся perfect-plank эталоном — должен проходить ВСЕ gates без штрафа.

**`Assets/_Project/Scripts/CV/IPoseSource.cs`** — флаг capability:

```csharp
public interface IPoseSource
{
    // ... existing ...
    bool ProvidesWorldLandmarks { get; }  // MediaPipe=true, Mock=true (после расширения), будущие — могут быть false
}
```

---

## Сигналы

### S1: SUPPORT-ANCHOR (запястья стоят на полу)

- **Что детектит**: руки не «висят в воздухе» — запястья закреплены в одной точке между Bottom-фазой и следующим Top. Ловит «качание руками в воздухе сидя/стоя/лёжа на спине».
- **Входы**: `frame.World(LeftWrist).Pos`, `frame.World(RightWrist).Pos`. Fallback image-space: `frame.Get(LeftWrist).Pos2D`, нормированные на `||shoulderMid - hipMid||_img`.
- **Формула**: sliding window `RingBuffer<Vector3>` ёмкостью `WristDriftWindowFrames=12` (≈400ms @30fps). На каждом кадре с `Visibility ≥ 0.5`:
  ```
  body_scale = |World(LeftHip) - World(LeftShoulder)|   // в метрах, наш масштаб
  medianL = componentwise_median(buffer_L)
  driftL  = sqrt( mean( |buffer_L[i] - medianL|^2 ) )
  driftL_frac = driftL / body_scale
  ```
  Берём `max(driftL_frac, driftR_frac)` среди видимых запястий.
- **Пороги**:
  - `< 0.10` → anchored, Pass
  - `0.10..0.20` → drifting, SoftDock(0.3) с reason `"WristDrift"`
  - `≥ 0.20` → airborne, HardVeto(`"WristsAirborne"`)
  - Image-space fallback: те же пороги, `body_scale = |shoulderMid - hipMid|_img`.
- **Где живёт**: `WristAnchorMonitor.cs`. Per-frame в `Tick(frame)`. Состояние `AnchorVerdict LastVerdict { get; }` читает PlankArmer (для arming) и AntiCheatAuditor (для per-rep dock).
- **Гистерезис/окно**: 12-frame sliding window. **Grace 45 кадров (1.5s) после `CreditRep`**: буфер `Clear()`, не оцениваем drift первые 1.5с — юзер может поправить руки. **Grace 30 кадров (1s) после `OnArmed`** — даём встать в позу.
- **Failure modes**:
  - Дальнее запястье за корпусом (side camera) → `Visibility<0.5` устойчиво → не учитываем эту сторону. Если обе вечно `<0.5` → verdict `Unknown`, **не блокируем**.
  - Кулаки/костяшки → запястье на 5-8см выше пола, при опускании опрокидывается → drift до 0.10. Порог 0.10 (а не 0.08 как в исходном предложении) — компромисс под кулаки.
  - World недоступен → image-space fallback, те же пороги; вычитываем известный риск нестабильности при движении камеры.
- **Test plan**: `MockArmWavingPoseSource` — стоячий, плечи статичны на body-Y, запястья синусоидально качаются с амплитудой 0.3м. Ожидание: `Reps == 0`, `LastVerdict == Airborne` устойчиво.

---

### S2: KNEE-DETECT (отжимания не с колен)

- **Что детектит**: knee push-ups. Hard constraint пользователя: «cannot count knee push-ups (no effort)».
- **Входы**: `frame.Get(LeftHip)`, `frame.Get(LeftKnee)`, `frame.Get(LeftAnkle)` (и зеркально). Угол **инвариантен к флипам** — можно считать в image-space; в world (если есть) точнее, без перспективы.
- **Формула**:
  ```csharp
  float SideKneeAngle(side) {
    if all three visible (>= MinJointVisibility)
      return PoseMath.AngleDeg(hip.Pos2D, knee.Pos2D, ankle.Pos2D);
    return NaN;
  }
  // Возвращаем min среди видимых — атакуем худшую ногу
  minKnee = min(notNaN(left, right))
  ```
  Если world доступен — считаем в 3D-векторах из `World(...)`, иначе image-space.
- **Пороги**:
  - `MinKneeAngleDeg = 145°` (понижено с 150° — учёт adversarial критики по «мягкому колену новичка» + перспективе в image-space)
  - Гистерезис `KneeHysteresisDeg = 15°` — лента 130..160 для frame-классификации Bent/Straight
  - Per-rep: использовать **5-й процентиль** угла за rep-window, не absolute min (защита от одиночных шумовых кадров)
  - Если оба ankle.Visibility < 0.3 → fallback на shoulder-hip-knee body-line с порогом `StrictKneeBodyLineDeg = 160°`
- **Где живёт**: `KneeBendDetector.cs`. Двойная роль:
  1. Per-frame в PlankArmer: если 5 кадров подряд `Bent` → reason `KneesBent`, disarm/refuse arm.
  2. Per-rep аудит: pre-allocated buffer knee angles за rep window → `p5_knee < 145°` → `HardVeto("KneePushup")`.
- **Гистерезис/окно**: лента 130..160. Для frame-flicker — требуем 5 кадров подряд Bent для срабатывания. Per-rep — статистика по всему окну.
- **Failure modes**:
  - Мешковатые штаны → `Visibility(knee)<0.5` → NaN → fallback на body-line ≥160°. **Не требуем оба колена видимыми для armings** (ослабление vs. исходного предложения PlankArmer): достаточно одной видимой ноги.
  - Image-space перспектива → честные 175° могут спроецироваться в 155° под острым углом → порог 145° даёт запас.
  - Knee-pushup со «стопами в воздухе» (стопы выше колена в гравитации) → проверка hip-knee-ankle = 175°, проходит. **Закрывается S9** (SupportPlaneCoplanarity) когда world доступен; без world остаётся дыра, документируем как известную для MVP.
- **Test plan**: `MockKneePushupPoseSource` — hip(0.65, 0.58) → knee(0.78, 0.73, на «полу») → ankle(0.82, 0.60, голень вверх). Угол ≈85-95°. Ожидание: PlankArmer не вооружается, `Reps == 0`.

---

### S3: PLANK-ARMER (вооружение счётчика)

- **Что детектит**: композитный gate. Счётчик не считает, пока юзер не продемонстрировал валидную планку `PlankArmHoldSec` подряд. Решает core-issue фазы 08: «лёжа на спине маханием» и любой not-a-plank сценарий просто не вооружают FSM.
- **Входы**: `PoseFrame` (image + world если есть), результат `KneeBendDetector`, результат `WristAnchorMonitor`.
- **Формула** — predicate `IsValidPlank(frame, out PlankRejectReason reason)`:
  1. Нижняя часть тела видна: **хотя бы одна** из {LeftAnkle, RightAnkle, LeftFootIndex, RightFootIndex} с `vis ≥ 0.7` **или** хотя бы один knee с `vis ≥ 0.7`. (Ослабление vs. строгого «обе ankle + обе knee» из исходного предложения — учёт side-camera + мешковатой одежды.)
  2. `PoseMath.PlankBodyLine(frame) >= ArmingBodyLineAngle = 160°` (строже текущего MinPlankBodyLine=140°).
  3. `KneeBendDetector.Classify ≠ Bent` (по 5-кадровой ленте).
  4. `PoseMath.ElbowAngle(frame) >= ArmingElbowTopAngle = 150°` (юзер в Top, не в середине отжимания).
  5. `WristAnchorMonitor.LastVerdict ∈ {Anchored, Drifting, Unknown}` (только не `Airborne`).
  6. **Опционально, только если `frame.HasWorldLandmarks`**: S10 BODY-HORIZONTAL (см. ниже) проходит. Если world нет — этот пункт скипается.
- **FSM**:
  ```
  Disarmed → Arming     : IsValidPlank становится true
  Arming   → Armed      : валидно непрерывно PlankArmHoldSec (default 1.0s)
  Arming   → Disarmed   : валидность сломалась
  Armed    → Cooling    : валидность сломалась
  Cooling  → Armed      : валидность вернулась
  Cooling  → Disarmed   : невалидно непрерывно PlankDisarmGraceSec (default 2.5s, повышено с 1.5s — учёт ad-критики P1 «глюк скелета на дне»)
  ```
  `IsArmed = (State == Armed || State == Cooling)`.
- **Пороги/константы** (стартовые, под калибровку из телеметрии):
  - `PlankArmHoldSec = 1.0f`
  - `PlankDisarmGraceSec = 2.5f`
  - `ArmingBodyLineAngle = 160f`
  - `ArmingElbowTopAngle = 150f`
- **Где живёт**: `PlankArmer.cs` с `IPlankArmer` интерфейсом. События `OnArmed`, `OnDisarmed(PlankRejectReason)`, свойства `State`, `IsArmed`, `ArmingProgress01`, `LastRejectReason`.
- **Failure modes**:
  - Glitch скелета на дне → Cooling до 2.5с, обычно возвращается → не теряем сессию.
  - Юзер делает 30 fast reps и в одном поправил руку → Cooling, через 200мс возврат в Armed, счёт не прерывается.
  - World недоступен → пункт 6 (S10) скипается, остаётся 5 пунктов — достаточно жёстко.
  - **Не закрывается**: «лежу на спине, но bodyLine=180°, knee=180°, ankle видна, ладони у пола». Пункт 5 (WristAnchor) ловит это в world-mode; в image-fallback — частично. Документируем как acceptable risk MVP.
- **Test plan**:
  - `MockPoseSource` (perfect plank) → `OnArmed` через 1.0с.
  - `MockArmWavingPoseSource` (стоя) → `Disarmed` навсегда, reason `WristsNotPlanted`.
  - `MockKneePushupPoseSource` → `Disarmed`, reason `KneesBent`.
  - `MockHipThrustPoseSource` (буква «∧», bodyLine=120°) → `Disarmed`, reason `BodySagging`.
  - Scripted: 2с plank → arm → 0.5с lost ankle → Cooling → 0.5с возврат → Armed. `Reps` не сбрасывается.

---

### S4: BILATERAL-SYMMETRY (обе руки работают)

- **Что детектит**: однорукие имитации (одна рука держит планку, другая «качает»), несбалансированные движения.
- **Входы**: `PoseMath.LeftElbowAngle(frame)`, `PoseMath.RightElbowAngle(frame)` — НОВЫЕ методы, split существующего `ElbowAngle`. Per-rep буфер.
- **Формула** в `RepWindow`:
  ```
  L_ROM = max(leftElbow) - min(leftElbow)
  R_ROM = max(rightElbow) - min(rightElbow)
  amplitude_ratio = min(L_ROM, R_ROM) / max(L_ROM, R_ROM)
  mean_abs_diff = mean( |leftElbow(t) - rightElbow(t)| )

  leftVisFrac  = (#frames с vis(L_shoulder, L_elbow, L_wrist) all >= 0.5) / total
  rightVisFrac = аналогично
  bothVisible = leftVisFrac >= 0.75 AND rightVisFrac >= 0.75
  ```
- **Пороги**:
  - `SymmetryAmplitudeMin = 0.5` — bothVisible && ratio<0.5 → `HardVeto("Asymmetric")`
  - `SymmetryMeanDiffMax = 25°` (повышено с 20° — учёт ad-критики, истинная асимметрия у юзеров) → SoftDock(0.2)
  - **Skip-правило**: если `min(leftVisFrac, rightVisFrac) < 0.6` И `|leftVisFrac - rightVisFrac| > 0.4` → side-camera mode, не голосуем (`Pass`).
- **Где живёт**: `BilateralSymmetryGate.cs : IRepValidator`.
- **Failure modes**: side camera → skip; реабилитация после травмы → жёсткий ratio<0.5 покрывает; шум landmark → медианная фильтрация по 3 кадрам перед min/max.
- **Test plan**: `MockOneArmPushupPoseSource` (L качается 90↔170, R фиксирован 170) → HardVeto. `MockPoseSource` (симметричный) → Pass.

---

### S5: FULL-ROM (грудь действительно опускается)

- **Что детектит**: «wrist-only fake» — локти качаются, корпус не движется. Самый мощный сигнал после plank-arming.
- **Входы**:
  - World mode: НЕ используем `World(Shoulder).Y` напрямую — body-frame жёстко привязан к торсу, shoulder.Y относительно hip константа. Используем **глобальную normalize-projection**: shoulder в image-space, проецируем смещение на `g_body = normalize(shoulderMid_img - hipMid_img)`, усреднённый по Top-фазе.
  - Image-space (primary path в фазе 08.1): `shoulderMid_img(t)`, `hipMid_img(t)`, `g_body` зафиксированный в момент входа в Descending.
- **Формула**:
  ```
  // g_body захватываем при переходе Top→Descending, усредняя по 5 последним Top-кадрам
  g_body = mean over last 5 Top frames of normalize(shoulderMid_img - hipMid_img)
  shoulderHipDist0 = mean |shoulderMid_img - hipMid_img| over those frames

  // На каждом sample в RepWindow:
  travel(t) = dot(shoulderMid_img(t) - shoulderMid_img(t_top), g_body)
  travelFrac = (max(travel) - min(travel)) / shoulderHipDist0
  ```
- **Пороги**:
  - `ShoulderTravelMinFrac = 0.12` (примерно 12% длины торса; для торса 50см → 6см, для торса 30см → 3.6см — body-relative, работает для детей и взрослых)
  - `< 0.12` → `HardVeto("InsufficientROM")`
  - **Floor absolute**: если `shoulderHipDist0 < 0.06` (в image-space, человек далеко от камеры) → не голосуем, доверяемся elbow FSM.
  - Median filter по 3 кадрам на `shoulderMid_img` перед max/min — защита от outlier'ов.
- **Где живёт**: `FullRomGate.cs : IRepValidator`.
- **Failure modes**:
  - Камера движется → `shoulderMid_img` смещается несинхронно с реальным движением. Опционально (фаза 08.2): `CameraStabilityCheck` через diff фона. В MVP принимаем риск.
  - Дыхание добавляет шум 1-3см к shoulderMid_img → detrend через скользящее среднее на длину дыхательного цикла (~1.5с) перед взятием max/min.
  - Маленький человек далеко от камеры → skip по floor absolute.
- **Test plan**: `MockWallPushupPoseSource` (стоячий fake, shoulderMid_img фиксирован, elbows качаются) → HardVeto. Перебалансированный `MockPoseSource` → Pass.

---

### S6: HIP-DECOUPLING (нет «червяка» бёдрами)

- **Что детектит**: cheat «inchworm» — таз волнообразно качается, плечи не двигаются; либо hip-thrust — таз падает сильнее плеч.
- **Входы**: image-space `shoulderMid_img(t)`, `hipMid_img(t)`, обе проекции на `g_body` (тот же из S5).
- **Формула**:
  ```
  shoulderProj(t) = dot(shoulderMid_img(t) - shoulderMid_img(t_top), g_body)
  hipProj(t)      = dot(hipMid_img(t)      - hipMid_img(t_top),      g_body)

  // Robust Pearson после detrend (выкидываем линейный тренд за окно):
  corr = pearson(detrend(shoulderProj), detrend(hipProj))
  hipTravel      = p95(|hipProj|) - p5(|hipProj|)
  shoulderTravel = p95(|shoulderProj|) - p5(|shoulderProj|)
  hipShoulderRatio = hipTravel / max(shoulderTravel, 1e-3)
  ```
- **Пороги**:
  - `HipShoulderCorrMin = 0.5` (понижено с 0.6 — учёт дыхания и усталости)
  - `HipTravelMaxRatio = 1.8` (повышено с 1.5 — у толстых пользователей таз гуляет от дыхания)
  - HardVeto: `corr < 0.3 AND hipShoulderRatio > 1.8`
  - SoftDock(0.15): `corr < 0.5` или `hipShoulderRatio > 1.3`
  - Skip: `shoulderTravel < 0.05` (S5 уже зарежет, нам тут нечего сказать).
- **Где живёт**: `HipDecouplingGate.cs : IRepValidator`.
- **Failure modes**: усталость → SoftDock корректен. Низкая видимость бёдер → skip. Дыхание → detrend.
- **Test plan**: `MockHipThrustPoseSource` (taз делает волну, плечи неподвижны) → HardVeto. Усталый прогиб → SoftDock.

---

### S7: TEMPO-SANITY (верхняя граница длительности)

- **Что детектит**: длинные паузы в нижней точке (отдых посередине rep), нереально медленные движения.
- **Входы**: `RepWindow.DurationSec`, `RepWindow.BottomDwellSec`.
- **Формула**: тривиально, два числовых сравнения. Реализовано прямо в `TempoSanityGate.Validate` (2 строки, но отдельный validator для consistency).
- **Пороги** (повышены vs. исходного предложения после ad-критики «pause push-ups»):
  - `MaxRepSeconds = 12.0f` (вместо 8 — учёт negative-reps + контрольного темпа)
  - `MaxBottomDwellSeconds = 5.0f` (вместо 3 — pause-pushups легитимны)
  - HardVeto: `Duration > 12s` → `"RepTooSlow"`
  - SoftDock(0.1): `BottomDwell > 5s` → `"BottomDwellLong"`
- **Где живёт**: `TempoSanityGate.cs : IRepValidator`.
- **Failure modes**: камера тротлит → используем `frame.TimestampSec` (монотонный из MediaPipe), не `Time.time`.
- **Test plan**: `MockSlowRepPoseSource` (10с/rep, в пределах 12) → Pass; 15с/rep → HardVeto.

---

### S8: REP-WINDOW-VISIBILITY (скелет был надёжен)

- **Что детектит**: rep, где половину окна скелет был галлюцинирован.
- **Входы**: per-frame `visibility` ключевых суставов в `RepWindow`.
- **Формула**:
  ```
  // Парами max(L, R) — side camera friendly:
  pair_vis(t) = mean over pairs of max(vis(L), vis(R))
                pairs = {Shoulder, Elbow, Wrist, Hip}
  // Winsorized mean — выкидываем нижние 15% кадров перед усреднением (защита от 1-2 плохих кадров):
  windowAvgVis = winsorized_mean(pair_vis over window, trim=0.15)
  ```
- **Пороги**:
  - `RepWindowVisMin = 0.55` (понижено с 0.6 — после winsorize порог можно мягче, потому что выбросы уже убраны)
  - HardVeto: `windowAvgVis < 0.55` → `"VisibilityTooLow"`
- **Где живёт**: `RepVisibilityGate.cs : IRepValidator`. Делит logic с `PoseQuality.cs:Classify` — выносим pair-max helper в `PoseQuality.PairMaxVisibility(frame)`.
- **Failure modes**: side camera покрывается pair-max. Очень длинный glitch (>40% окна) — корректно зарежем.
- **Test plan**: `MockLowVisPoseSource` (все vis=0.5 константно) → borderline pass; 0.45 → veto.

---

### S9: SUPPORT-PLANE-COPLANARITY (опоры в одной плоскости, world-only)

- **Что детектит**: «incline cheat» (одна рука на скамье), knee push-up со стопами в воздухе.
- **Входы**: world. `LeftWrist`, `RightWrist`, `LeftAnkle`, `RightAnkle`, `LeftFootIndex`, `RightFootIndex`, `LeftShoulder`, `RightShoulder`, `LeftHip`, `RightHip`.
- **Формула** — единственный корректный «gravity in body-frame»:
  ```
  // Восстанавливаем нормаль торса к плоскости спины:
  spine_axis = World(hipMid) - World(shoulderMid)
  shoulder_axis = World(RightShoulder) - World(LeftShoulder)
  n_torso = normalize( cross(shoulder_axis, spine_axis) )

  // Нормализуем знак: n должен смотреть в ту же сторону, что hip→knee (т.е. вниз к полу для plank):
  if dot(n_torso, World(LeftKnee) - World(LeftHip)) < 0 → n_torso = -n_torso

  // Проецируем опоры на n_torso:
  support_points = { World(LeftWrist), World(RightWrist),
                     World(LeftFootIndex), World(RightFootIndex) } // FootIndex приоритет над Ankle (ближе к полу)
  // Фильтруем по visibility >= 0.5
  depths = { dot(p - World(hipMid), n_torso) for p in support_points }
  spread = max(depths) - min(depths)   // метры

  body_scale = |spine_axis|
  spread_frac = spread / body_scale
  ```
  - Считается **один раз в момент Bottom** (геометрия стабильнее), не per-frame.
  - `n_torso` усредняется по 5 последним Top-кадрам перед rep — анти-шум.
- **Пороги** (body-relative — иммунитет к росту юзера):
  - `SupportPlaneSpreadSoftFrac = 0.20` (≈10см для торса 50см) → SoftDock(0.2)
  - `SupportPlaneSpreadHardFrac = 0.50` (≈25см) → `HardVeto("SupportNotCoplanar")`
  - Skip: если меньше 3 опорных точек видимы → Pass.
- **Где живёт**: `SupportPlaneCoplanarityGate.cs : IRepValidator`. **Skip с reason `"no world landmarks"` если `!HasWorldLandmarks`.**
- **Failure modes**:
  - Кулаки vs носки: wrist landmark на 5см выше пола, ankle на 2-3см — spread ≈3см, ниже soft порога 10см. OK.
  - Knee push-up: ankle торчит вверх, footIndex тоже выше колен → spread >25см → HardVeto. **Бонус: дублирует S2 KneeDetect для случая «прямая нога с поднятой стопой»** (который S2 пропускает).
  - World недоступен → skip → MVP принимает что incline cheat не ловится.
- **Test plan**: `MockKneePushupPoseSource` (ankles в +Y body выше колен) → HardVeto. `MockInclineCheatPoseSource` (RightWrist на 0.25м ниже LeftWrist по n_torso) → HardVeto. Perfect mock → spread ≈3см → Pass.

---

### S10: BODY-HORIZONTAL (тело параллельно полу, world-only)

- **Что детектит**: «лежу на спине» сценарий, когда `LooksLikePushup` мягкий и не блокирует. Closes остаток дыры в S3 PlankArmer.
- **Входы**: world. `n_torso` (из S9-логики), плюс **гравитация из акселерометра** `Input.acceleration` (нормализованный, в frame устройства). Преобразуем гравитацию в body-frame через ориентацию — **но** для нашего use case достаточно простой проверки: в honest plank нормаль торса параллельна gravity (оба смотрят в пол).
- **Формула**:
  ```
  // n_torso в body-frame; нам надо знать его направление относительно реальной гравитации.
  // Хак: используем тот факт, что в device frame gravity_device = Input.acceleration.
  // Преобразование body→device нетривиально без AR, поэтому используем proxy:
  // если устройство в landscape и юзер в plank, то image-Y направление пола можно вычислить
  // через положение feet vs head в image-space:
  feet_img_y = mean(LeftAnkle.Y, RightAnkle.Y)  // image-space, [0,1]
  head_img_y = Nose.Y
  // Это даёт нам ось «head→feet в image». В honest plank она примерно параллельна
  // короткой оси кадра (горизонтальная line поперёк изображения).
  body_axis_img = (footMid_img - shoulderMid_img)   // 2D вектор
  // Проверка: эта ось должна быть достаточно «длинной» (тело растянуто, не свёрнуто):
  body_length_norm = |body_axis_img| / max(image_diagonal, 1e-3)
  ```
- **Пороги**:
  - `body_length_norm >= 0.35` — тело занимает ≥35% диагонали кадра → в плане.
  - Если меньше → SoftReject (не армится плэнк, причина `BodyNotExtended`).
  - НЕ HardVeto на готовый rep — это часть PlankArmer.
- **Где живёт**: `BodyHorizontalGate.cs`. Не `IRepValidator`, а helper для PlankArmer.IsValidPlank (пункт 6).
- **Failure modes**:
  - Очень близкая камера → body_length_norm может быть >0.5 даже для свёрнутого человека → ослабляет detect. Принимаем (близкая камера для push-up неоптимальна, юзер сам найдёт).
  - Совсем без world — skip, PlankArmer.IsValidPlank пункт 6 пропускается, остаются 5 пунктов.
- **Test plan**: `MockLyingOnBackPoseSource` (body horizontal in image but в world нос и стопы на одной line вдоль gravity) → required: добавить acceleration mock в тесте. Этот test может быть deferred в фазу 08.2 если расход времени.

---

## Rep-verdict pipeline (алгоритм)

```
1. Per-frame loop в PushupSession.HandleFrame(frame):
   a. _armer.Tick(frame, now)
      ├─ _wristAnchor.Tick(frame)   // обновляет verdict
      ├─ _kneeBend.Tick(frame)      // обновляет classification
      └─ FSM transitions (см. S3)

   b. _counter.Process(frame, trackingOk, _armer.IsArmed)
      ├─ if !IsArmed: clear FSM state, _auditor.ClearWindow(), return
      ├─ _auditor.RecordSample(frame, _counter.Phase)
      └─ FSM elbow-angle transitions Top/Descending/Bottom/Ascending
         └─ on Ascending→Top arc completion → fire OnRepCandidate(window snapshot)

2. AntiCheatAuditor.Audit(window) — на каждый OnRepCandidate:
   a. Hard checks in order (first HardVeto wins, остальные не запускаются):
      1. RepVisibilityGate          (если скелет мусорный — нет смысла мерить остальное)
      2. TempoSanityGate            (если duration > 12с — отвергаем безусловно)
      3. FullRomGate                (нет ROM груди — фейк)
      4. KneeBendDetector per-rep   (p5_knee < 145°)
      5. BilateralSymmetryGate      (amplitude_ratio < 0.5 при bothVisible)
      6. WristAnchorMonitor verdict (Airborne)
      7. SupportPlaneCoplanarityGate (world-only)

   b. Если все hard прошли — soft checks накапливают penalty:
      formPenalty = sum( SoftDock penalties from all validators, clamped 0..0.8 )
      finalForm = LastForm * (1 - formPenalty)

   c. Emit:
      - HardVeto → PushupRepCounter.RejectRep(reason), fire OnRepRejected(reason), Telemetry.Log(rep_rejected, reasons[])
      - Pass     → PushupRepCounter.AcceptRep(), Reps++, fire OnRep(Reps), Telemetry.Log(rep_credited, signals)

3. PushupSession ретранслирует события HUD-у (PushupDebugHud).
```

**Порядок hard-checks** оптимизирован под cost: cheap-first (visibility, tempo — float compares), expensive-last (coplanarity — vector math). First-fail short-circuit экономит CPU.

**Принцип «лучше пропустить сомнительный, чем зарезать честный»**: если `RepVisibilityGate` пометил окно как degraded (vis<0.55), все остальные validators возвращают Pass (доверяем только тому, что точно знаем). Это closing P1 риск из ad-критики.

---

## Изменения в коде (полный список правок)

### NEW files

```
Assets/_Project/Scripts/CV/AntiCheat/
  IRepValidator.cs                       // interface { RepVote Validate(in RepWindow w); string Name; }
  RepVote.cs                             // readonly struct { Kind, Penalty, Reason } + static Pass/HardVeto/SoftDock
  RepRejectReason.cs                     // enum: WristsAirborne, KneePushup, InsufficientROM, Asymmetric,
                                         //       VisibilityTooLow, RepTooSlow, SupportNotCoplanar, HipWormCheat
  RepWindow.cs                           // struct { IReadOnlyList<RepSample> Samples, DurationSec, BottomDwellSec,
                                         //          ShoulderMidImgAtTop, HipMidImgAtTop, GBodyAtTop }
  RepSample.cs                           // struct per-frame: t, leftElbow, rightElbow, shoulderMidImg, hipMidImg,
                                         //                   shoulderMidWorld?, hipMidWorld?, pairAvgVis, phase
  AntiCheatAuditor.cs                    // owner of validators[], RingBuffer<RepSample>, runs Audit()
  PlankArmer.cs                          // IPlankArmer, FSM Disarmed/Arming/Armed/Cooling
  PlankRejectReason.cs                   // enum: NoLowerBody, BodySagging, KneesBent, NotAtTop,
                                         //       WristsNotPlanted, BodyNotExtended
  WristAnchorMonitor.cs                  // per-frame RingBuffer<Vector3> sliding window
  KneeBendDetector.cs                    // static helpers (MinKneeAngle, Classify) + stateful per-frame ribbon
  BilateralSymmetryGate.cs               // IRepValidator
  FullRomGate.cs                         // IRepValidator
  HipDecouplingGate.cs                   // IRepValidator
  TempoSanityGate.cs                     // IRepValidator
  RepVisibilityGate.cs                   // IRepValidator (+ winsorized_mean helper)
  SupportPlaneCoplanarityGate.cs         // IRepValidator (world-only, skip if !HasWorldLandmarks)
  BodyHorizontalGate.cs                  // helper for PlankArmer, not a validator

Assets/_Project/Scripts/CV/Util/
  RingBuffer.cs                          // zero-alloc fixed-capacity ring

Assets/_Project/Scripts/CV/Mocks/
  MockKneePushupPoseSource.cs            // knee on floor, shin up
  MockArmWavingPoseSource.cs             // standing, wrists oscillating
  MockOneArmPushupPoseSource.cs          // L arm working, R static
  MockHipThrustPoseSource.cs             // hip-only worm
  MockWallPushupPoseSource.cs            // standing fake (no shoulder travel)
  MockSlowRepPoseSource.cs               // duration > 12s
  MockLowVisPoseSource.cs                // pair_vis const 0.45
  MockLyingOnBackPoseSource.cs           // back on floor, arms waving (S10 test)
  ScriptedPoseSource.cs                  // takes (timestamp, PoseFrame)[] array, emits OnFrame по виртуальным часам

Assets/_Project/Scripts/CV/Telemetry/
  RepTelemetry.cs                        // static log-emitter, JSONL append-only to persistentDataPath

Assets/Tests/EditMode/CV/AntiCheat/
  PlankArmerTests.cs                     // scenarios A-D из ad-критики
  PlankArmerWorldTests.cs                // S10 scenario (requires world-enabled mock)
  WristAnchorMonitorTests.cs
  KneeBendDetectorTests.cs               // включая hysteresis stability test
  BilateralSymmetryGateTests.cs
  FullRomGateTests.cs
  HipDecouplingGateTests.cs
  TempoSanityGateTests.cs
  RepVisibilityGateTests.cs
  SupportPlaneCoplanarityGateTests.cs
  PushupSessionAntiCheatTests.cs         // integration: каждый Mock даёт ожидаемый Reps count
```

### MODIFIED files

| File | Изменения |
|---|---|
| `Assets/_Project/Scripts/CV/PoseFrame.cs` | Добавить `Landmark[] WorldLandmarks`, `bool HasWorldLandmarks`, `Landmark World(PoseLandmark)`. Конструктор `PoseFrame(Landmark[] img, Landmark[] world, float t)`. |
| `Assets/_Project/Scripts/CV/MediaPipe/MediaPipePoseSource.cs` | `OnPoseResult` дочитывает `result.poseWorldLandmarks`, заполняет `_pendingWorld`. `Update()` пробрасывает в `new PoseFrame(...)`. Свойство `ProvidesWorldLandmarks => true`. |
| `Assets/_Project/Scripts/CV/MockPoseSource.cs` | Синтезирует body-frame для plank (см. fragment в разделе «Расширение PoseFrame»). `ProvidesWorldLandmarks => true` после расширения. |
| `Assets/_Project/Scripts/CV/IPoseSource.cs` | Добавить `bool ProvidesWorldLandmarks { get; }`. |
| `Assets/_Project/Scripts/CV/PoseMath.cs` | Добавить `LeftElbowAngle(in PoseFrame)`, `RightElbowAngle(in PoseFrame)`, `KneeAngle(in PoseFrame)` (mean of sides). Существующий `ElbowAngle` оставляем для FSM. **Убрать вызов `LooksLikePushup` из `PushupRepCounter.Process` — gating переходит в PlankArmer.** `LooksLikePushup` оставляем как утилитный публичный (HUD «вижу человека»). |
| `Assets/_Project/Scripts/CV/PushupRepCounter.cs` | Новая сигнатура `Process(PoseFrame frame, bool trackingOk, bool isArmed)`. Убрать `LooksLikePushup` check. Добавить `event Action<RepWindow> OnRepCandidate`, methods `AcceptRep()` и `RejectRep(RepRejectReason)`. `CreditRep` переименовать в private `BuildRepCandidate` который собирает `RepWindow` и фаерит event вместо прямого инкремента. `OnRepRejected` event. Счётчик `RejectedReps` для дебага. |
| `Assets/_Project/Scripts/CV/PushupSession.cs` | В `Awake`: `_armer = new PlankArmer(_wristAnchor, _kneeBend)`, `_auditor = new AntiCheatAuditor(); _auditor.Register(...all gates...);`. `HandleFrame`: armer.Tick → counter.Process(frame, trackingOk, armer.IsArmed). Подписаться на `counter.OnRepCandidate → auditor.Audit → counter.AcceptRep/RejectRep`. Экспонировать `IPlankArmer Armer`, `event OnRepRejected(RepRejectReason)`. |
| `Assets/_Project/Scripts/CV/HUD/PushupDebugHud.cs` (если есть, иначе создать) | Показывать `Armer.State`, `Armer.ArmingProgress01`, последние 3 reject reasons, текущие vis scores. |
| `Assets/_Project/Scripts/CV/CVConstants.cs` | Новые константы (см. ниже). |
| `docs/architecture/constants.md` | Зеркально с CVConstants.cs (source of truth). |

### Новые константы

```csharp
// --- PlankArmer ---
public const float PlankArmHoldSec       = 1.0f;
public const float PlankDisarmGraceSec   = 2.5f;
public const float ArmingBodyLineAngle   = 160f;
public const float ArmingElbowTopAngle   = 150f;

// --- WristAnchor ---
public const int   WristDriftWindowFrames     = 12;
public const float WristDriftMaxFracBody      = 0.10f;
public const float WristDriftHardFailFracBody = 0.20f;
public const int   WristDriftMinSamples       = 6;
public const int   AnchorGraceFramesOnStart   = 30;  // 1s @ 30fps
public const int   AnchorGraceFramesAfterRep  = 45;  // 1.5s @ 30fps

// --- KneeBend ---
public const float MinKneeAngleDeg        = 145f;
public const float KneeHysteresisDeg      = 15f;
public const float StrictKneeBodyLineDeg  = 160f;
public const int   KneeBentConsecFrames   = 5;

// --- Bilateral Symmetry ---
public const float SymmetryAmplitudeMin   = 0.5f;
public const float SymmetryMeanDiffMaxDeg = 25f;
public const float SymmetryMinVisFrac     = 0.75f;

// --- Full ROM ---
public const float ShoulderTravelMinFrac      = 0.12f;
public const float ShoulderHipDistMinForGate  = 0.06f;  // skip floor

// --- Hip Decoupling ---
public const float HipShoulderCorrMin     = 0.5f;
public const float HipShoulderCorrSoft    = 0.6f;       // ниже — soft dock
public const float HipTravelMaxRatio      = 1.8f;
public const float HipTravelSoftRatio     = 1.3f;

// --- Tempo ---
public const float MaxRepSeconds          = 12.0f;
public const float MaxBottomDwellSeconds  = 5.0f;

// --- Rep Visibility ---
public const float RepWindowVisMin        = 0.55f;
public const float WinsorizeTrimFrac      = 0.15f;

// --- SupportPlane Coplanarity ---
public const float SupportPlaneSpreadSoftFrac = 0.20f;
public const float SupportPlaneSpreadHardFrac = 0.50f;

// --- BodyHorizontal ---
public const float BodyExtendedMinDiagFrac = 0.35f;
```

### Telemetry event schema

`RepTelemetry.cs` пишет JSONL в `Application.persistentDataPath/telemetry/{sessionId}.jsonl`:

```csharp
public struct RepTelemetryEvent
{
    public string  EventName;            // "rep_credited" | "rep_rejected"
    public string  SessionId;
    public int     AttemptIndex;         // sequence of all attempts incl rejected
    public int     RepsTotal;            // credited only
    public string  Verdict;              // "Pass" | "HardVeto" | "SoftDock"
    public string[] Reasons;             // ["KneePushup"], ["WristDrift", "BottomDwellLong"], etc.

    // Timing (sec)
    public float   StartTimeSec, BottomTimeSec, EndTimeSec, DurationSec, BottomDwellSec;
    public float   PlankArmedSec;        // как долго был armed на момент rep

    // Signals raw values
    public float   ElbowAngleMinDeg;     // min of (leftElbowMin, rightElbowMin)
    public float   LeftRomDeg, RightRomDeg;
    public float   SymmetryRatio;
    public float   SymmetryMeanDiffDeg;
    public float   KneeAngleP5Deg;
    public float   KneeValidFrameFrac;
    public float   ShoulderTravelFrac;   // S5 result
    public float   HipShoulderCorr;
    public float   HipShoulderRatio;
    public float   WristDriftMaxFrac;
    public float   MeanPairVisibility;   // S8 result
    public float?  SupportPlaneSpreadFrac;  // null if no world

    // Environment
    public bool    HasWorldLandmarks;
    public string  DeviceOrientation;
    public float   CameraFps;
}
```

PostHog event (опционально, при user opt-in в Settings): event name `pushup_rep`, properties — flattened поля выше. NO PII.

---

## Этапы реализации

### Stage 0 — Prerequisite (2-3h)
- Расширить `PoseFrame` + `MediaPipePoseSource` + `MockPoseSource` для world landmarks.
- Тесты: `MediaPipePoseSourceTests.OnPoseResult_NoWorldLandmarks_FrameStillValid`, `BodyArrayMatchesLength`, `YNotInverted`.
- **GATE: пользователь greenlight'ит изменение перед началом anti-cheat работы** (см. Open Question #4).

### Stage 1 — MVP (8-12h)
- `RingBuffer`, `RepSample`, `RepWindow`, `RepVote`, `IRepValidator`, `AntiCheatAuditor`.
- `PlankArmer` + `PlankRejectReason` + `WristAnchorMonitor` + `KneeBendDetector` (per-frame ribbon).
- `FullRomGate` (S5) — главный hard signal.
- Изменения в `PushupRepCounter` (новая сигнатура Process + OnRepCandidate) и `PushupSession` (wiring).
- Mocks: `MockKneePushupPoseSource`, `MockArmWavingPoseSource`, `MockWallPushupPoseSource`.
- Telemetry skeleton (local JSONL only).
- Тесты unit + integration: каждый mock → ожидаемый `Reps == 0`. `MockPoseSource` baseline → нормальный счёт.

**Definition of done MVP**: 3 cheat-mock'а дают 0 reps; honest mock даёт нормальный счёт; на iPhone верифицировано 10 reps с form ≥0.7.

### Stage 2 — Soft signals + symmetry (4-6h)
- `BilateralSymmetryGate` (S4) — split ElbowAngle на L/R в PoseMath.
- `HipDecouplingGate` (S6) с detrend.
- `TempoSanityGate` (S7).
- `RepVisibilityGate` (S8) + winsorized_mean helper.
- Mocks: `MockOneArmPushupPoseSource`, `MockHipThrustPoseSource`, `MockSlowRepPoseSource`, `MockLowVisPoseSource`.
- Form-score penalty integration.
- HUD: показ Armer.State + last reject reason.

### Stage 3 — World-only signals (3-5h)
- `SupportPlaneCoplanarityGate` (S9).
- `BodyHorizontalGate` (S10) в PlankArmer.
- Mock world synthesis для cheat вариантов.
- Acceleration integration для S10 (опционально — может быть deferred).

### Stage 4 — Calibration & polish (после 1000 reps телеметрии)
- Анализ телеметрии: гистограммы по credited vs rejected reps для каждого порога.
- Перекалибровка `MinKneeAngleDeg`, `ShoulderTravelMinFrac`, `SymmetryAmplitudeMin` под реальное распределение.
- Решение по «открытым вопросам» (см. ниже).

**Total estimate**: 17-26h инженерного времени до full coverage, не считая calibration phase.

---

## Телеметрия

См. schema выше. Локальное хранение **всегда** (JSONL), отправка в PostHog только при opt-in.

**Calibration goals из телеметрии**:
- `MinKneeAngleDeg`: hist credited reps → P5; hist rejected → P95; gap должен быть >20°. Если нет — порог не разделяет.
- `ShoulderTravelMinFrac`: hist credited → P5 should be > current threshold (0.12). Если P5 = 0.08 — снизить порог до 0.07.
- `MaxRepSeconds`: P99 credited + 30% buffer. Если P99 = 4s — порог можно опустить до 6s.
- `SymmetryAmplitudeMin`: P5 credited. Должно быть >0.55.

**Без минимум 500 credited reps от 10+ юзеров запуск в прод преждевременен.** Пороги в этом доке — стартовые educated guess'ы, не финальные.

---

## Решения пользователя (зафиксировано 2026-06-07)

**1. Knee push-up policy → HARD VETO.** На коленях счёт не растёт, эмитится `OnRepRejected(KneeBent)` для HUD. Никаких soft-XP за knee push-ups.

**2. Plank-arming hold → 1.0s.** `PlankArmHoldSec = 1.0f`. SerializeField на `PlankArmer` для in-Editor tuning.

**3. HUD на reject → subtle text-only flash + нейтральный «бзз» звук.** Никаких больших тостов в матче, никакого минус-счёта. Полноценный coaching overlay только при >3 rejected подряд (отдельный тикет, не MVP).

**4. World landmarks → GREENLIGHT.** Stage 0 (~2-3h) расширения `MediaPipePoseSource.OnPoseResult` для чтения `result.poseWorldLandmarks` начинается ПЕРВЫМ. Без него S9/S10 невозможны.

**5. Match-start countdown → 3-2-1.** Решение пользователя: после того как `PlankArmer` выдал `OnArmed`, запускается визуальный countdown **3 → 2 → 1 → GO**, и только после этого `PushupRepCounter` начинает считать. См. секцию «Match-start countdown» ниже.

**6. `MaxRepsPerMatch = 65` — оставить.** Деривация: 60с матч × ~60 RPM max + buffer = 70 → 65 ок. Перекалибровать по PvP-телеметрии.

---

## Match-start countdown (3-2-1) — DEFERRED → phase-14

> **2026-06-07**: UI-слой (3-2-1 countdown, plank-arming prompt, reject-feedback toasts, coaching overlay) **вынесен в [phase-14 §Anti-cheat UI hooks](phase-14-pvp-client-and-ux.md)**, потому что собирается рядом с остальным duel-UX (Assets/_Project/Scripts/UI/Duel/*). Phase-08.1 (Stages 0/1/2) поставила CV-логику и events; phase-14 их потребляет.
>
> Что доступно для consumer'а UI:
> - `PushupSession.Armer.OnArmed` / `OnDisarmed(PlankRejectReason)` / `ArmingProgress01` — биндинг для arming prompt + progress ring
> - `PushupSession.OnRepRejected(RepVote)` — биндинг для reject-тостов
> - `RepVote.Reason` (enum) + `PlankRejectReason` (enum) — для i18n маппинга
> - Один новый флаг для добавления в phase-14: `PushupRepCounter.CountingEnabled` — гейт счёта после «GO» countdown'а.
>
> Ниже — оригинальный design-набросок UX (сохранён для контекста, phase-14 ссылается):

UX-слой поверх `PlankArmer`. Anti-cheat валидирует позу, countdown готовит юзера к старту.

**Последовательность:**
1. Экран матча открыт, `PlankArmer.State = Disarmed`. HUD: «Встань в планку» + большая иконка планки.
2. Юзер заходит в планку → `State = Arming`. HUD: progress-ring заполняется за 1.0s.
3. `PlankArmer` эмитит `OnArmed` → запускается **3-2-1 countdown** (большие цифры на экране, по 1с каждая, плюс звук тика).
4. Во время countdown **продолжаем требовать валидную планку**. Если юзер вышел из планки → countdown **отменяется**, обратно в шаг 2 (без штрафа, просто перезапуск).
5. После «GO» → `PushupRepCounter` начинает считать, `OnRep` идёт в UI.

**Реализация:**
- Новый компонент `Assets/_Project/Scripts/UI/Duel/MatchStartCountdown.cs` (НЕ в `CV/AntiCheat/` — это UI).
- Подписан на `PlankArmer.OnArmed` / `OnDisarmed`.
- Использует существующую coroutine pattern (см. `WebCamPreview` для образца).
- Эмитит `OnCountdownComplete` → `PushupSession` ставит `_counter.CountingEnabled = true`.
- В `PushupRepCounter` добавляется флаг `CountingEnabled` (default false) — `Process()` no-op'ит пока он false. Это **отдельно** от `IsArmed` — armed может быть до countdown, считать начинаем после.

**Константы:**
- `CountdownStepSec = 1.0f` (3 цифры × 1с = 3с)
- `CountdownTickSoundEnabled = true`

**Поведение если юзер ломает планку во время countdown:**
- 0-я попытка: countdown отменяется, обратно в Arming.
- 3-я подряд отмена: HUD показывает coaching-tip «Не двигай руки во время отсчёта», countdown тот же 3с.

**Открытый вопрос для UI-фазы (не MVP anti-cheat)**: показывать ли opponent video preview во время countdown? Это решается в дуэль-HUD фазе.

---

## Открытые вопросы (после ответов пользователя)

Все блокирующие вопросы закрыты выше. Остались мелочи для будущей калибровки:

- Когда телеметрия наберётся — пересмотр стартовых порогов S1/S2/S5 (см. раздел «Телеметрия»).
- A/B тест `PlankArmHoldSec` 0.8s vs 1.0s vs 1.2s на retention первой сессии.
- Дизайн coaching-overlay при >3 rejected подряд (отдельный тикет, не MVP).

---

## Что НЕ делать (anti-patterns)

- **No single-threshold rejection** — комбинируем signals. Один сигнал может ложно сработать; HardVeto требует совпадения logical conditions. Soft penalties аккумулируются, но capped at 0.8.
- **No image-Y as gravity** — image-Y зависит от ориентации камеры. Используем world body-frame derivation (n_torso через cross) или image-space `g_body` (через shoulderMid→hipMid). НЕ доверять `landmark.Y` как «вверх/вниз по физике».
- **No silent rejection** — каждый rejected rep emits `OnRepRejected(reason)` event с конкретной причиной для HUD-feedback и телеметрии.
- **Don't go aggressive early** — стартовые пороги calibrated на «лучше пропустить сомнительный честный, чем зарезать». Tighten through telemetry, not gut-feeling.
- **Don't gate on a single occluded landmark** — side-camera friendly: fall back to max(L, R) для парных суставов, skip rules при `min(leftVisFrac, rightVisFrac) < 0.6`.
- **Don't invert MediaPipe world Y in adapter** — это создаёт false mental model «теперь Y вверх по гравитации» и ломает downstream. Храним body-frame as-is, документируем.
- **Don't use body-frame `shoulder.Y - hip.Y` as chest travel** — body-frame жёстко привязан к торсу, эта дельта константа. Chest travel считается только в image-space через проекцию на `g_body`.
- **Don't share rep-window buffer across threads** — все validators main-thread only.
- **Don't allocate per-frame** — все буферы pre-allocated в конструкторах через `RingBuffer<T>`, `RepSample[]`.
- **Don't tune knee threshold blindly** — изменения `MinKneeAngleDeg` без перепрогона телеметрии = регрессии. Связка «константа изменена → дашборд телеметрии проверен» обязательна.
