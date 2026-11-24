# Інструкція — спайк D1 на цільовому Windows-ПК (T5)

Покрокова інструкція до [T5](../tasks/T5-spike-libmpv-precision-on-the-target-pc.md): перевірити, що libmpv, кероване циклом Core (`PictureSync`), тримає картинку достатньо точно, і записати вердикт для roadmap D1. Критерій відмови — у [ADR-0002](../adr/0002-render-video-with-libmpv-slaved-to-the-master-clock-behind-a-renderer-port.md), розділ «verification gate». Стиль і початкові кроки (PowerShell, Git, .NET) такі самі, як у [target-pc-run-guide.md](../../two-outputs-one-clock/evidence/target-pc-run-guide.md).

## 1. Що перевіряється

Спайк відкриває файл у `MpvVideoRenderer` (власне вікно mpv), керує ним через `PictureSync` на системному годиннику (`TimeProvider.System`) і міряє. Спайк **fail**, якщо на 1080p HEVC з апаратним декодуванням хоч один рядок виконується:

| # | Рядок критерію відмови | Чим міряється | Хто вирішує |
|---|---|---|---|
| 1 | у сталому відтворенні картинка показана далі ніж за 20 мс від належного | похибка «показано мінус належне» на кожному такті: p50/p95/p99/max; PASS, якщо max ≤ 20 мс | числа |
| 2 | після паузи, поновлення чи перемотування картинка не повернулась у межі 20 мс від належного за 1 с | час повернення після кожної з 10 пауз/поновлень і 40 перемотувань (+60, −60, +10, −10 с, 10 кіл); PASS, якщо найгірший ≤ 1000 мс | числа |
| 3 | рух швидкості на 0,2 % дає видимий стрибок або повтор кадру | 10 пар рухів затримки картинки від 10 до 100 мс (туди й назад, 0,2 % швидкості) | **ваші очі**; у звіті є лише допоміжні числа |
| 4 | більше 1 кадру на хвилину запізнюється або губиться | лічильники `Late` + `Dropped` за весь прогін, на хвилину; PASS, якщо ≤ 1 | числа |

Додатково звіт містить (не вердикти, а матеріал для рішення і для рев'ю):

- час завантаження файлу в mpv (бюджет старту ≤ 3 с, `sad.md` §11);
- похибку запланованого старту (`PlayAt`) і вплив гранулярності таймера Windows: 10 стартів зі стандартним таймером та 10 з `timeBeginPeriod(1)`;
- чи не спрацювала подія `HardwareDecodingInactive` (стала б FAIL: картинка декодувалась програмно);
- окремий тест на пошкодженому файлі: чи рахується `SkippedDamaged` з `d3d11va`, чи спрацьовує `HardwareDecodingInactive`, чим закінчується недекодований шматок посеред файлу: `DecodeFailed` чи `EndOfFile`.

Годинник спайка — звичайний `MasterClock` на системному часі, без звукового бекенда: спайк ізолює шлях картинки. Звук тут не грає. Ефекти з боку звуку (ресемплер на ±2500 ppm, клавіші у вікні Avalonia) перевіряються пізніше, у T22/T23.

## 2. Що потрібно

- **ПК:** цільовий ПК з Windows 11 x64, з тим дисплеєм і відеокартою, на яких фільми дивитимуться. Вікно mpv має відкритись на цьому дисплеї.
- **Файл:** 1080p HEVC (H.265), тривалістю **не менше 30 хв** (прогін іде по файлу вперед приблизно 20 хв плюс стрибки на 60 с). Бажано реальний фільм зі швидким рухом камери, не тестова картинка.
- **Пошкоджений файл (за бажанням):** 1080p HEVC із пошкодженим шматком (наприклад, копія вашого файлу, у якій кілька мегабайтів посередині перезаписано випадковими байтами). Без нього пошкоджений тест просто пропускається.
- **Енергоживлення:** вимкніть сон і гасіння екрана на час прогону (близько 25 хв):
  ```powershell
  powercfg /change standby-timeout-ac 0
  powercfg /change monitor-timeout-ac 0
  ```
- **Тиша на ПК:** закрийте браузер, торренти, оновлення. Спайк міряє точність, а фонове навантаження її псує.

## 3. Встановлення

PowerShell, Git і .NET 10 SDK, клонування репозиторію: кроки 2.1–2.3 [попередньої інструкції](../../two-outputs-one-clock/evidence/target-pc-run-guide.md). Далі, у теці `DualPlayer` (на гілці `feature/lip-synced-video`):

```powershell
git fetch origin
git checkout feature/lip-synced-video
git pull
```

Завантажте libmpv та FFmpeg:

```powershell
powershell -ExecutionPolicy Bypass -File build\fetch-libmpv.ps1
powershell -ExecutionPolicy Bypass -File build\fetch-ffmpeg.ps1
```

`fetch-libmpv.ps1` завантажує LGPL-збірку libmpv, перевіряє SHA-256 і розпаковує `libmpv-2.dll` у `build\mpv\`. Наприкінці має з'явитись рядок про розпаковану бібліотеку. Перевірте:

```powershell
dir build\mpv\libmpv-2.dll
dir build\ffmpeg\*.dll
```

Якщо скрипт повертає 404, посилання на датовану збірку застаріло (див. коментар на початку скрипта); тоді зупиніться і напишіть про це.

Збірка:

```powershell
dotnet build DualPlayer.slnx -c Release
```

## 4. Запуск спайка

### 4.1 Короткий пробний прогін (3 хв)

Спершу переконайтесь, що все працює: вікно mpv відкривається, картинка йде.

```powershell
$env:DUALPLAYER_HARDWARE = "1"
$env:DUALPLAYER_SPIKE_FILE = 'D:\Video\film-1080p-hevc.mkv'   # ваш шлях, у одинарних лапках
$env:DUALPLAYER_SPIKE_REPORT = "$HOME\d1-spike-report.md"
$env:DUALPLAYER_SPIKE_STEADY_SECONDS = "30"
$env:DUALPLAYER_SPIKE_CYCLES = "2"
$env:DUALPLAYER_SPIKE_MOVE_MAX_MS = "30"
dotnet test tests\DualPlayer.Media.Tests -c Release --filter "FullyQualifiedName~Libmpv_steered_by_the_core_loop" --logger "console;verbosity=normal"
```

Числа цього прогону не є свідченням, лише перевірка, що все запускається. Якщо тест `Failed` з повідомленням `mpv.not_found` (немає `libmpv-2.dll` поряд з тестами) або `mpv.load_failed` (файл не відкрився), скопіюйте блок помилки.

### 4.2 Повний прогін (~25 хв)

```powershell
Remove-Item Env:DUALPLAYER_SPIKE_STEADY_SECONDS, Env:DUALPLAYER_SPIKE_CYCLES, Env:DUALPLAYER_SPIKE_MOVE_MAX_MS
dotnet test tests\DualPlayer.Media.Tests -c Release --filter "FullyQualifiedName~Libmpv_steered_by_the_core_loop" --logger "console;verbosity=normal"
```

Типові значення: 10 хв сталого відтворення, 10 пауз/поновлень, 10 кіл перемотувань (+60, −60, +10, −10 с), 10 пар рухів затримки картинки до 100 мс (кожен рух триває до 50 с, бо швидкість змінюється лише на 0,2 %). Скорочувати для остаточного прогону не можна. Налаштування середовища:

| Змінна | Типово | Що робить |
|---|---|---|
| `DUALPLAYER_SPIKE_FILE` | обов'язкова | шлях до 1080p HEVC |
| `DUALPLAYER_SPIKE_REPORT` | тека тестів, `d1-spike-report-<час>.md` | куди писати звіт |
| `DUALPLAYER_SPIKE_STEADY_SECONDS` | 600 | тривалість сталого відтворення |
| `DUALPLAYER_SPIKE_CYCLES` | 10 | кількість пауз/поновлень, кіл перемотувань і пар рухів |
| `DUALPLAYER_SPIKE_MOVE_MAX_MS` | 100 | найбільший рух затримки картинки |
| `DUALPLAYER_SPIKE_MPV_OPTIONS` | порожньо | перевизначення опцій mpv, `ключ=значення;ключ=значення` (лише для діагностики) |

### 4.3 Що дивитись очима під час рухів

Рух затримки картинки (рядок 3 критерію) триває приблизно 9 хв наприкінці прогону. У цей час тест у консолі нічого не виводить, але вікно mpv з картинкою йде. Дивіться на вікно хоча б частину цього часу, особливо на початку і в кінці кожного руху (там швидкість змінюється):

- **Стрибок:** картинка раптово «перескочила» вперед чи назад.
- **Повтор кадру:** один кадр постояв удвічі довше, ніж треба (помітніше на панорамах і титрах).
- **Чорний кадр** чи мигання.
- Окремо запишіть, чи бачили щось **під час пауз і перемотувань** (чорний кадр на секунду допустимий; стрибки після повернення в норму ні).

Якщо не бачили нічого поганого, так і запишіть. Якщо бачили, запишіть приблизний момент від початку фази рухів (звіт містить по рядку на кожен рух, але без часу на годиннику, тож запишіть, який за рахунком рух був).

### 4.4 Пошкоджений файл (за бажанням, до 2 хв)

```powershell
$env:DUALPLAYER_SPIKE_DAMAGED_FILE = 'D:\Video\film-damaged.mkv'
dotnet test tests\DualPlayer.Media.Tests -c Release --filter "FullyQualifiedName~A_damaged_file_with_d3d11va" --logger "console;verbosity=normal"
```

Файл відтворюється до кінця або до помилки (ліміт `DUALPLAYER_SPIKE_DAMAGED_SECONDS`, типово 120 с). Звіт іде поруч із основним: `...-damaged.md`. Очікування, які він підтверджує або спростовує: `SkippedDamaged` > 0 і відтворення триває; недекодований шматок посеред файлу закінчується `DecodeFailed`, а не тихим `EndOfFile`.

### 4.5 Прибирання

```powershell
Remove-Item Env:DUALPLAYER_HARDWARE, Env:DUALPLAYER_SPIKE_FILE, Env:DUALPLAYER_SPIKE_REPORT, Env:DUALPLAYER_SPIKE_DAMAGED_FILE -ErrorAction SilentlyContinue
```

## 5. Результат

Тест завершується:

- `Passed`: жоден числовий рядок не FAIL. Це **не** вердикт D1 сам по собі: рядок 3 (стрибки) вирішуєте ви.
- `Failed` з `A kill-criterion line failed - see <шлях>`: хоч один числовий рядок FAIL. Звіт усе одно записано.
- Шлях до звіту виведено діагностичним повідомленням і лежить у `DUALPLAYER_SPIKE_REPORT`; якщо змінну не задано, то в `tests\DualPlayer.Media.Tests\bin\Release\net10.0\d1-spike-report-<час>.md`.

Звіт містить таблицю «рядок критерію → виміряне → PASS/FAIL» з порожнім стовпцем «Owner observation» і розділи: інші виміри, запланований старт і таймер, рухи затримки, час повернення кожної події.

## 6. Запис свідчення

1. Створіть `docs/features/lip-synced-video/evidence/<дата>-d1-spike.md` (дата прогону, `РРРР-ММ-ДД`) і скопіюйте у `docs/features/lip-synced-video/evidence/<дата>-d1-spike/` звіт(и) `.md` (основний і, якщо був, пошкоджений).
2. Заповніть шаблон нижче. Числа беріть зі звіту, вердикти за числами лишайте такими, як у звіті; рядок 3 заповнюєте ви.

```markdown
# D1 spike: libmpv precision on the target PC - <дата>

- PC: <процесор, відеокарта, драйвер, дисплей, частота оновлення>, Windows 11 <збірка>
- Файл: <назва, кодек, роздільність, частота кадрів, тривалість>
- libmpv: <версія з build/fetch-libmpv.ps1>; hwdec: d3d11va
- Звіти: ./<дата>-d1-spike/<файл>.md
- Гілка/коміт: <git rev-parse --short HEAD>

| Kill-criterion line | Measured | Verdict |
|---|---|---|
| 1. Steady play: presented > 20 ms from due | p50 … / p95 … / p99 … / max … ms | |
| 2a. Pause/resume: not within 20 ms of due in 1 s | worst … ms | |
| 2b. Seek: not within 20 ms of due in 1 s | worst … ms | |
| 3. 0.2 % move: visible jump or repeated frame | <що бачили, коли> | |
| 4. > 1 frame/min late or dropped | … / min | |
| Hardware decoding stayed on | | |

## Other measurements
- mpv load time: … ms
- scheduled-start error: default timer … ms, timeBeginPeriod(1) … ms
- damaged file: SkippedDamaged …, HardwareDecodingInactive …, end: DecodeFailed / EndOfFile
- not covered here (T22/T23): key focus with the native child window; resampler artefacts at ±2500 ppm

## Verdict D1
<PASS / FAIL, одне речення>
```

## 7. Що означає результат

- **Усі рядки PASS** (включно з вашим «стрибків немає»): D1 вирішено, libmpv під керуванням циклу Core лишається. Агент оновить `docs/roadmap.md` (D1 decided з посиланням на ADR-0002 і свідчення) і рядок зони хвилі 3 для кроку 4, після чого `/sdd:implement` продовжує з T6 і далі.
- **Хоч один рядок FAIL:** D1 провалено. `/sdd:implement` зупиняється; жодне завдання після T5 не стартує. Далі:
  1. `/sdd:decide-adr`: новий ADR, що замінює ADR-0002 (запасний варіант 2 за тим самим портом `IVideoRenderer`: власні декодування й показ на FFmpeg);
  2. перерахувати крок до розміру XL, повторити проєктування частини Media;
  3. оновити статус у roadmap.
- **Сумнівні випадки** (наприклад, max 21 мс один раз на тисячі тактів, або один стрибок на 100 рухів): не округлюйте на свою користь. Запишіть як є і прогоніть ще раз; вердикт ваш, але критерій у ADR-0002 жорсткий.

Усе, що у звіті виглядає дивно (наприклад, стала похибка рівно в один кадр на всіх тактах), зберігайте як є: це матеріал для рев'ю адаптера, а не привід переправляти числа.
