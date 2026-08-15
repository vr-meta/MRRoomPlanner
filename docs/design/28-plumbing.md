# 28 — Сантехника v1: стояки, трубы, выводы, трап 🔜

Слой `Plumbing` из [07-mep-layers](07-mep-layers.md) — черновая (rough-in) канализация:
поставить стояки, развести от них трубы по полу/стенам/потолку и закончить выводами
под приборы. Эпик — issue #88 (подзадачи #89–#95). Шаблон — проверенная электрика
([19-electrical.md](19-electrical.md)): типы → трубная математика → вью → таб-контроллер
→ смета → персист. Принцип прежний: **всё — параметрические данные**, меши процедурные.

## Модель данных (Core, namespace `RoomPlanner.Plumbing`)

```csharp
enum PipeDiameter { D110, D50, D40 }        // стояк/унитаз · раковина/душ/стиралка · подводки
enum PlumbFixtureKind { ToiletOutlet, SinkOutlet, FloorDrain }
enum OutletAngle { Deg90, Deg45 }           // прямой вывод из стены или косой (классика под унитаз)

// PipeSpec (static): Radius (0.055 / 0.025 / 0.020 м), Label ("D110"…), TypeCount, Next.
// PlumbingDefaults: высотные пресеты выводов (Toilet 0.18, Sink 0.45 от Level до оси),
//   StubLength 0.15, TerminalSnapRadius 0.10, MinPointStep 0.03, PlaceDebounce 0.25 c,
//   FixtureClearance 0.05, DrainSize 0.15, DrainDepth 0.08, ConnectionAllowance 0.15,
//   Reserve 10 % (0–30, шаг 5) — зеркало ElectricalDefaults.

// PipeRoute : MonoBehaviour (как WireRoute): Points (мировые), Diameter, IsRiser,
//   StartFixtureId / EndFixtureId (Selectable-id фикстуры ИЛИ стояка), Build() —
//   трубка радиуса диаметра, 12 граней, winding наружу; Length; MoveBy/MovePoint/
//   TryMoveAttachedEnd — как у провода. Стояк = PipeRoute(IsRiser, 2 точки, D110).

// PipeMath (static, юнит-тесты):
//   OrthoElbowLow(prev, next, result) — колено канализации: горизонтальный пролёт на
//     ВЫСОТЕ НИЖНЕЙ из двух точек (магистрали лежат по полу, подъёмы вертикальны) —
//     зеркало WireMath.OrthoElbow, который ведёт по верху;
//   ClosestOnSegment(a, b, p) — снап к оси стояка на любой высоте (тройник);
//   CountElbows(points, out deg90, out deg45) — классификация колен по углу между
//     сегментами: < 22.5° — прямая, 22.5–67.5° — 45°, дальше — 90°.

// PlumbFixture : MonoBehaviour (один класс, Kind, как ElectricFixture) — ЛОКАЛЬНЫЙ меш,
//   постановка = transform (drag без re-cook). Меш вывода — трубка WireMath.BuildTube
//   по локальной полилинии + раструб (кольцо чуть шире у устья):
//   ToiletOutlet D110: 90° — патрубок из стены (+Z) длиной 0.15;
//                      45° — патрубок 0.08 и колено 45° вниз 0.10;
//   SinkOutlet D50 — те же два варианта;
//   FloorDrain — короб 150×150 мм врезан в пол (решётка заподлицо, корпус на
//     DrainDepth вниз) + боковой порт D50 (терминал) для трассы от душа/стиралки.
//   TerminalWorld — конец патрубка / порт трапа. BaseLevel — как у электрики.

// PlumbingBom (static, чистая функция): по живым нескрытым PipeRoute из SceneModel —
//   метры по диаметрам (Σ длин + 0.15 м × привязанный конец) + запас % → строки
//   "D110 — 6.4 m"; счёт колен 90°/45° по полилиниям; кол-во фикстур по видам.
//   Смета показывается в Describe() выбранного СТОЯКА (аналог щитка), Reserve % —
//   per-instance ряд стояка.
```

## UX (перенос решений электрики)

- **Один инструмент «Plumb»** — резервный слот радиала 10 (иконка `pipe`), тинт слоя
  `UiTokens.LayerPlumbing` #4DA6FF — ретикл/акценты, не сами объекты: трубы — серый ПП,
  фикстуры белые. Суб-режимы — **табы**: `Riser / Pipe / Outlet / Drain`.
- **Riser**: луч в пол (|n.y|>0.7), клик — вертикальная D110-труба от пола до потолка
  (потолок — луч вверх: ближайшая грань «смотрящая вниз» ≥2 м; фолбэк Level+WallHeight).
- **Pipe**: точка за точкой по **стенам, полу и потолку** (пол — валидная поверхность,
  в отличие от электрики: канализация лежит по полу). Ortho (дефолт) — колено
  `OrthoElbowLow`; Free — прямые. Финиш — B; дебаунс 250 мс + шаг 3 см. Настройки:
  `Diameter` Segmented (110/50/40, дефолт 50), `Route` Segmented (Ortho/Free).
- **Снапы конца трубы** (приоритет): терминал фикстуры → **ось стояка на любой высоте**
  (курсор прыгает на ближайшую точку оси, связь пишется id стояка — тройник) →
  свободный конец трубы ТОГО ЖЕ диаметра (продолжение, паттерн #81, одна команда
  undo) → сырая точка. Клик по терминалу/стояку завершает трассу сам. Старт от
  терминала задаёт дефолтный диаметр: Toilet — D110, Sink/Drain — D50.
- **Outlet**: луч + триггер по грани стены; высота снапится к пресету вида
  (грип — свободная вертикаль), ghost-превью, отказ+хаптика на промах/пересечение.
  Настройки: `Type` Segmented (Toilet/Sink), `Angle` Segmented (90°/45°),
  `Height` Slider (см).
- **Drain**: клик по полу — трап 150×150, ориентация identity (поворот — v1.1),
  высота не настраивается (решётка в плоскости пола).
- **Редактирование**: Select — драг фикстур, ручки точек труб (обобщение
  `RouteHandles`), B — удаление командой. Смена таба/инструмента с трассой ≥2 точек —
  авто-финиш.

## Персист (формат v5) и интеграция

- `ProjectData.CurrentVersion = 5`: `ProjectPlumbFixture { Id, Kind, Angle, Position,
  Rotation, BaseLevel }`, `ProjectPipe { Points, Diameter, IsRiser, StartId, EndId,
  Reserve }`. Id — verbatim (перепривязка концов бесплатно). Старые файлы читаются
  (пустая сантехника), новые старым читателем — отказ (правило Б2).
- `PlumbController.RestorePlumbFixture / RestorePipe` — фабрики восстановления
  (зеркало электрики, с фолбэком на голую сборку компонентов без префаба).
- `TeleportCommand` двигает слой (фикстуры MoveBy + BaseLevel, трассы MoveBy целиком —
  привязанные концы не едут дважды); `ClearScene` уничтожает зарегистрированное.
- Общие точки (минимум, в последнюю очередь): `Selectable` (Kind/Resolve/Describe),
  `TeleportCommand`, `ProjectStore`, `ToolManager` (поле + реестр + слот 10),
  `RigContext`, `SetupAssets` (PipeMat/PlumbFixtureMat), `MeasureSetup`,
  `10-controls.md`.

## Скелет классов и размещение

| Файл | Что |
|---|---|
| `Core/PlumbingTypes.cs` | enums, PipeSpec, PlumbingDefaults |
| `Core/PipeMath.cs` | OrthoElbowLow, ClosestOnSegment, CountElbows |
| `Core/PipeRoute.cs` | вью трубы/стояка (меш+коллайдер, OnDestroy освобождает меш) |
| `Core/PlumbFixture.cs` | вью выводов и трапа (один класс, Kind+Angle) |
| `Core/PlumbingBom.cs` | смета (чистая функция, юнит-тесты) |
| `Plumbing/PlumbController.cs` | ITool «plumb»/«Plumb», табы, постановка, снапы |
| `Plumbing/PlumbFixtureParameters.cs` | per-instance схема фикстуры |
| `Plumbing/PipeRouteParameters.cs` | per-instance схема трубы (+Reserve у стояка) |
| `Plumbing/PipeHandles.cs` | ручки точек трубы (по образцу RouteHandles) |
| `Editor/Setup/SetupPlumbTool.cs` | префабы PlumbFixture/PipeRoute + wiring |

## Осознанно отложено (v1.1+)

- **Уклон 2 %** горизонтальных участков (#95): расчёт по полилинии, предупреждение в
  Describe/подсветка — БЕЗ автокоррекции.
- Подача воды (PEX 16/20/25) как отдельный набор диаметров; коллекторы.
- Поворот трапа; виды трапов; сифоны/ревизии; фитинги как отдельные меши (сейчас
  колена — жёсткие изломы трубки, читается достаточно).
- Пересечения труб/стен, авто-обход препятствий; изоляция.
- Layer-чипы (eye/solo) — вместе с LayerManager из 07.
