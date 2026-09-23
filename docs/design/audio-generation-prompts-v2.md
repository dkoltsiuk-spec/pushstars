# Push Stars — готовые задания для генерации аудио

Версия 21.09.2026. Использовать вместе с `audio-direction-v2.md`. Все тексты ниже — задания; аудиорезультаты пока не получены. Запросы на английском для удобства переноса между сервисами.

## Как пользоваться

**Музыка:** Suno или Eleven Music, инструментальный режим, текст песни пустой. Если есть исключения — отдельно перечислить vocals, speech, choir, chants, EDM drops, chiptune. Для прослушивания направления достаточно 30 с; если сервис создаёт более длинный трек, оценивать участок с установившимся грувом. Создать по два варианта A/B/C, сравнить с одинаковой громкостью под видео проекта. Длительность, BPM и тональность из промпта проверять: модель не обязана соблюдать их точно.

**SFX:** ElevenLabs Sound Effects; один тип события за запрос, 3–4 варианта. Для короткого эффекта разрешено генерировать 1–3 с с тишиной после события, затем аккуратно вырезать готовый звук. Одна генерация длинного «набора всех звуков» не даёт удобного контроля. Варьировать material/body/brightness, сохраняя остальной текст.

**Сначала сделать:** A/B/C music, rep_accept, boss_hit, ui_tap, start, victory. Остальное — после принятия палитры. Это экономит итерации, а не подменяет качество дешёвой моделью.

## Музыкальные направления

### A — Athletic Adventure, основной кандидат

```text
Instrumental game soundtrack for Push Stars, a colorful stylized 3D fitness arena with a mischievous goblin forest boss. Athletic playful adventure, confident and tactile. Target 112 BPM, D Dorian. Tight dry breakbeat drums, warm rounded electric bass, hand percussion and wooden rim accents, sparse marimba, pizzicato strings, tiny brass answers. An original short ascending three-note motif, used sparingly with long gaps. Polished organic studio instrument textures, spacious midrange for gameplay feedback. Establish a steady groove immediately and keep the energy consistent. Restrained melody, loop-friendly arrangement. No vocals, chants, speech, EDM drops, chiptune, epic trailer orchestra or long intro. Thirty-second direction audition.
```

Отбирать по ощущению спортивного движения, материальности инструментов и свободному месту для повторов. Если слишком «детский мультфильм» — убрать marimba/brass, оставить дерево и бас. Если слишком обычный фитнес-трек — усилить pizzicato и ручную перкуссию.

### B — Arcade Funk

```text
Instrumental music for a modern playful fitness competition mobile game. Athletic arcade funk, target 108 BPM, D Dorian. Dry punchy drum kit with a relaxed breakbeat, warm fingerstyle electric bass, muted funk guitar, subtle Rhodes and restrained wooden percussion. An original short ascending three-note hook with lots of breathing space. Clean polished production, positive competitive swagger without aggression. Leave space for repeated game sound effects. Start with the groove, keep a consistent arrangement. No singing, voices, vocal samples, EDM risers or drops, chiptune or cinematic trailer drums. Thirty-second audition.
```

Отбирать по упругости, взрослому характеру и комфортности. Отбраковывать солирующую гитару, слишком сложный бас и выраженную песенную структуру.

### C — Playful Quest

```text
Instrumental playful adventure game battle music for a colorful stylized goblin forest arena. Target 116 BPM, D Dorian. Agile pizzicato strings, warm marimba ostinato, hand drums, dry wood percussion, light short brass responses and soft rounded bass. Brave, mischievous and energetic but friendly. A small expressive ensemble with premium recorded instrument texture and a spacious arrangement for gameplay impacts. Original short ascending three-note motif. Establish the groove immediately. No vocals, choir, chants, speech, horror, epic trailer percussion, chiptune, EDM drops or long intro. Steady background energy. Thirty-second audition.
```

Отбирать по характеру босса и ощущению места. Отбраковывать цирковое настроение, чрезмерную комедию и сплошную мелодию.

## Производные выбранного направления

Ниже дополнения к принятому музыкальному брифу. Для сохранения темы использовать исходный проект, разрешённый собственный референс или редактирование выбранной генерации, если сервис это поддерживает. Одинаковые слова в двух запросах не обеспечат согласованных дорожек.

| Материал | Дополнение |
|---|---|
| Меню | `Calm confident home-screen arrangement, target 108 BPM. Bass and dry drums, occasional brief motif, no dramatic buildup. Sixteen-bar loopable section.` |
| Подготовка | `Sparse preparation mix. Remove the active drum groove, keep a warm harmonic bed and restrained bass. Calm and clear, leave space for instructions.` |
| Тренировка | `Steady supportive training groove, minimal melody, no surprise fills, no countdown, no tempo changes. Maintain space for a soft repetition confirmation.` |
| Босс | `Add dry wooden percussion and mischievous pizzicato replies. Playful determination, compact rhythmic arrangement, no horror or cinematic escalation.` |
| Финальная фаза | `Increase rhythmic density slightly using light percussion, keep the exact tempo and harmonic sequence. No faster tempo, no huge climax, no vocal count.` |
| Результат | `Relaxed resolution of the established motif, sparse warm accompaniment, low rhythmic density and generous silence between phrases.` |

## Эффекты: готовые промпты

### 1. Кнопка — ui_tap

```text
A single tiny tactile mobile game button click: a dry rounded wood-and-soft-plastic pop with a subtle warm body. Crisp immediate attack, very short natural decay, intimate close studio recording. Friendly and premium, not squeaky. No melody, glass ping, mouse double click, reverb, voice, music or background noise. One event near the start followed by silence.
```

Финал: 50–120 мс. Получить четыре варианта одинаковой воспринимаемой силы.

### 2. Подтверждение / назад / вкладка

```text
A single compact friendly game interface confirmation: tactile soft wooden click followed immediately by a tiny warm upward tonal response. Rounded, dry, satisfying, restrained. No sparkling bells, harsh treble, voice, music bed or ambience. One short event, then silence.
```

Для back заменить upward на downward и сделать мягче; для tab убрать tonal response и добавить `a tiny soft fabric slide`. Это родственные, но различимые действия.

### 3. Засчитанный повтор — rep_accept

```text
One isolated satisfying fitness game repetition confirmation. A rounded dry wooden tok layered with a very soft leather pad thump and a restrained warm pitched body. Compact and tactile, friendly, immediate attack, about 160 milliseconds of decay. Pleasant when repeated many times. No metallic ringing, bright beep, explosion, coin sound, voice, music, ambience or reverb. One event near the start, then silence.
```

Шесть вариантов. Главный тест: 60 повторов подряд под музыку, ни один вариант не выпрыгивает по громкости или высоте.

### 4. Незасчитанная попытка — rep_reject

```text
A single gentle game feedback sound for an uncounted exercise repetition: two very short muted low wooden ticks, calm and informative, with soft edges. Clearly different from a successful rounded tok. No alarm, siren, buzzer, comedy failure, speech, music or reverb. Short isolated event followed by silence.
```

### 5. Отсчёт и старт

```text
One clean compact countdown tick for a friendly athletic game. A dry midrange wooden rim click with a small warm tonal center, precise and easy to hear on a phone speaker. No shrill beep, voice, music or ambience. Single tick with a short tail, then silence.
```

Отсчёт 3–2–1 собирается игровыми событиями, не одним длинным файлом.

```text
A short confident start signal for a playful fitness arena. Tight tactile click into a warm upward two-note accent and a tiny soft drum punctuation. Immediate, friendly, energetic and brief. No spoken go, whistle, siren, harsh treble, music bed or long reverb. One isolated start cue.
```

### 6. Удар по боссу — boss_hit

```text
A single isolated stylized cartoon game punch impact. Friendly athletic force: compact dry leather boxing-pad thump with a subtle woody crack. Satisfying tactile midrange body and a quick natural decay. Clean close studio foley. No harsh treble, sub-bass explosion, gore, voice, music, ambience or long reverb. One hit near the beginning followed by silence.
```

Для шести вариантов немного менять leather pad / padded wood / soft body thump. Whoosh делать отдельным слоем, чтобы контакт можно было синхронизировать независимо.

### 7. Получение урона — player_hurt

```text
A single muted padded-body impact for a friendly stylized sports game. Round low-mid thud, softer darker texture than a bright wooden punch, short cloth movement, compact decay. Clearly perceptible but not painful or frightening. No grunt, voice, bones, gore, bass boom, music or reverb. One isolated contact followed by silence.
```

### 8. Замах — attack_whoosh

```text
A single very short close cloth-and-air swish from a quick cartoon boxing motion. Dry, light, compact, smooth attack and fast decay. No sword, magic, impact, voice, music, ambience or long airy tail. One isolated movement near the beginning, then silence.
```

### 9. Потеря трекинга / восстановление

```text
A short calm attention cue for a fitness app losing camera tracking. Two soft rounded midrange wooden-tonal pulses, clearly separated, informative and non-alarming. Audible on a phone speaker without piercing high frequencies. No warning siren, speech, music or dramatic effect. One isolated cue.
```

Восстановление: `a single warm upward resolution, brief and reassuring`. Различимость проверять без экрана; не делать похожим на урон.

### 10. Победа — музыкальный джингл

```text
A two-second instrumental victory stinger for a colorful athletic adventure game. Warm short brass and pizzicato answer over a tight dry drum punctuation. An original ascending three-note motif with a confident friendly resolution. Earned satisfaction, small premium ensemble, warm bass support. No vocals, cheering crowd, choir, epic trailer boom, cymbal wash or long reverb. Clear start and complete musical ending.
```

Создавать музыкальной моделью; точный мотив согласовать с выбранной темой. Поражение: `a gentle restrained unresolved-to-neutral ending, respectful and warm, no sad trombone or comic failure`; ничья: `a balanced neutral resolution without a triumphant rise`.

### 11. Начисление награды

```text
One tiny soft reward counter tick for a premium playful game: a delicate rounded wooden bead click with a restrained warm sparkle. Very brief, dry, low intensity, pleasant in a quick sequence. No cash register, slot machine, sharp bell, voice or music. One isolated tick, then silence.
```

Финальное подтверждение собрать из того же материала и короткого мотива, не просто поднять громкость тика.

### 12. Кейс: механизм и зарядка

```text
A single small stylized treasure case unlocking. A tactile wooden latch and soft mechanical click with a tiny leather movement. Compact friendly game foley, clean dry close recording. No magical explosion, voice, music, metallic ringing or ambience. One event followed by silence.
```

```text
A brief gentle game reward energy buildup, warm airy texture with a rounded rising tonal body, increasing smoothly toward a clean stop. Friendly anticipation, restrained sparkle, no harsh hiss, alarm, vocals, music or final impact. Isolated charge sound suitable for editing to 0.7 seconds.
```

Окончательную зарядку смонтировать точно под 0.7 с; не принимать случайно сгенерированную длительность как совпадение с анимацией.

### 13. Раскрытие кейса

```text
A single rewarding treasure reveal for a colorful athletic adventure game. A soft tactile opening pop, a warm compact impact and a brief delicate ascending shimmer. Premium, friendly, satisfying, clear transient and a controlled short tail. No casino jackpot, huge explosion, harsh metallic ringing, voice, music bed or long reverb.
```

Редкости создавать слоями одного принятого исходника: базовое раскрытие; плюс короткий мотив; плюс немного ширины и воздушного хвоста. Они должны отличаться ценностью события, не скачком громкости.

### 14. Падение босса

```text
A single friendly cartoon goblin landing softly on a forest platform after defeat. A rounded padded thump, small dry wooden resonance and a brief scatter of soft leaves. Weighty but not violent, short close foley, clear contact. No scream, speech, gore, bone crack, music or huge bass. One landing followed by natural short decay and silence.
```

### 15. Лесное окружение

```text
A quiet seamless forest ambience for a colorful fantasy mobile game. Gentle soft air through broad leaves, sparse distant natural details, calm spacious background. Very even level, restrained high frequencies, no foreground bird calls, no sudden events, no footsteps, animals growling, voices, music, thunder or dramatic wind. Suitable as a barely audible looping bed under game music.
```

Включить loop, если он поддерживается сервисом, и всё равно проверить стык вручную.

## Что отдавать после генерации

Сохранить оригинальные загрузки без повторного MP3-перекодирования, промпт и параметры, дату/модель/условия использования. Названия кандидатов: `music_A_take01`, `music_B_take01`, `sfx_rep_take01` и т. п. Выбранные версии отдельно обработать в WAV, сделать игровые варианты и проверить микс. Генерация — источник материала; итоговый звук требует монтажа и сведения.

## Проверенные ссылки

- [Suno](https://suno.com/) и [коммерческое использование](https://help.suno.com/en/articles/9601665).
- [Eleven Music: возможности](https://elevenlabs.io/docs/overview/capabilities/music), [план композиции](https://elevenlabs.io/docs/eleven-api/guides/how-to/music/composition-plans).
- [ElevenLabs Sound Effects](https://elevenlabs.io/sound-effects).
- [BOOM Casual UI: демонстрация и палитра](https://www.boomlibrary.com/sound-effects/casual-ui/).
- [Sonniss: исходники GameAudioGDC](https://sonniss.com/gameaudiogdc/).
