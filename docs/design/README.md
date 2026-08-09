# Проектные решения — индекс

Решения разбиты по модулям (один файл = один модуль) для удобного поиска.
Статус: ✅ реализовано (скелет) · 🔜 ближайшее · 🧭 на будущее.

| # | Документ | Модуль |
|---|---|---|
| 00 | [overview](00-overview.md) | Стек и сквозной принцип |
| 01 | [measure](01-measure.md) | Измерения (рулетка, уровень) ✅🔜 |
| 02 | [walls](02-walls.md) | Стены (граф, стыки, высота) 🔜 |
| 03 | [openings](03-openings.md) | Проёмы: двери/окна 🔜 |
| 04 | [surfaces-materials](04-surfaces-materials.md) | Отделка: метрические UV, зоны 🔜 |
| 05 | [ai-placement](05-ai-placement.md) | AI-расстановка мебели (Claude) 🧭 |
| 06 | [project-format](06-project-format.md) | Формат проекта (JSON + glTF) 🧭 |
| 07 | [mep-layers](07-mep-layers.md) | Слои + инженерные сети (электрика/отопление/сантехника) 🧭 |
| 08 | [interop](08-interop.md) | Импорт/экспорт: **IFC**, DWG, glTF 🧭 |
| 09 | [project-structure](09-project-structure.md) | **Корневой:** Дом→Этажи→Комнаты, захват и сборка, Ingest 🔜 |
| 10 | [controls](10-controls.md) | Карта кнопок (раскладка ввода) ✅ |
| 11 | [object-operations](11-object-operations.md) | Манипуляции по типам объектов (роадмап редактирования) ✅🔜 |
| 12 | [coding-rules](12-coding-rules.md) | **Свод правил к коду** (из аудита 2026-08-09) ✅ |
| 13 | [phase-b-wallgraph](13-phase-b-wallgraph.md) | **План Фазы B:** WallGraph и правка стен 🔜 |
| 14 | [modularity](14-modularity.md) | **Модульность инструментов** (реестр, схема настроек, параллельные потоки) 🔜 |

## Порядок реализации

1. **Measure** — рулетка ✅, уровень, размеры 🔜
2. **Surfaces & Materials** — метрические UV, покраска/текстуры, двусторонние стены, зоны
3. **Wall Builder** — граф стен, стыки/T, высота
4. **Openings** — двери/окна (панелизация)
5. **Blueprint** — подложка чертежа + калибровка
6. **AI Placement** — расстановка мебели (Claude) + glTF-каталог
7. **Project I/O** — сохранение/загрузка + экспорт glTF
8. **MEP + слои** 🧭 — слои + общие примитивы (Fixture / RouteSpline / смета): электрика → отопление → сантехника
9. **Interop** 🧭 — импорт/экспорт IFC (round-trip с Revit/ArchiCAD), DWG-подложка

Верхнеуровневая карта — в [`../../ARCHITECTURE.md`](../../ARCHITECTURE.md).
