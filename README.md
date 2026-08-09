# MR Room Planner

MR-приложение для Meta Quest 3 / 3S (Horizon OS): сканирование помещения и работа с
интерьером в смешанной реальности. Строится поверх официального сэмпла Meta
**Virtual Home** (MR Utility Kit) и разворачивается по этапам.

Текущий этап — **Задача 1: рулетка** (аналог приложения Meta *Layout*):
ставим точку A → точку B → показываем расстояние между ними в MR.

---

## Требования

- **Устройство:** Meta Quest 3 или 3S (нужны цветной passthrough и Scene API).
- **Unity:** 6000.0.x (Unity 6). Тот же поток, что у сэмплов `Unity-MRUtilityKitSample`.
- **Пакеты (Package Manager / Asset Store):**
  - **Meta XR Core SDK**
  - **Meta MR Utility Kit (MRUK)**
  - TextMeshPro (входит в Unity).
- Ничего из перечисленного в репозиторий не коммитим — только наш код (`Assets/RoomPlanner`).

> Скрипты лежат в `Assets/` и попадают в стандартную сборку `Assembly-CSharp`,
> поэтому отдельный `.asmdef` не нужен: `OVRInput`, MRUK и TMPro видны автоматически
> после импорта SDK.

---

## Установка проекта (один раз)

1. Создайте новый Unity 6 проект (шаблон **3D (URP)** или Built-in — на ваш выбор).
2. Импортируйте **Meta XR Core SDK** и **MR Utility Kit** через Package Manager.
3. Импортируйте сэмпл **Virtual Home** из пакета MRUK (Package Manager → MRUK →
   Samples → Import) — он станет базовой сценой (перекраска комнаты, EffectMesh,
   готовый rig с камерой/контроллерами).
4. Скопируйте папку `Assets/RoomPlanner` из этого репозитория в `Assets/` проекта.
5. Настройки проекта:
   - **Build target:** Android.
   - **XR Plug-in Management → OpenXR (Android)**, feature groups Meta Quest.
   - Включите **Passthrough** и запрос разрешения **Scene** (`com.oculus.permission.USE_SCENE`).
   - Player Settings → минимальный API level по требованию Meta XR SDK.
6. На устройстве один раз пройдите **Space Setup** (Settings → Environment Setup),
   чтобы у комнаты появилась Scene Model.

---

## Подключение рулетки к сцене

Открыв сцену Virtual Home:

1. Убедитесь, что у компонента **EffectMesh** (спавнит меши стен/пола/потолка/мебели)
   включена генерация **коллайдеров** и все меши на отдельном слое, напр. `Scene`.
   По этим коллайдерам рулетка делает `Physics.Raycast`.
2. Создайте пустой объект `MeasureRig` и повесьте на него:
   - `MeasureController`
   - `SceneRaycaster` (в `Scene Mask` выберите слой `Scene`)
   - `PointerProvider` (в `Controller Anchor` перетащите `RightControllerAnchor`
     из OVRCameraRig)
   - `MeasureInput`
3. Соберите **префаб одного измерения** `Measurement`:
   - пустой объект с компонентом `Measurement`;
   - дочерний `LineRenderer` (тонкая линия) → в поле `Line`;
   - два маленьких маркера (сферы) → в поля `Marker A` / `Marker B`;
   - объект с `MeasurementLabel` (внутри `TMP_Text`, world-space) → в поле `Label`.
   Перетащите префаб в `MeasureController → Measurement Prefab`.
4. (Опционально) назначьте `Reticle` — маленький объект-прицел, который следует за
   точкой попадания луча.

### Управление

| Действие | Контроллер | Редактор (для теста) |
|---|---|---|
| Поставить точку | правый триггер (`PrimaryIndexTrigger`) | `Space` |
| Отменить последнее / очистить незавершённое | кнопка `B` (`Button.Two`) | `Backspace` |

Первый триггер ставит A, второй — B и фиксирует измерение с подписью расстояния.
Между A и B тянется предпросмотр линии за прицелом.

---

## Дорожная карта

См. `ARCHITECTURE.md`. Кратко: рулетка → покраска поверхностей → текстуры
(обои/плитка/пол) → возведение виртуальных стен → подложка чертежа → расстановка
мебели через LLM (structured output) и замена примитивов на glTF-модели.
