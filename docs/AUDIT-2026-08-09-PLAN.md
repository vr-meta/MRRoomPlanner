# План работ по итогам аудита (2026-08-09)

Документ для передачи в работу агенту/исполнителю. Основан на полном аудите кода
(4 независимых ревью: Core-геометрия, Editing/undo, рантайм-контроллеры, проектный
уровень). Все находки проверены по фактическому коду на момент коммита `0c4932e`
(+ параллельные `025807f`, `560da4b`). Тесты на старте: EditMode 32/32, PlayMode 13/13 —
зелёные, но слепые зоны описаны ниже.

## Правила выполнения (обязательно)

- Работать по `CLAUDE.md`: код/комментарии/UI — английский; отмечать прогресс в
  `docs/CHECKLIST.md` (раздел 2e и новые под-задачи).
- После каждого WP прогонять тесты headless: `powershell -File ci/run-tests.ps1 -Mode All`
  (редактор Unity должен быть закрыт). Ненулевой exit code = стоп и разбор.
- Каждый фикс сопровождать регрессионным тестом (EditMode в `RoomPlanner.Tests`,
  PlayMode в `RoomPlanner.Tests.Play`), кроме пунктов, где явно указано «тест не нужен».
- Если правится состав рига/сериализованные поля — перед сборкой запускать
  `powershell -File ci/unity-run.ps1 -Method RoomPlanner.EditorTools.CiTools.SetupRig`.
- Порядок WP — это порядок приоритета. Внутри WP задачи можно переставлять,
  зависимости указаны явно.

---

## WP1 — Единый владелец времени жизни объектов (P0, краш)

**Проблема.** Два несогласованных механизма удаления: `DeleteCommand` прячет объект
(`SetActive(false)`), а Measure/Wall/Floor-контроллеры жёстко `Destroy()` те же объекты,
минуя историю. Плюс проверка живости в командах не работает: поля типизированы
интерфейсом `ISelectable`, поэтому перегруженный `==` UnityEngine.Object не вызывается
и уничтоженный объект проходит `!= null`.

**Воспроизводимый краш:** подвинуть измерение в Select (MoveCommand в истории) →
переключиться в Measure → нажать B (`Destroy` мимо истории) → нажать X →
MissingReferenceException; команда при этом теряется из обоих стеков и история
рассинхронизирована до конца сессии.

### Задачи

1.1. **Unity-aware проверка живости в командах.**
  - `Assets/RoomPlanner/Scripts/Editing/Model/EditCommands.cs:19-20, 31-32` —
    `_target != null` (интерфейсная ссылка). Ввести в `ISelectable` свойство
    `bool IsAlive` (реализация в `Selectable`: `this != null && gameObject != null`)
    либо типизировать команды конкретным `Selectable`. Все Do/Undo — no-op, если не жив.
  - Затронуто также: `SceneModel.Register/Unregister/Items`
    (`Assets/RoomPlanner/Scripts/Editing/Model/SceneModel.cs:23-33`) — те же
    интерфейсные сравнения.

1.2. **Exception-safety EditHistory.**
  - `Assets/RoomPlanner/Scripts/Core/EditHistory.cs:27, 40-54` — команда снимается со
    стека ДО вызова `Undo()/Redo()`; исключение теряет её из обоих стеков.
    Перейти на peek → вызов → pop, либо try/catch с возвратом команды на место и
    пробросом. `Execute()`: при исключении в `Do()` не класть в стек.

1.3. **Все удаления — через команды.**
  - Точки жёсткого `Destroy` в обход истории:
    - `Assets/RoomPlanner/Scripts/Measure/MeasureController.cs:281-289` (`TrimMeasurements`, лимит 40),
      `:291-299` (delete по B), `:315` (`ClearLast`);
    - `Assets/RoomPlanner/Scripts/Floors/FloorController.cs:109-119` (`DeleteLast`);
    - `Assets/RoomPlanner/Scripts/Walls/WallController.cs` (`FinishChain`, удаление в цепочке).
  - Решение: либо эти пути тоже создают `DeleteCommand` (delete-as-hide), либо при
    жёстком `Destroy` вызывается новый метод `SceneModel.PurgeDead()` /
    `EditHistory.PurgeWhere(cmd => !cmd.TargetAlive)`, чистящий историю и реестр.
    Выбрать одно и зафиксировать решение в `docs/design/11-object-operations.md`.

1.4. **`Selectable.OnDestroy` → авто-`Unregister` + purge истории.**
  - Сейчас `Selectable` не снимает себя с учёта; корректность держится на шести вручную
    спаренных вызовах Unregister+Destroy. Добавить `OnDestroy`-хук.

1.5. **Контроллеры не должны видеть скрытые («удалённые») объекты.**
  - Снап к невидимым стенам: `WallController.cs:161-188` (`TrySnapToNode` — проверка
    `w == null` не отсекает неактивные объекты);
  - Магнит к скрытым измерениям: `MeasureController.cs:342-372`
    (`FindEndpointHandle`, `TrySnapToEndpoint`);
  - Скрытые измерения занимают лимит в `TrimMeasurements` (`MeasureController.cs:281-289`)
    и обрабатываются в `DedupeMarkers` (`:320-329`);
  - `FloorController.DeleteLast` удаляет скрытую плиту вместо последней видимой.
  - Фильтровать по `IsHidden`/`activeInHierarchy`; в идеале — контроллеры читают
    `SceneModel.Items` вместо приватных списков (пункт «Фаза B» чек-листа, строка 116).

1.6. **Регрессионные тесты (PlayMode):**
  - undo после `Destroy` цели — не бросает, история консистентна;
  - redo `DeleteCommand` (повторное скрытие) — сейчас вообще не покрыт;
  - снап/трим игнорируют скрытые объекты;
  - `Register` уже-уничтоженного объекта; пикинг после Unregister-без-Destroy.

**Критерий приёмки WP1:** сценарий из «воспроизводимого краша» не бросает; все тесты
зелёные; ни одного пути `Destroy` для Selectable-объектов без синхронизации
истории/реестра (проверить grep'ом по `Destroy(`).

---

## WP2 — Ориентация треугольников (winding) Floor/Wall (P0, точность)

**Проблема.** Меши вывернуты: рейкаст проходит сквозь ближнюю грань и попадает в
дальнюю → точка пика систематически смещена на толщину элемента (20 см при дефолте).
Для приложения с точностью «cm» — функциональный баг. Ни одного теста на нормали нет;
PlayMode-тест маскирует симптом допуском.

### Задачи

2.1. **Floor: исправить winding всех граней.**
  - `Assets/RoomPlanner/Scripts/Core/Floor.cs:85-90` — верхняя грань
    `Quad(t, 0, 1, 2, 3)` даёт нормаль (0,−1,0) (вниз), нижняя — вверх, боковые — внутрь.
    Перевернуть порядок индексов: верх — наружу (+Y), низ — наружу (−Y), борта — наружу.

2.2. **Wall: сделать ориентацию независимой от знака `OutwardSign`.**
  - `Assets/RoomPlanner/Scripts/Core/Wall.cs:229-256` (Triangulate) +
    `Wall.cs:117-121` (`OutwardSign`): при oSign = −1 (типовой случай, interior со
    стороны −rn — как во всех текущих тестах) меш полностью вывернут; внутренняя грань
    смотрит от пользователя. Треугольники строить по фактической геометрии
    (inner-грань → к interior-точке), а не по порядку вершин, зависящему от oSign.
  - Комментарий `Wall.cs:26` («winding is irrelevant») — удалить/исправить: он верен
    только для рендера с cull off, но не для MeshCollider-рейкастов.

2.3. **Тесты на нормали и точку хита (EditMode + PlayMode).**
  - EditMode: для каждой грани Wall/Floor ассерт знака нормали треугольника
    (`Cross(v1-v0, v2-v0)`), обе стороны interior (oSign = ±1).
  - PlayMode: рейкаст сверху в пол попадает в ВЕРХНЮЮ грань (y == level, а не
    level − thickness); луч из комнаты в стену — в ближнюю грань, нормаль хита к лучу.

2.4. **Убрать маскирующий допуск.**
  - `Assets/RoomPlanner/Tests/Play/GeometryPickingPlayTests.cs:117`
    (`TryPick_HitsRealFloorFromAbove`, допуск `InRange(-0.25, 0.05)` с комментарием
    «winding-dependent») → ужесточить до малого эпсилона вокруг ожидаемой грани.

**Критерий приёмки WP2:** тесты 2.3 зелёные; допуск 2.4 ужесточён; визуально при
lit-материале с включённым culling пол виден сверху, стена — изнутри комнаты.

---

## WP3 — Производительность горячих путей (P1, Quest 72–120 Гц)

### Задачи

3.1. **Драг без пересборки меша каждый кадр.**
  - Цепочка: `Assets/RoomPlanner/Scripts/Editing/SelectController.cs:66` →
    `Selectable.MoveBy` → `Wall.cs:105-110` → полный `Build()` →
    `Wall.cs:223-226` (новые списки вершин) + `Wall.cs:265-270`
    (`sharedMesh = null; sharedMesh = _mesh` — перепекание MeshCollider каждый кадр).
    Аналогично `Floor.cs:98-102`.
  - Решение: во время драга двигать `transform` (дёшево), параметры и
    коллайдер коммитить один раз в `EndDrag`. API: `Wall/Floor.BeginTransformMove()` /
    `CommitMove(delta)` либо перенести translate на уровень `Selectable`.

3.2. **`Physics.RaycastAll` → `RaycastNonAlloc`.**
  - `Assets/RoomPlanner/Scripts/Editing/Model/SceneModel.cs:44` — аллокация каждый кадр
    в дефолтном инструменте (Select активен с запуска, `ToolManager.cs:86`).
    Кэшированный буфер `RaycastHit[16]`.

3.3. **Лейбл измерения: обновлять только при смене значения.**
  - `Assets/RoomPlanner/Scripts/Measure/MeasurementLabel.cs:31-40` — каждый кадр превью
    новая строка + безусловный `text.ForceMeshUpdate()`. Кэшировать последнее округлённое
    значение см; `ForceMeshUpdate` убрать (TMP сам перестраивается при смене text) либо
    вызывать только при фактической смене строки.

3.4. **Утечка текстуры плана пола.**
  - `Assets/RoomPlanner/Scripts/Floors/FloorController.cs:137-141` — каждая активация
    Floor-инструмента: `new Texture2D` + `LoadImage` без `Destroy` старой; плюс мутация
    shared-ассета `planMaterial`. Решение: грузить один раз / по изменению mtime файла;
    `Destroy(old)`; работать с runtime-копией материала
    (`new Material(planMaterial)`), не с ассетом.

3.5. **`DedupeMarkers` — по мутации, не каждый кадр.**
  - `Assets/RoomPlanner/Scripts/Measure/MeasureController.cs:79` — выполняется до
    early-out по `blocked`; `Measurement.cs:40` — 2× `GetComponent<Renderer>` на маркер
    за кадр (~80 вызовов при лимите 40), O(n²) скан. Кэшировать Renderer'ы, вызывать
    dedupe только при добавлении/удалении/перемещении точек.

3.6. **Утечка `Mesh` при удалении стен/полов.**
  - `Wall.cs:59, 64`, `Floor.cs:35` — `new Mesh` без `OnDestroy { Destroy(_mesh); }`.
    Добавить в оба класса. Тест не нужен.

**Критерий приёмки WP3:** при драге стены нет аллокаций списков/пересборки коллайдера
(проверка Profiler'ом или логом в Build()); тесты зелёные; поведение MoveBy/Undo
round-trip не изменилось (существующие Play-тесты).

---

## WP4 — Ввод и UX-дефекты (P1)

### Задачи

4.1. **Клик сквозь панели меню/инспектора.**
  - `Assets/RoomPlanner/Scripts/Tools/ToolManager.cs:107-124` — `overMenu` истинен
    только при хите в `MenuButton`/`InspectorGrab`; фоны панелей без коллайдеров —
    их снимает `Assets/RoomPlanner/Scripts/Editor/MeasureSetup.cs:481, 494, 585`.
    Сценарий: триггер между кнопками инспектора ставит точку на стену позади панели.
  - Решение: оставить BoxCollider на фоновых квадах панелей (слой 2), `overMenu` —
    любой хит по слою 2. После правки — SetupRig (меняется состав рига).

4.2. **Смена инструмента / блокировка посреди драга.**
  - `SelectController.cs:38` — `OnDeactivate` → `EndDrag(record: false)`: применённый
    сдвиг остаётся, но MoveCommand не записан → Undo пропускает этот шаг. Решение:
    записывать команду, если `_draggedFar` (или откатывать сдвиг).
  - `SelectController.cs:47-52` — при `blocked` драг «замерзает», не завершаясь; при
    возврате луча объект телепортируется. Завершать драг при входе в blocked либо
    не накапливать дельту за время блокировки.
  - `ToolManager.cs:99-100` — X/Y (undo/redo) срабатывают во время активного драга —
    запретить, пока `_dragging`.

4.3. **Коллайдеры точки измерения остаются выключенными.**
  - `MeasureController.cs:163-164` (drag start → `SetInteractable(false)`) и `:67-73`
    (`OnDeactivate` нулит `_dragging` без восстановления). Восстанавливать в
    `OnDeactivate`.

4.4. **Конфликт грипа: снап оси vs захват панели.**
  - Грип = `MeasureInput.SnapHeld` (`MeasureController.cs:131`, `WallController.cs:80`)
    и одновременно захват панели (`ToolManager.cs:114-120`). Сценарий: измерение с
    зажатым грипом + луч задел тайтл-бар → панель прыгает к указателю, tick
    инструмента пропущен. Решение: захват панели только по фронту нажатия грипа при
    наведении на панель (edge, не held), либо игнорировать захват при активном
    незавершённом действии инструмента. Обновить `docs/design/10-controls.md`.

4.5. **Мелочи (без тестов, по коду):**
  - `SelectController.cs:69, 163` — `_draggedFar` защёлкивается: драг «туда-обратно»
    пишет нулевую MoveCommand (мёртвый шаг undo + сброс redo). Сравнивать итоговую
    дельту с порогом при записи.
  - `SelectController.cs:147-155` + `Core/MeasureMath.cs:47-55` — фолбэк
    `_lastCursor = origin + dir*2` не на плоскости драга → боковой скачок при первом
    пересечении. Инициализировать `_lastCursor` только валидным пересечением
    (см. также 5.6).
  - `MeasureController.cs:270` — `_preview.Set(...)` без null-guard (в отличие от `:170`).
  - `MeasureController.cs:80-85` — `HidePlus()` без `UpdateHover(null, null)`: «+»
    появляется с залипшим hover-масштабом 1.4×.
  - `MeasureInput.cs:92` — хаптика всегда `RTouch`; добавить остановку вибрации в
    `OnDisable`.
  - `Selectable.cs:84-98` — тинт не красит TMP-бейдж (нужен `_FaceColor`);
    `SetPropertyBlock(null)` стирает весь MPB — заменить на восстановление только
    цветовых свойств (задел под будущую покраску).

**Критерий приёмки WP4:** ручная проверка на устройстве по пунктам чек-листа 2e
(«Проверка на устройстве», строка 115) + прогон тестов. По 4.1 — обязателен SetupRig
перед сборкой.

---

## WP5 — Устойчивость геометрии к «грязному» вводу (P1/P2)

Все правки — в `RoomPlanner.Core`, покрывать EditMode-тестами.

5.1. **Miter limit.** `Wall.cs:154-163` — |mvec| = 1/cos(θ/2) не ограничен: разворот
  ~175° даёт шип ~4.5 м, ~177° — ~9 м (порог отсечения только denom ≤ 1e-3).
  Ввести лимит (стандартно ~2×thickness), выше — фолбэк на bevel.

5.2. **Дубли соседних точек центрлинии.** `Wall.cs:127` (`RightNormal` → фиктивный
  `Vector3.forward` при нулевом сегменте), `:151-152` (нормализация нулевого dir).
  Схлопывать точки ближе эпсилона (~1 мм) на входе `Build`. Дубль-клик в MR — реальный
  сценарий.

5.3. **Разворот на ~180°.** `Wall.cs:157, 161` — fallback `(n0+n1).normalized` даёт
  нулевой вектор → сечение схлопывается в точку. Обработать явно (защёлка на
  перпендикуляр одного из сегментов).

5.4. **Валидация параметров.** `Wall.cs:85-91`, `Floor.cs:67-68` — отрицательные
  thickness/height молча переворачивают геометрию; отрицательный planScale молча
  заменяется на 1. Клэмп/ассерты + тесты.

5.5. **Алиасинг входа `Build`.** `Wall.cs:74-83` — `wall.Build((List<Vector3>)wall.Points, …)`
  сначала `_pts.Clear()` очистит вход (Points — тот же список); `centerline == null` → NRE.
  Копировать вход до Clear (как уже делает `Rebuild()`, `:100`) + null-guard.

5.6. **`MeasureMath.RayPlaneY`:** `MeasureMath.cs:52` — `t <= 0` отвергает луч,
  начинающийся ровно на плоскости; сменить на `t < 0` (или эпсилон). Связано с
  мёртвой зоной драга (4.5).

**Пробелы покрытия, закрыть вместе с WP5:** Bevel-join (не покрыт вообще);
Inner-режим офсета (не покрыт); Center для `Build`; `OutwardSign` c interior по другую
сторону; Floor UV-маппинг (planScale/planOriginX/planOriginZ — главная фича класса,
ноль тестов); `EditHistory.Record` очищает redo (`EditHistory.cs:37` — не проверено);
LIFO-порядок undo; `Clear()`; `SnapToAngleXZ`/`SnapToGridXZ`/`ClosestPointOnSegment`
с вырожденным входом; `FormatDistanceCm` с отрицательными.

---

## WP6 — Тестируемость: границы сборок (P1, делать до/вместе с тестами WP1/WP4)

**Проблема.** `RoomPlanner.Editing.asmdef` лежит в `Scripts/Editing/Model/`, поэтому
продакшн-`Selectable.cs` и `SelectController.cs` (в `Scripts/Editing/`, зависят от
Measure/Walls/TMP) компилируются в Assembly-CSharp, куда тестовые asmdef ссылаться не
могут. Play-тесты гоняют рукописные дубликаты (`FakeSelectable`, `GeoProbe` в
`GeometryPickingPlayTests.cs`), которые уже дрейфуют от оригинала (у `FakeSelectable.SetHidden`
нет реального guard'а; `GeoProbe.WorldBounds` игнорирует детей).

### Задачи

6.1. Создать asmdef `RoomPlanner.App` (или расширить `RoomPlanner.Editing`) и перенести
  туда `Selectable`, `SelectController`, контроллеры Measure/Walls/Floors/Tools.
  Зависимости: Core, Editing, Unity.TextMeshPro, Oculus/Meta asmdef'ы (проверить имена
  по `Packages/packages-lock.json`). `RoomPlanner.Tests.Play` → добавить ссылку.
6.2. Удалить дубликаты `FakeSelectable`/`GeoProbe` из тестов, тестировать реальные
  компоненты (в т.ч. hover/select/drag state machine, `Describe()`, MPB-подсветку).
6.3. Добавить Editor-asmdef для `Assets/RoomPlanner/Scripts/Editor/` (сейчас
  Assembly-CSharp-Editor, держится на autoReferenced у Core/Editing).
6.4. После переноса — SetupRig + полный прогон (перенос между сборками может
  сбросить сериализованные ссылки — проверить сцену).

---

## WP7 — Инфраструктура и гигиена (P2)

7.1. **MeasureSetup: не пересоздавать ассеты.**
  - `MeasureSetup.cs:195-196, 226-227, 250-251, 305, 337, 382, 410` —
    `AssetDatabase.DeleteAsset` + `CreateAsset` меняет GUID 12 материалов/префабов при
    каждом запуске (ломает внешние ссылки, шумит в диффах). Создавать только если нет,
    иначе мутировать существующий ассет (GUID сохраняется).
7.2. **MeasureSetup: хрупкости.**
  - `DestroyAllNamed("Inspector"/"ToolMenu"/"MeasureRig")` сносит любые одноимённые
    объекты — сузить до объектов с маркер-компонентом;
  - слой 6 захардкожен — проверять/создавать имя «Selectable» в TagManager;
  - `TryEnableEffectMeshColliders` рефлексией включает все bool-поля с подстрокой
    «collider» — сузить до известного поля MRUK;
  - ~40 строковых `FindProperty` — минимум обернуть в проверку с понятной ошибкой.
7.3. **Явно зафиксировать IL2CPP.** `ProjectSettings/ProjectSettings.asset:691` —
  `scriptingBackend: {}` (билд корректен — в APK `libil2cpp.so`/arm64 — но держится на
  дефолте). Выставить явно через Player Settings.
7.4. **Единый путь сборки APK.** Сейчас три: `BuildTool.BuildQuest` → `Build/RoomPlanner.apk`
  (правит applicationIdentifier), `CiTools.BuildAndroid` → `Build/MRRoomPlanner.apk`
  (не правит), ручной Ctrl+B → `Measure.apk`. Оставить один (CiTools), BuildTool —
  тонкая обёртка. Починить мёртвый фолбэк сцен `BuildTool.cs:38` (ищет сцены в
  `Assets/RoomPlanner`, а сцена — `Assets/Measure.unity`).
7.5. **Пакеты/чек-лист.** Удалить `com.unity.multiplayer.center` из
  `Packages/manifest.json`; актуализировать раздел 1 `docs/CHECKLIST.md`
  (строки 31-34 — проект давно собирается, пункты стоят `[ ]`).
7.6. **Мусор на диске** (не в индексе, git чист): `Measure.apk`, `TempAssembly.dll`,
  `MRRoomPlanner_BurstDebugInformation_DoNotShip/`, `TestResults-*.xml`, `ci-*.log` —
  удалить локально по желанию.

---

## Зависимости и порядок

```
WP1 (краш undo)  ──┐
WP2 (winding)      ├─ независимы, делать первыми (P0)
                   │
WP6 (asmdef) ──────┼─ до написания Play-тестов WP1.6/WP4, иначе тесты снова на дублях
WP3 (перфоманс) ───┤   (3.1 меняет MoveBy — прогнать Play-тесты round-trip)
WP4 (ввод/UX) ─────┤   (4.1 требует SetupRig; 4.2 зависит от WP1.2)
WP5 (геометрия) ───┤   независим, чистый Core
WP7 (инфра) ───────┘   в любой момент; 7.1 — до следующего массового SetupRig
```

**Definition of Done (весь план):**
1. `ci/run-tests.ps1 -Mode All` — зелёный, число тестов выросло (новые регрессионные).
2. Сценарии-крашеры из WP1/WP4 не воспроизводятся.
3. Рейкаст в пол/стену попадает в ближнюю грань (WP2), допуски тестов ужесточены.
4. В Profiler нет пер-кадровых аллокаций в Select-режиме и при драге (WP3).
5. `docs/CHECKLIST.md` обновлён (2e + новые под-задачи), решения зафиксированы в
   `docs/design/11-object-operations.md` и `10-controls.md`.
6. Проверка на устройстве: выбрать/перетащить/удалить + undo/redo (чек-лист 2e:115).
