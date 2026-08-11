# 25 — Матрица действий и их тестового покрытия (issue #61)

Фидбек шлема 2026-08-13: «новая фича ломает предыдущие, и не всегда заметно» —
регресс ловим **скриптованными жестами**: FakeInput/FakePointer (virtual-швы
`MeasureInput`/`PointerProvider`, введены в 7de0db9 и расширены здесь) гонят
**реальные** контроллеры покадрово, без дублей (правила 12). Для меню используется
настоящий `ToolManager.Update` (вызов по рефлексии при выключенном компоненте —
кадры детерминированы).

**Правило: каждый фикс взаимодействия — с таким регрессионным тестом.** Новая
фича = новая строка в матрице.

## Матрица (✓ покрыто жестом · к — покрыто на уровне команд/схемы · — не покрыто)

| Фича | Действие | Статус | Тест |
|---|---|---|---|
| Select | тап = выбор без сдвига (#46) | ✓ | `SelectGesturePlayTests` |
| Select | второй тап по двери = открыть/закрыть | ✓ | `SelectGesturePlayTests` |
| Select | холд+ведение = драг объекта, 1 undo | ✓ | `SelectGesturePlayTests` |
| Select | драг двери вдоль стены, 1 undo | ✓ | `SelectGesturePlayTests` |
| Select | B = удалить выбранное (undo) | к | `SelectionModel`/`DeleteCommand`-тесты |
| Measure | руки: закрепить A/B, драг вершины | — | (математика — юнитами `MeasureMath`) |
| Measure | ray-режим, глубина стиком | — | |
| Walls | два клика = сегмент с вью | ✓ | `WallGesturePlayTests` |
| Walls | B рвёт цепочку | ✓ | `WallGesturePlayTests` |
| Walls | ручки вершин (T-стык тянет 3 стены) | к | `WallHandlesPlayTests` |
| Walls | снапы к плите/углам | к | `WallSnapToFloorPlayTests` |
| Walls | per-instance параметры (undo) | к | `WallParametersPlayTests` |
| Floor | рисование контура кликами + замыкание | — | |
| Floor | дыра контуром внутри плиты | к | `FloorEditing`/`PaintRoom` |
| Floor | параметры Level/Thickness (undo) | к | `FloorEditingPlayTests` |
| Openings | create/move/delete проёма (undo) | к | `OpeningsToolPlayTests` |
| Openings | постановка проёма триггером по стене | — | |
| Openings | открывание створки/ворот | к | `OpeningLeafPlayTests`, `WallOpeningPlayTests` |
| Stairs | параметры пролёта (undo) | к | `StairParametersPlayTests` |
| Paint | триггер по плите = 1 PaintCommand + undo | ✓ | `PaintGesturePlayTests` |
| Paint | промах/блокировка меню не красят | ✓ | `PaintGesturePlayTests` |
| Paint | покраска комнаты (carve, 1 undo) | к | `PaintRoomPlayTests` |
| Paint | по сторонам стены / текстуры | к | `PaintPlayTests`, round-trip |
| Paint | ластик «Original look» | — | |
| Electric | схема/команды/BOM щитка | к | `ElectricalPlayTests` |
| Electric | жест: розетка триггером по стене | — | |
| Electric | жест: трасса провода + клик по терминалу | — | |
| Import | build/replace/electrical-очистка | к | `ImportPlayTests` |
| Import | маркер: IFC в точку, проект — нет | ✓ | `ImportMarkerPlayTests` |
| Projects | Save/New/Open/Delete через виджеты | ✓ | `ProjectsToolPlayTests` |
| Blueprint | загрузка плана, калибровка 2 парами | — | (файлы устройства) |
| Teleport | A-тап: модель к ногам, 1 undo | ✓ | `NavGesturePlayTests` |
| Teleport | портал левым триггером | — | (команда — к, `TeleportPlayTests`) |
| Teleport | сдвиг всех слоёв + undo | к | `TeleportPlayTests` |
| Undo/Redo | X/Y глобально | ✓ | `NavGesturePlayTests` |
| UI | состояния кнопок (radio/toggle/hover) | к | `MenuButtonStatePlayTests` |
| UI | деструктив: полный холд 0.5 с = 1 срабатывание | ✓ | `WidgetGesturePlayTests` |
| UI | деструктив: ранний отпуск не срабатывает | ✓ | `WidgetGesturePlayTests` |
| UI | степпер: автоповтор при удержании | ✓ | `WidgetGesturePlayTests` |
| UI | слайдер: драг = 1 CommitNumber (before/after) | ✓ | `WidgetGesturePlayTests` |
| UI | попапы (Select-список, numpad, свотчи) | к | `InspectorSchemaPlayTests` (страницы/табы) |
| UI | радиал: открытие, выбор сектора | — | (математика — юнитами `RadialMath`) |
| UI | снап-тумблеры/чип инструмента | — | |

## Как устроен жестовый тест

1. `FakeInput : MeasureInput` — переопределяет virtual-методы (`ConfirmPressed/Held`,
   `ClearPressed`, `TeleportPressed/Held`, `Undo/RedoPressed`, `Pulse*`); флаги
   выставляет тест.
2. `FakePointer : PointerProvider` — луч задаётся тестом.
3. Реальные компоненты собираются в риг (`SetField` рефлексией — как сетап пишет
   сериализованные поля), контроллер гоняется `Tick(false)` покадрово; для меню —
   `ToolManager.Update` по рефлексии при `enabled = false`.
4. Ассерты — на модель (граф/история/финиши), не на визуал; 1 жест = 1 undo-запись,
   где это инвариант.

## Осознанно не покрываем headless

- Вид панелей/шрифты/цвета — UiShots (скриншоты) + шлем.
- Файловые пикеры устройства (Blueprint/Import списки) — шлем.
- Хаптика, комфорт локомоции — шлем.
