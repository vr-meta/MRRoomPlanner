# 14 — Модульность инструментов (параллельная разработка) 🔜

**Цель:** несколько потоков работы (сейчас: «стены, Фаза B» и «чертёж/Blueprint»)
могут идти одновременно, не сталкиваясь в одних файлах. Новый инструмент = новые файлы
+ одна строка регистрации, а не правки по всей обвязке.

## Диагноз (до рефакторинга)

Ядро уже модульное: `RoomPlanner.Core` (геометрия по файлу на тип), `RoomPlanner.Editing`
(выбор/undo, не знает инструментов), контроллеры инструментов — по файлу. Но обвязка
сходится в три «хаба», которые правит каждый поток:

| Хаб | Почему конфликтует |
|---|---|
| `ToolManager` | enum `Tool` + switch, сериализованные ссылки на каждый инструмент, `Execute(MenuAction)` со ВСЕМИ параметрами всех инструментов |
| `MenuAction` + `InspectorPanel` | общий enum действий; пред-собранные группы инспектора (wallGroup/floorGroup) с полями-лейблами на каждый параметр |
| `MeasureSetup` | ~700 строк: сборка рига, палитры и инспектора всех инструментов в одном файле |

## Целевая архитектура

### 1. Схема настроек (Core) — инструмент сам описывает свои параметры

```csharp
// RoomPlanner.Core — чистый C#, юнит-тестируемо
enum SettingKind { Stepper, Cycle }
class SettingField  { string Id, Caption; SettingKind Kind;
                      Func<string> Value; Action Decrease, Increase; }
class SettingsSchema { List<SettingField> Fields;
                       SettingsSchema Stepper(id, caption, value, dec, inc);
                       SettingsSchema Cycle(id, caption, value, next); }
```

Это «Фаза 1.5» из чек-листа (2d): вместо пред-собранных групп инспектор строит ряды
из схемы. Параметр добавляется **только в контроллере инструмента** — `InspectorPanel`
и `MenuAction` не трогаются.

### 2. Реестр инструментов (ToolManager)

```csharp
interface ITool {
    string Id { get; }             // "wall", "blueprint", …
    string PaletteLabel { get; }   // подпись кнопки палитры
    void OnActivate(); void OnDeactivate(); void Tick(bool blocked);
    SettingsSchema GetSettings();  // null/пусто → без панели настроек
}
```

- `ToolManager` держит `ITool[]` и активный индекс; никакого enum `Tool` и switch.
- `MenuAction` сокращается до **глобальных** действий палитры: `SelectTool` (+ индекс
  на кнопке), снап-тумблеры, Scan. Все параметрические действия (Thickness±, Plan± и
  т.п.) уходят в делегаты схемы.
- Кнопки инспектора — `MenuButton.OnClick` (рантайм-делегат); `ToolManager.Execute`
  сначала пробует `OnClick`, иначе enum.

### 3. Инспектор — генерация рядов из схемы (рантайм)

- Setup собирает только **каркас**: фон (с коллайдером — блокирует сцену), тайтл-бар
  (grab), группу выбранного объекта.
- Ряды (`caption [−] value [+]` / `caption value [>]`) `InspectorPanel` строит в
  рантайме из `GetSettings()` активного инструмента; пере-биндится при смене
  инструмента, значения обновляются из `Func<string>` в `RefreshMenu`.

### 4. MeasureSetup → модули (Editor/Setup/)

| Файл | Владение |
|---|---|
| `SetupAssets.cs` | материалы/текстуры (мутация без смены GUID) + фабрика UI-элементов |
| `SetupCoreRig.cs` | риг, указатель, ввод, ретикл, SceneModel, слой 6, EffectMesh |
| `SetupSelectTool.cs` / `SetupMeasureTool.cs` / `SetupWallTool.cs` / `SetupFloorTool.cs` | по инструменту: его префабы + wiring |
| `SetupPalette.cs` | палитра: кнопки инструментов из реестра + глобальные тумблеры |
| `SetupInspector.cs` | каркас инспектора |
| `MeasureSetup.cs` | тонкий оркестратор (порядок вызовов + итоговый диалог) |

## Как добавить новый инструмент (чек-лист разработчика)

1. Контроллер `XxxController : MonoBehaviour, ITool` — свой файл; параметры и
   `GetSettings()` внутри.
2. Чистая математика — новый файл в `RoomPlanner.Core` + EditMode-тесты.
3. `Editor/Setup/SetupXxxTool.cs` — префабы и wiring.
4. Регистрация: одна строка в списке инструментов (`MeasureSetup` + массив в
   `ToolManager`) и кнопка палитры в `SetupPalette` (из `PaletteLabel`).
5. Объекты сцены — регистрировать в `SceneModel`, правки — командами (см. правила 12, §2–3).
6. Раскладка ввода — если добавились жесты, обновить `10-controls.md`.

## Карта владения файлами для текущих потоков

| Поток | Пишет | Не трогает |
|---|---|---|
| **Стены (Фаза B)** | `Core/WallGraph.cs`, `Core/WallMesh.cs`, `Core/Wall.cs`, `WallController`, `SetupWallTool`, свои тесты | Floor*, Blueprint*, InspectorPanel, ToolMenu, MenuAction |
| **Чертёж (Blueprint)** | `Core/BlueprintTransform.cs` (новый), `BlueprintController` (новый), `SetupBlueprintTool` (новый), свои тесты; выселяет `planScale/planOffset` из ToolManager в свой контроллер | Wall*, WallGraph*, InspectorPanel, ToolMenu, MenuAction |
| Общее (редко, интеграция по очереди) | строка регистрации в `MeasureSetup`/`ToolManager`; `10-controls.md` | — |

## Что остаётся связанным (осознанно)

- **Общие параметры** (`WallThickness` делят Wall и Floor; `Level`) — пока живут в
  `ToolManager` как общий стор; схемы ссылаются на его геттеры/методы. Когда появится
  полноценный стор настроек проекта (Project I/O), переедут туда.
- **Палитра снапов** (Cor/Edg/Grd/Ang/Scan) — глобальная, через `MenuAction`.
- **Сцена** `Measure.unity` — генерируется SetupRig'ом; конфликт решается перегенерацией.

## Порядок миграции

1. `Core/SettingsSchema` + тесты (ничего не ломает).
2. `ITool` расширен; четыре контроллера реализуют Id/PaletteLabel/GetSettings.
3. `MenuButton.OnClick` + `toolIndex`; `MenuAction` сокращён до глобальных.
4. `ToolManager` на реестр; параметрические ветки `Execute` → публичные методы
   (`AdjustWallThickness` и т.п.), на которые ссылаются схемы.
5. `InspectorPanel` — рантайм-ряды; `ToolMenu` — массив кнопок инструментов.
6. Разрезка `MeasureSetup` на `Editor/Setup/*`.
7. SetupRig + полный прогон тестов; проверка на устройстве (панель обязана выглядеть
   и работать как раньше).

После миграции потоки «стены» и «чертёж» пересекаются только строкой регистрации.
