# Мастер-чеклист аудита 2026-08-10

Полный аудит всех подсистем по фактическому коду main (HEAD ~`eab8ec4`). По файлу на
подсистему: текущее состояние → баги с точным указанием места → спеки нового
функционала. Работать сверху вниз внутри приоритета; отмечать здесь и в профильном
документе.

**Легенда:** `[x]` готово · `[~]` в работе · `[ ]` не начато.

---

## P0 — баги с потерей данных или обманом пользователя

- [ ] **B1.** Электрика не сохраняется в проект, а загрузка её уничтожает → [08-electrical.md](08-electrical.md) §Б1
- [ ] **B2.** Текстурная отделка при save/load деградирует в белый цвет → [06-paint-textures.md](06-paint-textures.md) §Б1
- [ ] **B3.** Измерения не сохраняются; после загрузки проекта висят от прошлой сцены → [01-measure.md](01-measure.md) §Б1
- [ ] **B4.** Сплит стены теряет проёмы (T-стык через стену с окном ломает окно) → [02-walls.md](02-walls.md) §Б1
- [ ] **B5.** `Floor.AddHole`: непостроенный мост молча уничтожает плиту → [04-floors.md](04-floors.md) §Б1
- [ ] **B6.** `ProjectMep.Storey` не заполняется в Capture → фильтр этажей мёртв после загрузки → [09-ifc-import.md](09-ifc-import.md) §Б1
- [ ] **B7.** Ряд «Original look» в Paint никогда не срабатывает → [06-paint-textures.md](06-paint-textures.md) §Б2
- [ ] **B8.** Ряд «Corner» (Bevel/Round) стен — UI без эффекта → [02-walls.md](02-walls.md) §Б2
- [ ] **B9.** Калибровка Blueprint выставляет масштаб вне диапазона слайдера → [07-blueprint.md](07-blueprint.md) §Б1
- [ ] **B10.** `Switch To URP` затирает Renderer-ассет вместе с SSAO-фичей → [06-paint-textures.md](06-paint-textures.md) §Б3
- [ ] **B11.** Скрытая (удалённая) стена продолжает формировать стыки соседей → [02-walls.md](02-walls.md) §Б3
- [ ] **B12.** Атомарная запись автосейва (temp + Replace); битый JSON молча перетирается пустым → [12-persistence.md](12-persistence.md) §Б1

## P1 — системные разрывы контракта

- [ ] **S1.** Undo создания объектов: стены/плиты/дыры/измерения/фикстуры/трассы рождаются мимо `EditHistory` → [12-persistence.md](12-persistence.md) §С1 (общий паттерн `CreateCommand`), применение по подсистемам: [01](01-measure.md) §Р1, [02](02-walls.md) §Р1, [04](04-floors.md) §Р1, [08](08-electrical.md) §Р1
- [ ] **S2.** Формат проекта v2: Version-проверка + миграция v1→v2 + электрика + измерения + отделки + Storey → [12-persistence.md](12-persistence.md) §Р1
- [ ] **S3.** Единая политика снапов через `SnapService.SnapFinder` (рулетка, драг узлов стен, инструмент пола) → [01-measure.md](01-measure.md) §Р2, [02-walls.md](02-walls.md) §Р2, [04-floors.md](04-floors.md) §Р2
- [ ] **S4.** Перф: убрать `FindObjectsByType` из телепорта/Scan (реестры), отложенный коллайдер в драге угла пола → [11-locomotion.md](11-locomotion.md) §Р3, [04-floors.md](04-floors.md) §Б2

## P2 — новые инструменты на готовой инфраструктуре (максимальная отдача)

- [ ] **F1.** Инструмент **Openings** (двери/окна): слот 4 радиала, панелизация готова → [03-openings.md](03-openings.md)
- [ ] **F2.** **Лестницы в UI**: `StairParameters` + ручка Yaw + инструмент постановки → [05-stairs.md](05-stairs.md)
- [ ] **F3.** **Комнаты**: `FindClosedLoops` уже написан — площади, «покрасить комнату», авто-потолки → [02-walls.md](02-walls.md) §Р4
- [ ] **F4.** **Потолки** как класс (после комнат) → [02-walls.md](02-walls.md) §Р5
- [ ] **F5.** **Светильники + связь выключатель→светильник** → [08-electrical.md](08-electrical.md) §Р2
- [ ] **F6.** **Экспорт glTF** (первый выход наружу) → [09-ifc-import.md](09-ifc-import.md) §Р2
- [ ] **F7.** **Мультипроекты** (New/Open/Save As, имена файлов) → [12-persistence.md](12-persistence.md) §Р2

## P3 — качество существующих инструментов

- [ ] **Q1.** Рулетка: «уровень», числовой ввод, площадь/периметр цепочки → [01-measure.md](01-measure.md) §Р3–Р5
- [ ] **Q2.** Полы: вставка/удаление вершин, редактирование дыр, снапы → [04-floors.md](04-floors.md) §Р3
- [ ] **Q3.** Paint: отделка по сторонам стены; фильтр таба по типу цели; T4 (метрический верх плиты) → [06-paint-textures.md](06-paint-textures.md) §Р1–Р3
- [ ] **Q4.** Blueprint: план отдельным слоем, путь файла в проект, undo placement → [07-blueprint.md](07-blueprint.md) §Р1–Р3
- [ ] **Q5.** IFC: асинхронный импорт с Progress, счётчики невидимых пропусков, защита от циклов → [09-ifc-import.md](09-ifc-import.md) §Р1, §Б2
- [ ] **Q6.** UI: точка в numpad, живые Readout, скролл панели, кнопки Undo/Redo → [10-ui-system.md](10-ui-system.md) §Р1–Р4
- [ ] **Q7.** Локомоция: телепорт по земле/скану, глушить ход при попапе → [11-locomotion.md](11-locomotion.md) §Р1–Р2
- [ ] **Q8.** Электрика: смета в CSV, несколько щитков, зачистка мёртвого API → [08-electrical.md](08-electrical.md) §Р3–Р4

## P4 — стратегические направления (не начаты)

- [ ] **D1.** AI Placement (мебель через Claude + glTF-каталог, слот 5 радиала) → [13-missing-features.md](13-missing-features.md) §1
- [ ] **D2.** Отопление / сантехника (слоты 9–10, по образцу электрики) → [13-missing-features.md](13-missing-features.md) §2
- [ ] **D3.** Ingest скана: грани MRUK → `WallGraph` → [13-missing-features.md](13-missing-features.md) §3
- [ ] **D4.** Hand tracking → [13-missing-features.md](13-missing-features.md) §4
- [ ] **D5.** IFC round-trip (экспорт), IFC4, имперские единицы → [09-ifc-import.md](09-ifc-import.md) §Р4

## Гигиена (попутно, без отдельных заходов)

- [ ] **H1.** Синхронизировать `docs/CHECKLIST.md` § 2n Текстуры: T1/T2/T3/T5 фактически сделаны, открыты T4/T6
- [ ] **H2.** Ложные комментарии: `TeleportCommand.cs:15` («Measurements stay put» — двигает), `ImportController.cs:302` («не режет проёмы» — режет), `UiPopups.cs:237`, контракт `Polygon.Clean`
- [ ] **H3.** Мёртвый код: `CeilingOffset*` в `ElectricalTypes`, `NextFile/SelectedFileLabel` в Blueprint/Import, `RadialMenu.WithAlpha`, тип `Cycle`
- [ ] **H4.** Непрогнанные проверки на устройстве: E8/E10.6, P6, L5, I12–I17, U9-финал

---

## Карта документов

| Файл | Подсистема |
|---|---|
| [01-measure.md](01-measure.md) | Рулетка |
| [02-walls.md](02-walls.md) | Стены, комнаты, потолки |
| [03-openings.md](03-openings.md) | Проёмы (новый инструмент) |
| [04-floors.md](04-floors.md) | Полы |
| [05-stairs.md](05-stairs.md) | Лестницы |
| [06-paint-textures.md](06-paint-textures.md) | Покраска, текстуры, рендеринг |
| [07-blueprint.md](07-blueprint.md) | Чертёж на полу |
| [08-electrical.md](08-electrical.md) | Электрика |
| [09-ifc-import.md](09-ifc-import.md) | Импорт IFC, экспорт |
| [10-ui-system.md](10-ui-system.md) | UI-система |
| [11-locomotion.md](11-locomotion.md) | Телепорт и ход |
| [12-persistence.md](12-persistence.md) | Персистентность, undo-контракт |
| [13-missing-features.md](13-missing-features.md) | Ненача́тые направления |
