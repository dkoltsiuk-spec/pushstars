# Фаза 03 — Дизайн-система UI

## Цель фазы

Перенести визуальный язык из макетов в переиспользуемые префабы: тёмный фон, акцентный синий/лайм, типографика, кнопки, чипы, бейджи пользователя.

## Что НЕ делаем в этой фазе

- Не подключаем реальные данные Firebase.
- Не внедряем MediaPipe и Genies.

## Предусловия

Фаза 02.

## Затрагиваемые экраны

1, 2, 3, 4, 5, 6, 7 — общие компоненты.

## Файлы

- `Assets/_Project/UI/Theme/` — ScriptableObject или статический класс цветов (PrimaryBlue, AccentLime, TrophyGold, DangerRed, BG Dark).
- Префабы: `PrimaryButton`, `SecondaryChip`, `StatBadge`, `PlayerPill` (флаг + имя), `TrophyRow`, `LoadingVsRing` (маска под анимацию фазы 14).
- Шрифты: TMP fallback для кириллицы.

## Acceptance criteria

- [x] Все префабы на одной тестовой сцене `UI_Gallery`.
- [x] Safe Area учтена для iPhone с вырезом.
- [x] Локализация-ready: строки через ключи (подготовка к фазе 17).

## Реализованные файлы (фаза 03)

### Runtime-скрипты (`Assets/_Project/Scripts/UI/`)
| Файл | Описание |
|------|----------|
| `Theme/PushStarsTheme.cs` | ScriptableObject — все цветовые токены |
| `Theme/AppColors.cs` | Статический класс быстрого доступа к цветам |
| `Theme/ThemeInitializer.cs` | MonoBehaviour — загружает тему из Resources при старте |
| `Components/SafeAreaFitter.cs` | Подгоняет RectTransform под safe area (iPhone notch) |
| `Components/PrimaryButton.cs` | Крупная CTA-кнопка |
| `Components/SecondaryChip.cs` | Маленький чип выбора режима |
| `Components/StatBadge.cs` | Бейдж значение + подпись |
| `Components/PlayerPill.cs` | Флаг + имя игрока + трофеи |
| `Components/TrophyRow.cs` | Строка лидерборда |
| `Components/LoadingVsRing.cs` | Вращающееся кольцо VS (заглушка для фазы 14) |

### Editor-скрипты (`Assets/_Project/Editor/`)
| Файл | Описание |
|------|----------|
| `UIGallerySetup.cs` | `Tools → Push Stars → Setup UI Gallery` (авто-дискавери спрайтов из папки) |
| `SpriteFactory.cs` | `Tools → Push Stars → Generate UI Sprites` — процедурные pill/circle/ring |
| `SpriteImporter.cs` | `Tools → Push Stars → Configure Figma Sprites` — применяет импорт-настройки к PNG из Figma |
| `FontSetup.cs` | `Tools → Push Stars → Setup Rubik Font` — генерирует TMP Font Asset из Rubik-Variable.ttf |
| `DemoScreensSetup.cs` | `Tools → Push Stars → Build Demo Screens` |

### Генерируемые ассеты (создаются Editor-скриптом)
| Путь | Тип |
|------|-----|
| `Assets/_Project/UI/Theme/Resources/PushStarsTheme.asset` | ScriptableObject |
| `Assets/_Project/UI/Prefabs/PrimaryButton.prefab` | Префаб |
| `Assets/_Project/UI/Prefabs/SecondaryChip.prefab` | Префаб |
| `Assets/_Project/UI/Prefabs/StatBadge.prefab` | Префаб |
| `Assets/_Project/UI/Prefabs/PlayerPill.prefab` | Префаб |
| `Assets/_Project/UI/Prefabs/TrophyRow.prefab` | Префаб |
| `Assets/_Project/UI/Prefabs/LoadingVsRing.prefab` | Префаб |
| `Assets/_Project/Scenes/UI_Gallery.unity` | Тестовая сцена |

## Workflow замены спрайтов (Figma → Unity)

1. Экспортируй PNG из Figma по согласованным размерам (см. ниже).
2. Положи файл в `Assets/_Project/UI/Sprites/` с правильным именем.
3. `Tools → Push Stars → Configure Figma Sprites` — настроит TextureType, border, filter.
4. `Tools → Push Stars → Setup UI Gallery` — пересоберёт тему + префабы с новым спрайтом.
5. `Tools → Push Stars → Build Demo Screens` — пересоберёт сцены.

### Конвенция имён файлов

| Файл | Применение | Размер Figma | Import |
|------|------------|--------------|--------|
| `btn_primary.png` | PrimaryButton bg | 96×96 @2x, без текста | 9-slice, border 24 px |
| `chip.png` | SecondaryChip bg | 72×36 @2x | 9-slice, border 16 px |
| `badge.png` | PlayerPill / TrophyRow bg | 64×32 @2x | 9-slice, border 12 px |
| `icon_circle.png` | Nav-таб / VS-значок | 128×128 | Simple, Bilinear |
| `ring_dashed.png` | Матчмейкинг-кольцо | 512×512 | Simple, Bilinear |
| `icon_sm_*.png` | Мелкие иконки | 64×64 | Simple, Point filter |
| `icon_*.png` | Средние иконки | 128×128 | Simple, Bilinear |
| `bg_*.png` | Фоновые изображения | 780×1688 | Simple, Bilinear |

### Приоритет спрайтов в PushStarsTheme
`btn_primary.png` > `pill_24.png` (процедурный fallback)  
— аналогично для остальных слотов.

## Тестирование

Проверка на нескольких разрешениях (маленький Android + iPhone с notch).

## Связь с дизайном

Все 7 основных экранов используют общие элементы из этого набора.
