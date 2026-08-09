# 18 — Импорт IFC (Revit → редактируемая модель) 🔜

Конкретизация [08-interop](08-interop.md) до реализуемого MVP. Ветка: `worktree-ifc-import`.

**Цель:** принести проект из Revit (экспорт в IFC) в шлем и получить **редактируемые
параметрические** стены/полы нашей модели — не «немой меш». Дальше пользователь
докрашивает/двигает/достраивает обычными инструментами.

## Референс-файл

`Project1.ifc` (Revit 24.2, IFC2X3, мм, 10 МБ, ~206k сущностей) — реальный жилой дом:

| Сущность | Кол-во | Использование |
|---|---|---|
| `IfcWallStandardCase` | 41 | все с представлением `Axis` (`IfcPolyline`, 2 точки) + толщина из `IfcMaterialLayer` + высота из `IfcExtrudedAreaSolid` |
| `IfcColumn` | 31 | все прямоугольные 300×300 (`IfcRectangleProfileDef` + экструзия) |
| `IfcSlab` | 11 | профиль контура (`IfcArbitraryClosedProfileDef`/`IfcRectangleProfileDef`) + толщина |
| `IfcBuildingStorey` | 5 | отметки уровней (0 / 3150 / 6100 / 9050 / 11300) |
| `IfcDoor` / `IfcWindow` / `IfcOpeningElement` | 17/16/36 | размеры прямо в сущности; связи `IfcRelVoidsElement`/`IfcRelFillsElement` |
| `IfcFurnishingElement` | 21 | Brep-меши (IKEA), имена в типе |
| `IfcFlowTerminal` | 21 | сантехника (кириллица в `\X2\…\X0\`) |
| `IfcSpace` | 0 | Revit не экспортировал комнаты (настройка экспорта) |

## Решения

1. **Свой лёгкий парсер STEP-подмножества в `RoomPlanner.Core`**, без Xbim:
   - Xbim на IL2CPP/Android — риск (reflection, вес); наше подмножество — ~15 типов сущностей.
   - Парсер = текстовый формат ISO-10303-21: `#id=TYPE(arg,arg,…);`, аргументы —
     строки/числа/enum `.X.`/ссылки `#n`/вложенные списки. Полностью юнит-тестируемо.
   - Никакого бэкенда: 10 МБ текста на устройстве парсится за секунды; строим индекс
     `id → сырая запись`, разрешаем ссылки лениво только для нужных типов.
2. **Импорт на устройстве**: `.ifc` кладётся в `Download` (как планы Blueprint),
   выбор файла — тот же паттерн `PlanFileLocator` (расширим до generic-локатора).
3. **Маппинг (MVP):**
   | IFC | → Наша модель |
   |---|---|
   | `IfcBuildingStorey.Elevation` | уровни (Level) этажей |
   | `IfcWallStandardCase` `Axis` + `IfcMaterialLayerSetUsage` (толщина) + экструзия (высота) | `Wall` (лента, 2-точечная осевая) |
   | `IfcColumn` с `IfcRectangleProfileDef` | **короткий сегмент `Wall`** (ось = длинная сторона профиля через центр, толщина = короткая) — красится/выбирается как стена, нового типа объекта нет |
   | `IfcSlab` (профиль контура) | `Floor.BuildOutline` + толщина |
   - Проёмы в MVP **не вырезаем** (модуль Openings не готов) — стены сплошные; данные
     дверей/окон в файле полные, подключим Фазой D.
4. **Координаты:** мм → м (×0.001); IFC right-handed Z-up → Unity Y-up:
   `(x, y, z) → (x, z, y)`. Позиции элементов — композиция цепочки `IfcLocalPlacement`
   (элемент → этаж → здание → площадка); 2D-точки `Axis` поднимаются через неё в мир.
5. **Импорт = команды**: элементы рождаются через `SceneModel`/`EditHistory` одной
   составной командой — весь импорт отменяется одним Undo.
6. **Навигация по дому (телепорт)** — в MR камеру не двигаем (passthrough):
   двигаем **модель относительно пользователя**. Все импортированные объекты — под общим
   корнем `Building`; телепорт = луч в точку пола → `Building` смещается так, чтобы точка
   оказалась у ног; выбор этажа — степпером (скрывает уровни выше глаз / показывает один
   этаж). Математика смещения — в Core, тестируется.

## Скелет классов

```
Core/Ifc/StepTokenizer.cs   — разбор одной записи: id, тип, дерево аргументов
                              (строки с \X2\-юникодом, числа, .ENUM., #ref, $, *, списки)
Core/Ifc/StepFile.cs        — построчный ридер файла → индекс id→запись (лениво парсим аргументы)
Core/Ifc/IfcImporter.cs     — обход: Storeys / Walls (axis+thickness+height+placement) /
                              Columns (rect) / Slabs (outline) → ImportedBuilding
Core/Ifc/ImportedBuilding.cs— чистые данные: ImportedStorey/ImportedWall/ImportedSlab (метры, Unity-оси)
Core/BuildingNav.cs         — математика телепорта (смещение корня, выбор видимого этажа)
Tools/ImportController.cs   — ITool "Import": ряды File (листать .ifc) / Load / Storey;
                              строит Wall/Floor из ImportedBuilding командами
Editor/Setup/SetupImportTool.cs + строка в реестре ToolManager.Start + SetupPalette
```

## Осознанно откладываем

- Двери/окна/проёмы — Фаза D (Openings); данные в файле уже есть.
- Круглые колонны (`IfcCircleProfileDef`) — MVP пропускает с пометкой в статусе импорта.
- Мебель (Brep-меши), MEP-точки, `IfcSpace`, лестницы, `IfcBuildingElementProxy` — визуальный
  импорт мешей отдельным шагом позже.
- Round-trip (экспорт обратно в IFC) — после формата проекта (док 06).
- IFC4 (пока только IFC2X3 — то, что даёт Revit по умолчанию в CoordinationView).

## Тестовая стратегия

- Юнит-тесты токенизатора/ридера — на инлайн-фрагментах STEP.
- Тесты `IfcImporter` — на **мини-fixture** (несколько КБ), собранной из реальных цепочек
  `Project1.ifc` (стена #150 со всеми зависимостями, колонна #277, слэб #236, этажи) —
  кладётся в `Tests/Fixtures/`. Сам 10-МБ файл в репозиторий не кладём.
- Play-тесты: Load fixture → в сцене N стен/полов, undo снимает всё одной командой.
