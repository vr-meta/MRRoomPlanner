using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Core.Furniture;
using RoomPlanner.Editing;
using RoomPlanner.Measure;
using RoomPlanner.Tools;

namespace RoomPlanner.Furniture
{
    /// <summary>
    /// The Furn tool (design/27 §3, issue #71): pick a collection, pick an item, aim at
    /// the floor or a wall and place it 1:1. Two tabs — Place and Move — because the
    /// headset feedback was that placing furniture is only half the job; moving what is
    /// already there (including furniture imported from IFC) is the other half.
    ///
    /// Everything about WHERE a piece may sit lives in Core/FurniturePlacement; this
    /// controller only feeds it the aim hit and turns the result into scene objects and
    /// undo commands.
    /// </summary>
    public class FurnitureController : MonoBehaviour, ITool
    {
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private MeasureInput input;
        [SerializeField] private ToolManager manager;
        [SerializeField] private SceneModel sceneModel;
        [SerializeField] private SceneRaycaster raycaster;
        [SerializeField] private FurnitureLibrary library;
        [SerializeField] private FurnitureLoader loader;
        [SerializeField] private Transform reticle;          // shared aim point
        [SerializeField] private LineRenderer ghost;         // footprint rectangle
        [SerializeField] private Transform itemsRoot;        // parent for placed furniture
        [SerializeField] private Material placeholderMat;    // stands in for a missing pack
        [SerializeField] private Material partitionMat;      // generated slat screens (#86)

        private const int SelectableLayer = 6;
        /// <summary>Stick rotation speed, degrees per second (a full turn in four seconds —
        /// fast enough to aim a sofa, slow enough to stop on a detent).</summary>
        private const float RotateSpeed = 90f;
        /// <summary>Stick deflection that counts as "turning" (below it the stick is idle).</summary>
        private const float RotateDeadzone = 0.35f;
        /// <summary>How far around a piece we look for a wall to back onto.</summary>
        private const float WallProbe = 1.2f;
        /// <summary>Directions sampled when looking for that wall (every 45°).</summary>
        private const int ProbeRays = 8;

        private int _tab;                    // 0 Place · 1 Move
        private int _collection;
        private int _category;               // 0 = All, then CategoriesOf order
        private int _sub;                    // 0 = All, then SubcategoriesOf order
        private int _item;
        private float _yaw;
        private bool _snapToWall = true;

        private SettingsSchema _settings;
        private readonly List<FurnitureItem> _items = new();
        private readonly List<FurnitureCategory> _categories = new();
        private readonly List<string> _subcategories = new();
        private string[] _subNames = System.Array.Empty<string>();
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private readonly Vector3[] _corners = new Vector3[4];
        private string[] _collectionNames = System.Array.Empty<string>();
        private string[] _categoryNames = System.Array.Empty<string>();
        private string[] _itemNames = System.Array.Empty<string>();
        private int _catalogStamp = -1;      // rebuild the cached option lists when it changes

        public string Id => "furniture";
        public string PaletteLabel => "Furn";
        public string IconId => "furniture";

        // ---- catalog access ---------------------------------------------------------

        private FurnitureCatalog Catalog => library != null ? library.Catalog : null;

        private FurnitureCollection CurrentCollection
        {
            get
            {
                var catalog = Catalog;
                if (catalog == null || catalog.Count == 0) return null;
                return catalog.Collections[Mathf.Clamp(_collection, 0, catalog.Count - 1)];
            }
        }

        private FurnitureItem CurrentItem
        {
            get
            {
                RefreshLists();
                if (_items.Count == 0) return null;
                return _items[Mathf.Clamp(_item, 0, _items.Count - 1)];
            }
        }

        /// <summary>
        /// Rebuild the option lists when the catalog or the selection changed. Lists are
        /// cached because a Select popup asks for its options every frame it is open, and
        /// rebuilding string arrays per frame would allocate (rules 12 §4.1).
        /// </summary>
        private void RefreshLists()
        {
            var catalog = Catalog;
            if (catalog == null) return;
            int stamp = catalog.Count * 1000003 + _collection * 1009 + _category * 31 + _sub;
            if (stamp == _catalogStamp) return;
            _catalogStamp = stamp;

            _collectionNames = new string[catalog.Count];
            for (int i = 0; i < catalog.Count; i++) _collectionNames[i] = catalog.Collections[i].DisplayTitle;

            var collection = CurrentCollection;
            if (collection == null)
            {
                _categoryNames = new[] { "All" };
                _subNames = new[] { "All" };
                _itemNames = System.Array.Empty<string>();
                _items.Clear();
                return;
            }

            catalog.CategoriesOf(collection.Id, _categories);
            _categoryNames = new string[_categories.Count + 1];
            _categoryNames[0] = "All";
            for (int i = 0; i < _categories.Count; i++) _categoryNames[i + 1] = _categories[i].ToString();

            _category = Mathf.Clamp(_category, 0, _categoryNames.Length - 1);
            FurnitureCategory? filter = _category == 0 ? null : _categories[_category - 1];

            // Subcategories only make sense inside one category ("Sofa" under Seating);
            // with All selected the second level is not offered.
            if (filter.HasValue) catalog.SubcategoriesOf(collection.Id, filter.Value, _subcategories);
            else _subcategories.Clear();
            _subNames = new string[_subcategories.Count + 1];
            _subNames[0] = "All";
            for (int i = 0; i < _subcategories.Count; i++) _subNames[i + 1] = _subcategories[i];
            _sub = Mathf.Clamp(_sub, 0, _subNames.Length - 1);

            string subFilter = _sub == 0 ? null : _subcategories[_sub - 1];
            catalog.ItemsOf(collection.Id, filter, subFilter, _items);

            _itemNames = new string[_items.Count];
            for (int i = 0; i < _items.Count; i++) _itemNames[i] = _items[i].Name;
            _item = _items.Count == 0 ? 0 : Mathf.Clamp(_item, 0, _items.Count - 1);
        }

        // ---- settings panel ---------------------------------------------------------

        public SettingsSchema GetSettings()
        {
            if (_settings != null) return _settings;

            var place = new SettingsSchema()
                .Select("collection", "Collection", () => { RefreshLists(); return _collectionNames; },
                    () => _collection,
                    i => { _collection = i; _category = 0; _sub = 0; _item = 0; _page = 0; _catalogStamp = -1; RefreshLists(); })
                .Select("category", "Category", () => { RefreshLists(); return _categoryNames; },
                    () => _category,
                    i => { _category = i; _sub = 0; _item = 0; _page = 0; _catalogStamp = -1; RefreshLists(); })
                .Select("sub", "Type", () => { RefreshLists(); return _subNames; },
                    () => _sub,
                    i => { _sub = i; _item = 0; _page = 0; _catalogStamp = -1; RefreshLists(); })
                // Pictures, not names: "Sofa (classic)" vs "Sofa (fabric)" means nothing
                // until it is in the room (headset feedback 2026-08-15, #83). The grid shows
                // one PAGE — a pack has hundreds of items and a panel taller than the user
                // is not a menu (design/20 §3).
                .Stepper("page", "Page", () => $"{_page + 1}/{PageCount}",
                    () => SetPage(_page - 1), () => SetPage(_page + 1))
                .PreviewSwatch("item", "Item",
                    () => { RefreshLists(); return PageSize(); },
                    i => library != null ? library.PreviewOf(ItemAt(i)) : null,
                    i => ItemAt(i)?.Name,
                    () => _item - _page * ItemsPerPage,
                    i => _item = _page * ItemsPerPage + i,
                    PickerVersion)
                .Stepper("yaw", "Rotate", () => $"{_yaw:0}°",
                    () => AdjustYaw(-PlacementOptions.DefaultYawStep),
                    () => AdjustYaw(PlacementOptions.DefaultYawStep))
                .Toggle("snap", "Snap to wall", () => _snapToWall, v => _snapToWall = v)
                // Size AND provenance in one row: the page is at the eight-row ceiling
                // (design/20 §3), and both facts belong to the same selected item anyway.
                .Readout("size", "Item", () =>
                {
                    if (library != null && !library.Ready) return library.Status;
                    var item = CurrentItem;
                    if (item == null) return "—";
                    string size = $"{item.Size.x * 100f:0} × {item.Size.z * 100f:0} × {item.Size.y * 100f:0} cm";
                    var c = CurrentCollection;
                    string licence = c == null ? null : (c.CommercialUse ? c.License : c.License + " (NC)");
                    return string.IsNullOrEmpty(licence) ? size : $"{size} · {licence}";
                });

            var move = new SettingsSchema()
                .Readout("howmove", "How to", () => "aim furniture · hold Trigger = drag")
                .Readout("howturn", "Rotate", () => "stick ← → turns it · Grip = 15° steps")
                .Toggle("snapmove", "Snap to wall", () => _snapToWall, v => _snapToWall = v)
                .Readout("target", "Selected", () =>
                    _dragView != null ? _dragView.Describe() : "—");

            _settings = SettingsSchema.Tabbed(
                new[] { "Place", "Move" },
                () => _tab, i => { _tab = Mathf.Clamp(i, 0, 1); EndDrag(record: true); },
                place, move);
            return _settings;
        }

        /// <summary>Chips per page — 6 columns × 4 rows, the grid the panel renders.</summary>
        public const int ItemsPerPage = 24;

        private int _page;

        private int PageCount => Mathf.Max(1, Mathf.CeilToInt(_items.Count / (float)ItemsPerPage));

        /// <summary>Items on the current page (the last one is usually short).</summary>
        private int PageSize() =>
            Mathf.Clamp(_items.Count - _page * ItemsPerPage, 0, ItemsPerPage);

        private FurnitureItem ItemAt(int indexOnPage)
        {
            int i = _page * ItemsPerPage + indexOnPage;
            return i >= 0 && i < _items.Count ? _items[i] : null;
        }

        private void SetPage(int page)
        {
            int clamped = Mathf.Clamp(page, 0, PageCount - 1);
            if (clamped == _page) return;
            _page = clamped;
            // Keep a valid selection on the page the user is looking at.
            _item = Mathf.Min(_page * ItemsPerPage, Mathf.Max(0, _items.Count - 1));
        }

        /// <summary>
        /// Identifies WHAT the picker is showing. The panel rebuilds its rows when this
        /// changes — without it, switching category left the previous category's pictures
        /// on screen (headset feedback 2026-08-15).
        /// </summary>
        private int PickerVersion()
        {
            RefreshLists();
            return ((_collection * 31 + _category) * 31 + _sub) * 1021 + _page * 7 + _items.Count;
        }

        private void AdjustYaw(float delta) =>
            _yaw = FurniturePlacement.QuantizeYaw(_yaw + delta, PlacementOptions.DefaultYawStep);

        /// <summary>Degrees to turn this frame from the right stick; 0 when it rests.</summary>
        private float StickTurn()
        {
            float x = input.Thumbstick().x;
            if (Mathf.Abs(x) < RotateDeadzone) return 0f;
            return x * RotateSpeed * Time.deltaTime;
        }

        private float _lastDetent = float.NaN;

        /// <summary>One haptic tick per 15° crossed — the same detent language sliders and
        /// steppers use, so a turn feels stepped even while it is continuous.</summary>
        private void PulseOnDetent(float yaw)
        {
            float detent = Mathf.Round(yaw / PlacementOptions.DefaultYawStep);
            if (!float.IsNaN(_lastDetent) && Mathf.Abs(detent - _lastDetent) >= 1f)
                input.Pulse(0.3f, 0.008f);
            _lastDetent = detent;
        }

        // ---- tool lifecycle ---------------------------------------------------------

        public void OnActivate() { }

        public void OnDeactivate()
        {
            EndDrag(record: true);
            EndRotate(record: true);
            HideAim();
        }

        private void HideAim()
        {
            if (ghost != null) ghost.enabled = false;
            if (reticle != null) reticle.gameObject.SetActive(false);
        }

        public void Tick(bool blocked)
        {
            if (pointer == null || input == null || sceneModel == null) return;
            if (blocked)
            {
                EndDrag(record: true);
                EndRotate(record: true);
                HideAim();
                return;
            }

            if (_tab == 1) TickMove();
            else TickPlace();
        }

        // ---- place ------------------------------------------------------------------

        private void TickPlace()
        {
            var item = CurrentItem;
            bool aimed = TryAim(out var hit, out var normal);

            if (reticle != null)
            {
                reticle.gameObject.SetActive(aimed);
                if (aimed) reticle.position = hit;
            }

            if (!aimed || item == null)
            {
                if (ghost != null) ghost.enabled = false;
                if (input.ClearPressed() && manager != null) manager.ActivateTool("select");
                return;
            }

            // Stick left/right turns the piece before it lands — the panel stepper is the
            // precise path, the stick is the fast one (design/20 §2: every continuous
            // manipulation keeps a discrete twin).
            float turn = StickTurn();
            if (turn != 0f)
            {
                _yaw = FurniturePlacement.Normalize360(_yaw + turn);
                if (input.SnapHeld())
                    _yaw = FurniturePlacement.QuantizeYaw(_yaw, PlacementOptions.DefaultYawStep);
                PulseOnDetent(_yaw);
            }

            var pose = Solve(item, hit, normal);
            ShowGhost(pose, item.Size, pose.Valid ? UiTokens.Selected : UiTokens.Danger);

            if (input.ConfirmPressed())
            {
                if (pose.Valid) Place(item, pose);
                else input.Pulse(0.2f, 0.01f);   // refusal tick — the ghost is already red
            }

            if (input.ClearPressed() && manager != null) manager.ActivateTool("select");
        }

        private FurniturePose Solve(FurnitureItem item, Vector3 hit, Vector3 normal)
        {
            var options = PlacementOptions.Default;
            options.SnapToWall = _snapToWall;
            options.WallMountHeight = item.Anchor == FurnitureAnchor.Wall
                ? FurniturePlacement.DefaultMountHeight(item.Category)
                : -1f;

            var pose = FurniturePlacement.Solve(hit, normal, item.Size, item.Anchor, _yaw, options);
            if (pose.Valid && _snapToWall && item.Anchor == FurnitureAnchor.Floor)
                TrySnapToNearbyWall(ref pose, item.Size);
            return pose;
        }

        /// <summary>
        /// Look for a wall to back onto by probing outwards every 45°. A wall's face is
        /// whatever the probe hits, so this works for our parametric walls AND for scan
        /// meshes — the user does not care which one their room is made of.
        /// </summary>
        private void TrySnapToNearbyWall(ref FurniturePose pose, Vector3 size)
        {
            float reach = PlacementOptions.DefaultSnapDistance + Mathf.Max(size.x, size.z) * 0.5f;
            var origin = pose.Position + Vector3.up * Mathf.Min(0.5f, size.y * 0.5f);
            float best = float.MaxValue;
            Vector3 bestPoint = default, bestNormal = default;

            for (int i = 0; i < ProbeRays; i++)
            {
                float a = i * (360f / ProbeRays) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                int n = Physics.RaycastNonAlloc(new Ray(origin, dir), _hits, reach,
                    ~0, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < n; h++)
                {
                    var hit = _hits[h];
                    if (hit.distance >= best) continue;
                    if (!IsWallSurface(hit)) continue;
                    best = hit.distance;
                    bestPoint = hit.point;
                    bestNormal = hit.normal;
                }
            }

            if (best == float.MaxValue) return;
            FurniturePlacement.TrySnapBackToWall(ref pose, size, bestPoint, bestNormal,
                PlacementOptions.DefaultSnapDistance);
        }

        private static bool IsWallSurface(RaycastHit hit)
        {
            if (Mathf.Abs(hit.normal.y) > 1f - FurniturePlacement.HorizontalDot) return false;
            var sel = hit.collider.GetComponentInParent<Selectable>();
            if (sel == null) return true;                       // scan mesh or ground: still a wall face
            return sel.Kind == SelectableKind.Wall;             // never back onto other furniture
        }

        private void Place(FurnitureItem item, in FurniturePose pose)
        {
            var view = Spawn(item, pose);
            if (view == null) return;

            var selectable = view.GetComponent<Selectable>();
            sceneModel.Register(selectable);
            sceneModel.History.Record(new CreateCommand(selectable));
            input.Pulse(0.6f, 0.02f);
        }

        /// <summary>
        /// Create the placed object immediately and let the model stream in underneath:
        /// the undo entry and the collider must exist the moment the user pulls the
        /// trigger, not a few frames later when glTFast finishes parsing.
        /// </summary>
        public FurnitureItemView Spawn(FurnitureItem item, in FurniturePose pose)
        {
            if (item == null) return null;
            var go = new GameObject($"Furniture ({item.Name})") { layer = SelectableLayer };
            go.transform.SetParent(itemsRoot != null ? itemsRoot : transform, worldPositionStays: true);

            var view = go.AddComponent<FurnitureItemView>();
            view.ProceduralMaterial = partitionMat;
            view.Bind(item, null, library != null ? library.Catalog.Find(item.CollectionId) : null);
            view.ApplyPose(pose);
            go.AddComponent<Selectable>();

            // Generated pieces are complete the moment they are bound; only model-backed
            // items wait for glTFast.
            if (loader != null && !item.IsProcedural) LoadModelInto(view, item);
            return view;
        }

        private async void LoadModelInto(FurnitureItemView view, FurnitureItem item)
        {
            var model = await loader.InstantiateAsync(item, view.transform);
            // The user can undo/delete while the model is still parsing.
            if (view == null || !view) { if (model != null) Destroy(model); return; }
            if (model == null) return;
            model.layer = SelectableLayer;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = SelectableLayer;
            view.Bind(item, model, library != null ? library.Catalog.Find(item.CollectionId) : null);
        }

        // ---- project restore (#73) --------------------------------------------------

        /// <summary>
        /// Recreate a piece from a project file. The catalog may still be loading (the
        /// library reads manifests asynchronously), so the work is deferred until it is
        /// ready — and if the pack is genuinely gone the piece comes back as a labelled
        /// placeholder of the right size, never as a silent hole in the room.
        /// </summary>
        public void RestoreItem(Core.Project.ProjectFurniture saved)
        {
            if (saved == null || sceneModel == null) return;
            StartCoroutine(RestoreRoutine(saved));
        }

        private System.Collections.IEnumerator RestoreRoutine(Core.Project.ProjectFurniture saved)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (library != null && !library.Ready && Time.realtimeSinceStartup < deadline)
                yield return null;

            var pose = new FurniturePose { Position = saved.Position, Yaw = saved.Yaw, Valid = true };
            var item = Catalog?.FindItem(saved.Key);
            FurnitureItemView view;

            if (item != null)
            {
                view = Spawn(item, pose);
            }
            else
            {
                Debug.LogWarning($"[Furniture] {saved.Key} is not in any installed collection — " +
                                 "restored as a placeholder");
                view = SpawnPlaceholder(saved, pose);
            }

            if (view == null) yield break;
            var selectable = view.GetComponent<Selectable>();
            if (selectable != null)
            {
                if (!string.IsNullOrEmpty(saved.Id)) selectable.Id = saved.Id;
                sceneModel.Register(selectable);
            }
        }

        /// <summary>A grey box the size of the missing piece: the layout survives, and the
        /// user can see (and move, and delete) what the project expected to be there.</summary>
        private FurnitureItemView SpawnPlaceholder(Core.Project.ProjectFurniture saved, in FurniturePose pose)
        {
            var item = new FurnitureItem
            {
                Id = saved.Key, Name = string.IsNullOrEmpty(saved.Name) ? "Missing item" : saved.Name,
                Size = saved.Size, Anchor = (FurnitureAnchor)saved.Anchor,
                Category = FurnitureCategory.Decor, Fit = FurnitureFit.Stretch,
                CollectionId = null, File = null,
            };

            var view = Spawn(item, pose);
            if (view == null) return null;

            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Placeholder";
            Destroy(box.GetComponent<BoxCollider>());     // the view already owns the pick box
            box.transform.SetParent(view.transform, false);
            box.transform.localScale = saved.Size;
            box.transform.localPosition = new Vector3(0f, saved.Size.y * 0.5f, 0f);
            box.layer = SelectableLayer;
            if (placeholderMat != null) box.GetComponent<MeshRenderer>().sharedMaterial = placeholderMat;
            return view;
        }

        // ---- move -------------------------------------------------------------------

        private FurnitureItemView _dragView;
        private Selectable _dragSelectable;
        private Vector3 _dragStart;
        private float _dragStartYaw;
        private float _dragPlaneY;

        private void TickMove()
        {
            bool aimed = TryAim(out var hit, out _);
            if (reticle != null)
            {
                reticle.gameObject.SetActive(aimed);
                if (aimed) reticle.position = hit;
            }

            if (_dragView != null)
            {
                if (input.ConfirmHeld()) { DragTo(hit, aimed); return; }
                EndDrag(record: true);
                return;
            }

            var target = AimedFurniture();
            if (target != null)
            {
                // Turning without picking it up: aim at the piece and push the stick.
                float turn = StickTurn();
                if (turn != 0f) RotateAimed(target, turn);
                else EndRotate(record: true);

                ShowGhost(new FurniturePose
                {
                    Position = target.transform.position,
                    Yaw = target.Yaw,
                    Valid = true,
                }, target.Size, UiTokens.Selected);

                if (input.ConfirmPressed()) BeginDrag(target);
                if (input.ClearPressed())
                {
                    var sel = target.GetComponent<Selectable>();
                    if (sel != null) sceneModel.History.Execute(new DeleteCommand(sel));
                    input.Pulse(0.6f, 0.02f);
                }
                return;
            }

            EndRotate(record: true);   // the ray left the piece — settle whatever it turned
            if (ghost != null) ghost.enabled = false;
            if (input.ClearPressed() && manager != null) manager.ActivateTool("select");
        }

        // Rotating a placed piece with the stick. One command per gesture: the turn is
        // recorded when the stick returns to centre (or the aim leaves), never per frame.
        private FurnitureItemView _rotView;
        private float _rotStartYaw;

        private void RotateAimed(FurnitureItemView target, float turn)
        {
            if (_rotView != target)
            {
                EndRotate(record: true);
                _rotView = target;
                _rotStartYaw = target.Yaw;
            }
            float yaw = target.Yaw + turn;
            if (input.SnapHeld()) yaw = FurniturePlacement.QuantizeYaw(yaw, PlacementOptions.DefaultYawStep);
            target.SetYaw(yaw);
            PulseOnDetent(target.Yaw);
        }

        private void EndRotate(bool record)
        {
            if (_rotView == null) return;
            var view = _rotView;
            float from = _rotStartYaw;
            _rotView = null;
            if (view == null || !view) return;

            if (!record) { view.SetYaw(from); return; }
            if (Mathf.Abs(Mathf.DeltaAngle(from, view.Yaw)) < 1e-3f) return;
            sceneModel.History.Record(
                new FurnitureYawCommand(view.GetComponent<Selectable>(), view, from, view.Yaw));
            input.Pulse(0.5f, 0.02f);
        }

        /// <summary>The furniture under the ray — catalog pieces AND imported IFC furniture
        /// (anything wearing a FurnitureItemView or a Selectable of that kind).</summary>
        private FurnitureItemView AimedFurniture()
        {
            if (!sceneModel.TryPick(pointer.GetRay(), out var picked, out _)) return null;
            if (picked is not Selectable s || !s.IsAlive) return null;
            return s.GetComponent<FurnitureItemView>();
        }

        private void BeginDrag(FurnitureItemView view)
        {
            _dragView = view;
            _dragSelectable = view.GetComponent<Selectable>();
            _dragStart = view.transform.position;
            _dragStartYaw = view.Yaw;
            _dragPlaneY = _dragStart.y;
            input.Pulse(0.4f, 0.015f);
        }

        private void DragTo(Vector3 hit, bool aimed)
        {
            // Carry the piece across its own floor plane: the pointer's height must not
            // lift a sofa off the ground (rules 12 §5.4 — no fallback-driven deltas).
            var ray = pointer.GetRay();
            float denom = ray.direction.y;
            if (Mathf.Abs(denom) < 1e-4f) return;
            float t = (_dragPlaneY - ray.origin.y) / denom;
            if (t <= 0f) return;
            var target = ray.GetPoint(t);
            if (aimed && Mathf.Abs(hit.y - _dragPlaneY) < 0.05f) target = hit;

            var pose = new FurniturePose { Position = target, Yaw = _dragView.Yaw, Valid = true };
            pose.Position.y = _dragPlaneY;

            // The stick keeps turning the piece while it is being carried.
            float turn = StickTurn();
            if (turn != 0f) { pose.Yaw = FurniturePlacement.Normalize360(pose.Yaw + turn); PulseOnDetent(pose.Yaw); }
            if (input.SnapHeld())
                pose.Yaw = FurniturePlacement.QuantizeYaw(pose.Yaw, PlacementOptions.DefaultYawStep);
            if (_snapToWall) TrySnapToNearbyWall(ref pose, _dragView.Size);

            _dragView.ApplyPose(pose);
            ShowGhost(pose, _dragView.Size, UiTokens.Selected);
        }

        /// <summary>
        /// Settle the drag: one MoveCommand for the travel and one yaw command when the
        /// piece turned. Nothing may stay applied but unrecorded (rules 12 §3.3).
        /// </summary>
        private void EndDrag(bool record)
        {
            if (_dragView == null) return;
            var view = _dragView;
            var selectable = _dragSelectable;
            var start = _dragStart;
            float startYaw = _dragStartYaw;
            _dragView = null;
            _dragSelectable = null;

            if (view == null || !view) return;
            var delta = view.transform.position - start;
            float yawDelta = Mathf.DeltaAngle(startYaw, view.Yaw);

            if (!record)
            {
                view.transform.position = start;
                view.SetYaw(startYaw);
                return;
            }

            if (delta.sqrMagnitude > 1e-8f && selectable != null && selectable.IsAlive)
                sceneModel.History.Record(new MoveCommand(selectable, delta));
            if (Mathf.Abs(yawDelta) > 1e-3f)
                sceneModel.History.Record(new FurnitureYawCommand(selectable, view, startYaw, view.Yaw));
            if (delta.sqrMagnitude > 1e-8f || Mathf.Abs(yawDelta) > 1e-3f) input.Pulse(0.5f, 0.025f);
        }

        // ---- aiming and ghost -------------------------------------------------------

        private bool TryAim(out Vector3 point, out Vector3 normal)
        {
            point = default; normal = Vector3.up;
            if (raycaster == null) return false;
            return raycaster.TryRaycastSurface(pointer.GetRay(), out point, out normal, out _);
        }

        private void ShowGhost(in FurniturePose pose, Vector3 size, Color color)
        {
            if (ghost == null) return;
            if (!pose.Valid) { ghost.enabled = false; return; }

            FurniturePlacement.Footprint(pose, size, _corners);
            ghost.enabled = true;
            ghost.positionCount = 5;
            for (int i = 0; i < 4; i++) ghost.SetPosition(i, _corners[i] + Vector3.up * 0.005f);
            ghost.SetPosition(4, _corners[0] + Vector3.up * 0.005f);
            ghost.startColor = color;
            ghost.endColor = color;
        }
    }
}
