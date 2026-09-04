# 03 — Проёмы: двери, окна, гаражные ворота

Статус: панелизация и модель данных давно в бою (IFC-импорт, персист v2, распашные
двери); **инструмент постановки** — этот документ, v1 в работе (аудит
`docs/audit-2026-08-10/03-openings.md`, ветка `worktree-audit-fixes`).

## Модель: проём — параметр стены (не булев вырез)

Фактическая модель — `WallOpening` в `Core/WallGraph.cs`, хранится в
`WallSegment.Openings`, едет вместе со стеной и **пересчитывается при сплите**
(доли пересчитываются с сохранением мировой позиции):

```csharp
enum OpeningKind { Door, Window, Garage }   // Kind; легаси-IsDoor маппится при чтении
class WallOpening {
    float AlongFraction;      // центр, доля 0..1 вдоль A→B
    float Width, Height;      // метры
    float SillHeight;         // низ от базы стены: дверь/ворота = 0, окно ≈ 0.9
    OpeningKind Kind;
    Vector3 SwingDir, HingeDir; // распашные двери; zero = закрыто
}
```

## Генерация меша — панелизация (без CSG)

`Wall.TriangulateWithOpenings`: простенки, притолока, подоконная часть, откосы,
торцы; стекло окон — submesh 1, столярка (рамы/полотно/подоконник) — submesh 2.
Импортные двери стоят открытыми на 75°.

Imported windows are deliberately normalized to one glass pane and a plain four-bar
frame. `IfcMember`/`IfcPlate` descendants attached below an opening's `IfcWindow` or
`IfcDoor` through `IfcRelAggregates`/`IfcRelNests` are not baked a second time; this
removes exporter-specific mullions and crossbars while preserving standalone members,
plates and curtain walls.

**Гаражные ворота (Kind = Garage):** проём от низа стены во всю ширину, перемычка
сверху как у двери; полотно — секционные ворота: 4 горизонтальные панели с
фасками-рустами в срединной плоскости (submesh столярки, без стекла); распахивания
нет. Из IFC: роликовые/секционные `IfcDoorStyle.OperationType` → Garage.

## Инструмент «Open» (v1, слот 4 радиала, иконка `door-window`)

- **Tabs: Door | Window | Garage** (суб-режимы — только табами).
  - Door: Numeric `Width` (85 см, 60–120), Numeric `Height` (210 см).
  - Window: Numeric `Width` (120), Numeric `Height` (140), Numeric `Sill` (90, 0–200).
  - Garage: Numeric `Width` (250, 180–500), Numeric `Height` (210, 180–300).
- Луч по стене (слой 6, только Wall) → ghost-рамка габарита на грани в точке
  прицела; цвет = валидность. Триггер → `CreateOpeningCommand` (undo: проём
  снимается со стены, redo — возвращается). B по пустоте — Esc в Select.
- **Удаление v1**: прицел на стену рядом с проёмом (центр проёма ближе 25 см к
  точке прицела вдоль стены) + B → `DeleteOpeningCommand` (undo).
- Валидация (`Core/OpeningMath`): простенок до соседнего проёма/торца ≥ 5 см,
  верх проёма ≤ высоты стены − 5 см (перемычка), ширина ≥ 30 см.

## Открывающиеся двери и ворота (v2, issue #50, 2026-08-12)

Полотно двери и панели ворот — **не часть меша стены**, а дочерние объекты-виды,
двигающиеся трансформами (ноль пересборок меша в кадре, правило 12 §4):

```csharp
// Core (RoomPlanner.Walls):
class WallOpening { …; float OpenFraction; }   // 0 закрыто … 1 открыто; персист v3
static class OpeningPose                        // чистая математика, юнит-тесты
{
    float DoorYawDeg(fraction)  => fraction * 100°;        // импортные 75° = 0.75
    PanelPose GaragePanel(h, panels, i, fraction);         // рельс: вверх по откосу,
}                                                          // потом горизонтально под потолок
class OpeningLeafView : MonoBehaviour           // дочерний GO "Leaf#id" у стены
// полотно/панели box-мешами в локальном фрейме (X вдоль, Y вверх, Z поперёк),
// MeshCollider для пика, SetFraction(f, animate) — ease без аллокаций в Update.
```

- `Wall.SyncLeafViews` после триангуляции: дети по Id проёма, меш пересобирается
  только при смене габаритов (драг узлов стены НЕ пересоздаёт полотна), слой = слой
  стены. Двери/ворота; окна без полотна. Рама остаётся в столярке меша стены.
- **Выбор двери** (явный запрос): на leaf-ребёнке — `Selectable`
  (`SelectableKind.Door`) + `OpeningParameters : ISettingsProvider` (паттерн
  StairParameters): Numeric Width/Height (валидация `CanPlace(ignore:self)`,
  `OpeningEditCommand` undo), Segmented Hinge L/R и Swing In/Out (двери),
  **Slider «Open %»** (0–100, БЕЗ undo — это view-действие, не правка сцены),
  Action Delete (→ `DeleteOpeningCommand`). Навешивает `WallGraphRenderer`.
- **Триггер по выбранной двери** (Select) — toggle: анимация к 0 ↔ последний
  Open %. B на выбранной двери — удаление проёма (не hide полотна).
- Персист: `ProjectOpening.Open` (v3). Легаси (v2/IFC): swing задан → 0.75
  (прежний вид «импортные двери открыты на 75°» сохраняется).

## Осознанно отложено (v3)

Открывание окон (створки тем же механизмом), перенос проёма на другую стену,
вставка glTF-моделей, привязка к `DOOR_FRAME`/`WINDOW_FRAME` из MRUK, ручки-гизмо
на полотне.
