# Музыка онбординга и замера

Пользователь сгенерировал оба трека по промптам из разговора 26.09.2026 (Athletic Adventure, D Dorian) и принёс их из Downloads.

| Исходник | Игровой файл | Где звучит |
|---|---|---|
| `Welcome to Training.original.mp3` | `music_onboarding_welcome.wav` | Сцена Onboarding (выбор героя, тренер, камера, постановка телефона) и Boot при первом запуске |
| `Marimba Pulse.original.mp3` | `music_assessment_pulse.wav` | Живой замер (`FightMode.LevelTest`) и в Onboarding, и в Fight |

Игровые файлы лежат в `Assets/_Project/Resources/Audio/`: WAV PCM 16-bit, 48 kHz stereo; Unity импортирует их как потоковый Vorbis (`GameAudioImporter`: всё с префиксом `music_`).

Обработка (ffmpeg после декодирования):

- Welcome: темп по сетке онсетов 96.14 BPM; файл дополнен тишиной до ровно 16 тактов (39.943 с), чтобы петля не сбивала ритм. Фейд 5 мс в начале, затухание хвоста с 39.5 с. Громкость без изменений (−14.9 LUFS ≈ Action Groove −14.5).
- Marimba Pulse: 107 BPM; обрезан до 83 тактов (186.170 с), −2 dB до −14.6 LUFS, фейд 5 мс / хвост с 185.6 с.

Переключение: `GameAudio` держит два музыкальных источника и делает кроссфейд. Тема сцены — `SceneTheme`, режим внутри сцены — `SetMusicOverride`/`ClearMusicOverride` (замер ставит его в `FightController`). Главный трек после возврата продолжается с места, где его прервали.
