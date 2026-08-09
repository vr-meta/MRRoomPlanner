# Чек-лист проекта

Единый список задач: что сделано, что нет, что в работе. Работать **согласно этому
чек-листу** (см. `CLAUDE.md`). Крупную незавершённую задачу разбивать на под-задачи и
отмечать их по мере готовности.

**Легенда:** `[x]` готово · `[~]` в работе · `[ ]` не начато · 👤 действие пользователя.

---

## 0. Окружение (Windows)

- [x] Unity Hub 3.20 установлен
- [x] Путь редакторов задан на `D:\Unity\Editors`
- [x] Unity Editor **6000.0.81f1** установлен (офлайн-инсталлятором на D:)
- [x] Android Build Support (PlaybackEngine) установлен
- [x] Android **SDK / NDK / OpenJDK** — доустановлены напрямую (отсутствовали в offline-инсталляторе)
  - [x] OpenJDK 17 + NDK r27c — распакованы в `AndroidPlayer/{OpenJDK,NDK}` на D:
  - [x] cmdline-tools → `SDK/cmdline-tools/latest`; через `sdkmanager`: platform-tools, platforms android-34/35, build-tools 36.0.0, cmake 3.22.1
  - [x] компоненты проверены (adb, platforms, build-tools, cmake, ndk, java — все на месте)
  - [ ] проверить фактическую сборку APK (после Meta SDK + сцены)
- [ ] 👤 **Лицензия Unity**: войти в Unity Hub под Unity ID → выбрать бесплатную **Personal**
- [~] 👤 **Meta XR SDK**: **Meta XR All-in-One SDK импортирован** ✅
  - [ ] прогнать **Meta XR Project Setup Tool** (Fix All / Apply All) — настройки Android/OpenXR/passthrough
  - [ ] проверить/добавить **MR Utility Kit (MRUK)** — если не пришёл с All-in-One
- [ ] 👤 Импорт сэмпла **Virtual Home** (из пакета MRUK)
- [ ] 👤 **Quest Developer Mode** (приложение Meta Horizon на телефоне) + подключение по USB + разрешение отладки

## 1. Проект Unity (открываемый)

- [ ] `Packages/manifest.json` + `ProjectSettings` из шаблона редактора (валидно под 6000.0.81f1)
- [ ] `ProjectSettings/ProjectVersion.txt` (changeset `6238fec1e98f`)
- [ ] Настройки сборки: Android, OpenXR (Meta Quest), Passthrough, разрешение Scene
- [ ] Проект открывается без ошибок компиляции
- [ ] Добавить define `ROOMPLANNER_META` после импорта Meta SDK (включает ввод с контроллеров)

## 2. Рулетка (первое приложение)

- [x] Скелет кода `Assets/RoomPlanner/Scripts/Measure/*` (компилируется без Meta SDK)
- [x] MR-сцена (Building Blocks): Camera Rig + Passthrough + Effect Mesh + MRUK
- [x] Editor-автосборка `RoomPlanner → Setup Measure Rig` (создаёт MeasureRig + префаб, связывает ссылки, включает коллайдеры Effect Mesh)
- [x] Запущен Setup Measure Rig, TMP Essentials импортированы, сцена сохранена
- [~] Сборка `.apk` и запуск на Quest: точка A → точка B → расстояние в MR
  - [x] Недостающие пакеты добавлены в manifest (test-framework, ugui, inputsystem) — проект компилируется
  - [ ] 👤 Developer Mode на Quest + USB + разрешить отладку
  - [ ] `adb devices` видит шлем
  - [x] XR-плагин **OpenXR** не был установлен (Meta Fix All не смог) → добавлен `com.unity.xr.openxr` 1.16.1 в manifest; OpenXR+MetaQuestSupport компилируются
  - [x] **Проект переведён на URP**: добавлен `com.unity.render-pipelines.universal` 17.0.4 + меню `RoomPlanner → Switch To URP`; MRUK-шейдеры компилируются
  - [x] Build And Run (Android) — APK собирается, ставится и запускается на Quest (passthrough работает)
- [x] **Рулетка работает на Quest** ✅ (проверено в шлеме)
  - [x] Ввод: убрана директива `ROOMPLANNER_META`, `OVRInput` активен всегда
  - [x] Указатель: луч от `OVRCameraRig.rightControllerAnchor`
  - [x] Материалы линии/маркеров/прицела — сохранённые URP .mat-ассеты (стерео корректно, без двоения)
  - [x] Линия стоит в мире, расстояние считается и отображается
  - [x] Цепочка измерений через кнопку «+» у последней точки (проверено)
  - [~] Привязка к оси (вертикаль/горизонталь) при зажатом грипе
  - [x] Размеры всегда в сантиметрах
  - [x] Магнит точек к концам существующих измерений (snapDistance 0.1 м)
  - [~] Бейдж размера: фиолетовая скруглённая плашка, белый текст, чуть **над** линией
  - [~] Редактирование: удалить (наведение+B), тащить точку (наведение+удержание триггера)
    - [x] «+» вынесен **вбок** от шарика-точки (вдоль продолжения линии) — чтобы не путать «добавить» и «редактировать»
    - [x] Хаптик при наведении на «+»/шарик + «+» подрастает под прицелом
    - [x] «+» **универсален и по наведению**: всплывает у любой точки (стартовой/конечной), один пулиный экземпляр (нет постоянных «+»)
    - [x] Совпавшие точки нескольких линий **схлопываются в один маркер**
    - [x] В режиме перетаскивания «+» **скрыт** (не мешает магнититься к точкам)
- [ ] Инструмент «уровень» (по нормали) — на будущее
- [ ] Редактирование размеров в см — на будущее

## 3. Структура проекта: Дом → Этажи → Комнаты (корневое)

_Дизайн: `docs/design/09-project-structure.md`._
- [ ] Модель данных `Project / Floor / Room` (+ transform комнаты, Y-этажа, captureState)
- [ ] Захват по комнатам (Space Setup) и хранение нескольких `MRUKRoom`
- [ ] Сборка на 2D-плане: snap по общим стенам/дверям; стек этажей по высоте
- [ ] Сохранение/загрузка проекта (JSON)

## 4. Ingest: скан → наша модель

_Дизайн: `docs/design/09-project-structure.md`, `02-walls.md`._
- [ ] Маппинг: `WALL_FACE` / контур пола → `WallGraph`; `DOOR_FRAME`/`WINDOW_FRAME` → `Opening`; объёмы → мебель
- [ ] **Свёртка тонких стен (медиальная ось)** — пары встречных параллельных граней → один сегмент + толщина; торец отбрасывать; периметр (одиночная грань) → толщина наружу
  - [ ] авто-детект по порогам (толщина/угол/перекрытие)
  - [ ] ручной фолбэк «свернуть в одну стену»
  - [ ] автотест: повторная экструзия воспроизводит скан

## 5. Модули (по дорожной карте) — на будущее

- [ ] **Wall Builder** — граф стен, стыки/T, высота (`02-walls.md`)
- [ ] **Openings** — двери/окна, панелизация (`03-openings.md`)
- [ ] **Surfaces & Materials** — метрические UV, двусторонние стены, зоны (`04-surfaces-materials.md`)
- [ ] **Blueprint** — подложка чертежа + калибровка
- [ ] **AI Placement** — расстановка мебели через Claude + glTF-каталог (`05-ai-placement.md`)
- [ ] **Project I/O** — формат проекта + экспорт glTF (`06-project-format.md`)
- [ ] **MEP + слои** — электрика / отопление / сантехника (`07-mep-layers.md`)
- [ ] **Interop** — IFC (round-trip Revit/ArchiCAD), DWG, glTF (`08-interop.md`)

---

## Решённые проблемы окружения (как чинили — не повторять ошибок)

- [x] **Unity Hub (MSIX) забивал C: через свой TEMP** → `ENOSPC`. Решение: ставить
  редакторы **офлайн-инсталляторами** Unity (обычные exe слушаются нашего `TEMP=D:\Temp`),
  а не через Hub. URL берём из release API Unity по changeset.
- [x] **Обрыв сети при скачивании** больших инсталляторов (`connection forcibly closed`).
  Решение: качать `curl.exe -L -C -` (докачка) в **цикле** «пока размер файла < размера на
  сервере» с `--retry`.
- [x] **`Start-Process ... "operation was canceled by the user"`** при запуске
  скачанного инсталлятора (Mark-of-the-Web / запрос прав). Решение: `Unblock-File` перед
  запуском.
- [x] **Hub `install-modules` не работает для офлайн-редактора** («module installation
  is only supported for editors installed with Unity Hub»). Решение: Android SDK/NDK/JDK
  ставить напрямую — OpenJDK и NDK распаковкой в фикс-папки, внутренности SDK через
  `sdkmanager` (URL берём из release API Unity). Скрипт: `D:\Installers\install_android_sdk.ps1`.
- [x] **`sdkmanager --licenses` зависал** (интерактивный prompt, stdin не проходил).
  Решение: писать файлы лицензий напрямую в `SDK/licenses/` (`android-sdk-license` и др.
  с известными хэшами), затем `sdkmanager` ставит пакеты без запроса.
