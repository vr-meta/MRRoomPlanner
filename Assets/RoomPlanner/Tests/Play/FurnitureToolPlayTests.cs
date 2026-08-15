using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Core.Furniture;
using RoomPlanner.Editing;
using RoomPlanner.Furniture;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Furniture tool v1 (design/27, issues #70 #71 #72) on real components: the bundled
    /// library really loads out of StreamingAssets, glTFast really parses a shipped model,
    /// and a placed piece really measures its curated real-world size. Aiming and dragging
    /// need a headset — that check stays on the checklist.
    /// </summary>
    public class FurnitureToolPlayTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private static void SetField(object target, string field, object value) =>
            target.GetType()
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);

        private T Track<T>(T go) where T : Object
        {
            if (go is GameObject g) _spawned.Add(g);
            return go;
        }

        private IEnumerator MakeLibrary(System.Action<FurnitureLibrary> onReady)
        {
            var go = Track(new GameObject("FurnitureLibrary"));
            var library = go.AddComponent<FurnitureLibrary>();
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!library.Ready && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(library.Ready, "library never finished loading the bundled packs");
            onReady(library);
        }

        [UnityTest]
        public IEnumerator Library_LoadsBundledPack()
        {
            FurnitureLibrary library = null;
            yield return MakeLibrary(l => library = l);

            Assert.GreaterOrEqual(library.Catalog.Count, 1, library.Status);
            CollectionAssert.IsEmpty(library.Problems, string.Join("; ", library.Problems));

            // The generated partitions are always available — with no packs installed at
            // all, the tool still has something to place (#86).
            var partitions = library.Catalog.Find(FurnitureLibrary.PartitionsId);
            Assert.NotNull(partitions, "generated partitions must always be in the catalog");
            Assert.Greater(partitions.Items.Count, 0);
            Assert.IsTrue(partitions.Items[0].IsProcedural);

            // Any pack that does ship must resolve its items to readable model URLs.
            foreach (var c in library.Catalog.Collections)
            {
                if (c.Id == FurnitureLibrary.PartitionsId) continue;
                foreach (var item in c.Items)
                {
                    StringAssert.Contains(item.File, library.UrlOf(item));
                    break;
                }
            }
        }

        /// <summary>Any placeable item — a generated partition, so the test does not depend
        /// on which packs are installed today.</summary>
        private static FurnitureItem AnyItem(FurnitureLibrary library)
        {
            var partitions = library.Catalog.Find(FurnitureLibrary.PartitionsId);
            Assert.NotNull(partitions, "generated partitions are the fallback content");
            return partitions.Items[0];
        }

        /// <summary>First item backed by a real model file, or null when no pack ships.</summary>
        private static FurnitureItem AnyModelItem(FurnitureLibrary library)
        {
            foreach (var c in library.Catalog.Collections)
                foreach (var item in c.Items)
                    if (!item.IsProcedural) return item;
            return null;
        }

        [UnityTest]
        public IEnumerator Loader_ParsesAModel_AndTheItemMeasuresItsRealSize()
        {
            FurnitureLibrary library = null;
            yield return MakeLibrary(l => library = l);

            var loaderGo = Track(new GameObject("Loader"));
            var loader = loaderGo.AddComponent<FurnitureLoader>();
            loader.Bind(library);

            // Whatever pack ships, a placed model must end up measuring its declared
            // real-world size — that is the entire promise of the catalog.
            var item = AnyModelItem(library);
            if (item == null) Assert.Ignore("no model-backed pack installed");

            var host = Track(new GameObject("Piece"));
            var view = host.AddComponent<FurnitureItemView>();

            var task = loader.InstantiateAsync(item, host.transform);
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(task.IsCompleted, "glTFast never finished");
            Assert.IsNull(loader.LastError, loader.LastError);
            var model = task.Result;
            Assert.NotNull(model, "the bundled GLB must parse");

            view.Bind(item, model, library.Catalog.Find(item.CollectionId));
            yield return null;

            var bounds = WorldBounds(host);
            Assert.AreEqual(item.Size.x, bounds.size.x, 0.02f, "width");
            Assert.AreEqual(item.Size.y, bounds.size.y, 0.02f, "height");
            Assert.AreEqual(item.Size.z, bounds.size.z, 0.02f, "depth");
            Assert.AreEqual(host.transform.position.y, bounds.min.y, 0.02f,
                "the piece stands ON its origin, not half-sunk through it");
        }

        [UnityTest]
        public IEnumerator Place_RegistersAnUndoableSelectableFurniture()
        {
            FurnitureLibrary library = null;
            yield return MakeLibrary(l => library = l);

            var rig = Track(new GameObject("Rig"));
            var model = rig.AddComponent<SceneModel>();
            var tool = rig.AddComponent<FurnitureController>();
            SetField(tool, "library", library);
            SetField(tool, "sceneModel", model);

            var item = AnyItem(library);
            var pose = new FurniturePose { Position = new Vector3(1f, 0f, 2f), Yaw = 90f, Valid = true };
            var view = tool.Spawn(item, pose);
            _spawned.Add(view.gameObject);

            Assert.AreEqual(pose.Position, view.transform.position);
            Assert.AreEqual(90f, view.Yaw, 1e-3f);
            Assert.AreEqual(item.Key, view.CatalogKey);
            Assert.IsFalse(string.IsNullOrEmpty(view.Credit), "a placed piece states where it came from");

            var selectable = view.GetComponent<Selectable>();
            Assert.NotNull(selectable);
            Assert.AreEqual(SelectableKind.Furniture, selectable.Kind);
            StringAssert.Contains("cm", selectable.Describe());

            var box = view.GetComponent<BoxCollider>();
            Assert.NotNull(box, "the piece must be pickable right away, before the model streams in");
            Assert.AreEqual(item.Size, box.size);

            // Delete/undo goes through the history like every other object.
            model.Register(selectable);
            model.History.Execute(new DeleteCommand(selectable));
            Assert.IsTrue(selectable.IsHidden);
            model.History.Undo();
            Assert.IsFalse(selectable.IsHidden);
        }

        [UnityTest]
        public IEnumerator Move_RecordsOneMoveAndOneRotate_BothUndoable()
        {
            FurnitureLibrary library = null;
            yield return MakeLibrary(l => library = l);

            var rig = Track(new GameObject("Rig"));
            var model = rig.AddComponent<SceneModel>();
            var tool = rig.AddComponent<FurnitureController>();
            SetField(tool, "library", library);
            SetField(tool, "sceneModel", model);

            var item = AnyItem(library);
            var view = tool.Spawn(item, new FurniturePose { Position = Vector3.zero, Yaw = 0f, Valid = true });
            _spawned.Add(view.gameObject);
            var selectable = view.GetComponent<Selectable>();
            model.Register(selectable);

            // What a drag leaves behind: a moved, turned piece plus its two commands.
            view.MoveBy(new Vector3(0.5f, 0f, 0.25f));
            view.SetYaw(45f);
            model.History.Record(new MoveCommand(selectable, new Vector3(0.5f, 0f, 0.25f)));
            model.History.Record(new FurnitureYawCommand(selectable, view, 0f, 45f));

            model.History.Undo();
            Assert.AreEqual(0f, view.Yaw, 1e-3f, "the rotation undoes on its own");
            Assert.AreEqual(new Vector3(0.5f, 0f, 0.25f), view.transform.position);

            model.History.Undo();
            Assert.AreEqual(Vector3.zero, view.transform.position, "and then the travel");

            model.History.Redo();
            Assert.AreEqual(new Vector3(0.5f, 0f, 0.25f), view.transform.position);
        }

        [UnityTest]
        public IEnumerator Schema_HasPlaceAndMoveTabs_WithoutForbiddenWidgets()
        {
            FurnitureLibrary library = null;
            yield return MakeLibrary(l => library = l);

            var rig = Track(new GameObject("Rig"));
            var tool = rig.AddComponent<FurnitureController>();
            SetField(tool, "library", library);

            var schema = tool.GetSettings();
            Assert.AreSame(schema, tool.GetSettings(), "one tabbed root instance");
            Assert.IsTrue(schema.HasTabs);
            CollectionAssert.AreEqual(new[] { "Place", "Move" }, schema.Tabs);

            var place = schema.TabPages[0];
            var kinds = new Dictionary<string, SettingKind>();
            foreach (var f in place.Fields) kinds[f.Id] = f.Kind;

            Assert.AreEqual(SettingKind.Select, kinds["collection"]);
            Assert.AreEqual(SettingKind.Select, kinds["category"]);
            Assert.AreEqual(SettingKind.Select, kinds["sub"], "second level: Sofa vs Dining table");
            // Pictures, not names (#83): the item row is a preview grid.
            Assert.AreEqual(SettingKind.Swatch, kinds["item"]);
            Assert.AreEqual(SettingKind.Stepper, kinds["yaw"]);
            Assert.AreEqual(SettingKind.Toggle, kinds["snap"]);
            Assert.AreEqual(SettingKind.Readout, kinds["size"]);

            foreach (var page in schema.TabPages)
            {
                Assert.LessOrEqual(page.Fields.Count, 8, "design/20 §3: eight rows is the ceiling");
                foreach (var f in page.Fields)
                    Assert.AreNotEqual(SettingKind.Cycle, f.Kind, "design/20 §2: Cycle is banned");
            }

            // The collection list is the catalog's — packs are re-curated, so assert on the
            // one collection that is always there rather than on today's pack.
            CollectionAssert.Contains(Row(place, "collection").ResolveOptions(), "Partitions");
            StringAssert.Contains("cm", Row(place, "size").Value());
        }

        /// <summary>Right stick pushed to the side, nothing else pressed.</summary>
        private class StickInput : RoomPlanner.Measure.MeasureInput
        {
            public Vector2 Stick;
            public override Vector2 Thumbstick() => Stick;
            public override bool ConfirmPressed() => false;
            public override bool ConfirmHeld() => false;
            public override bool ClearPressed() => false;
            public override void Pulse(float amplitude = 0.5f, float duration = 0.06f) { }
        }

        private class RayPointer : RoomPlanner.Measure.PointerProvider
        {
            public Ray Value = new(new Vector3(0f, 2f, 0f), Vector3.down);
            public override Ray GetRay() => Value;
        }

        [UnityTest]
        public IEnumerator Rotate_StickTurnsAPlacedPiece_AsOneUndoableGesture()
        {
            // Headset feedback 2026-08-12: "еще бы крутить мебель". The stick turns the
            // piece under the ray; the whole turn is ONE history entry, recorded when the
            // stick returns to centre (rules 12 §3.3 — no unrecorded drift).
            FurnitureLibrary library = null;
            yield return MakeLibrary(l => library = l);

            var rig = Track(new GameObject("Rig"));
            var model = rig.AddComponent<SceneModel>();
            var input = rig.AddComponent<StickInput>();
            var pointer = rig.AddComponent<RayPointer>();
            var tool = rig.AddComponent<FurnitureController>();
            SetField(tool, "library", library);
            SetField(tool, "sceneModel", model);
            SetField(tool, "input", input);
            SetField(tool, "pointer", pointer);

            var item = AnyItem(library);
            var view = tool.Spawn(item, new FurniturePose { Position = Vector3.zero, Yaw = 0f, Valid = true });
            _spawned.Add(view.gameObject);
            model.Register(view.GetComponent<Selectable>());
            yield return null;
            Physics.SyncTransforms();   // the pick ray needs the collider where it was placed

            // Aim from ABOVE the piece: a room divider is 2.2 m tall, so a ray starting at
            // 2 m begins inside its own collider and hits nothing.
            pointer.Value = new Ray(view.transform.position + Vector3.up * 5f, Vector3.down);
            Assert.IsTrue(model.TryPick(pointer.Value, out var picked, out _)
                          && ReferenceEquals(picked, view.GetComponent<Selectable>()),
                "the ray must actually hit the piece — otherwise the rest tests nothing");

            // Move tab, ray straight down onto the piece.
            tool.GetSettings().SelectTab(1);
            input.Stick = new Vector2(1f, 0f);
            for (int i = 0; i < 10; i++) { tool.Tick(false); yield return null; }

            Assert.Greater(view.Yaw, 0f, "the stick must turn the piece");
            Assert.AreEqual(0, model.History.UndoCount, "nothing is recorded while the stick is held");

            float turned = view.Yaw;
            input.Stick = Vector2.zero;
            tool.Tick(false);

            Assert.AreEqual(1, model.History.UndoCount, "the finished turn is one entry");
            model.History.Undo();
            Assert.AreEqual(0f, view.Yaw, 0.01f, "undo returns the original angle");
            model.History.Redo();
            Assert.AreEqual(turned, view.Yaw, 0.01f);
        }

        [UnityTest]
        public IEnumerator Loader_UsesTheProjectShader_NotGltFastsOwn()
        {
            // Headset feedback 2026-08-12: every placed piece was magenta. glTFast's
            // shaders are stripped from the build, so materials must come from OUR
            // template — then there is nothing to strip and furniture is lit like the room.
            FurnitureLibrary library = null;
            yield return MakeLibrary(l => library = l);

            var template = new UnityEngine.Material(Shader.Find("Universal Render Pipeline/Lit"));
            template.name = "TestFurnitureTemplate";

            var loaderGo = Track(new GameObject("Loader"));
            var loader = loaderGo.AddComponent<FurnitureLoader>();
            loader.Bind(library);
            loader.BindMaterial(template);

            var item = AnyModelItem(library);
            if (item == null) Assert.Ignore("no model-backed pack installed");
            var host = Track(new GameObject("Piece"));
            var task = loader.InstantiateAsync(item, host.transform);
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(task.IsCompleted && task.Result != null, loader.LastError);

            var renderers = host.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, 0);
            foreach (var r in renderers)
                foreach (var m in r.sharedMaterials)
                {
                    Assert.NotNull(m, "a null material renders magenta too");
                    Assert.AreEqual(template.shader, m.shader,
                        $"{r.name} still uses {m.shader.name} — that shader is stripped from the APK");
                }

            Object.DestroyImmediate(template);
        }

        [UnityTest]
        public IEnumerator Teleport_CarriesFurnitureWithTheModel()
        {
            // Headset feedback 2026-08-12: placed pieces "flew with the gaze". Teleport
            // moves the MODEL, so anything left out of the shift appears to follow the
            // user — the exact bug outlets and tapes had before.
            FurnitureLibrary library = null;
            yield return MakeLibrary(l => library = l);

            var rig = Track(new GameObject("Rig"));
            var model = rig.AddComponent<SceneModel>();
            var tool = rig.AddComponent<FurnitureController>();
            SetField(tool, "library", library);
            SetField(tool, "sceneModel", model);

            var item = AnyItem(library);
            var view = tool.Spawn(item, new FurniturePose
            {
                Position = new Vector3(2f, 0f, 1f), Yaw = 0f, Valid = true,
            });
            _spawned.Add(view.gameObject);

            var delta = new Vector3(-3f, 0.5f, 4f);
            var cmd = new RoomPlanner.Tools.TeleportCommand(
                null, null, delta, null, null, null, null, null,
                RoomPlanner.Tools.TeleportCommand.CollectFurniture());

            cmd.Do();
            Assert.AreEqual(new Vector3(-1f, 0.5f, 5f), view.transform.position,
                "furniture travels with the model");
            cmd.Undo();
            Assert.AreEqual(new Vector3(2f, 0f, 1f), view.transform.position, "and comes back on undo");
        }

        [UnityTest]
        public IEnumerator Partition_IsGeneratedOnPlacement_AndResizesFromItsInspector()
        {
            // A slat screen is parameters, not a file (#86): it must be complete the moment
            // it is placed — no loader, no waiting — and re-generate when resized.
            FurnitureLibrary library = null;
            yield return MakeLibrary(l => library = l);

            var rig = Track(new GameObject("Rig"));
            var model = rig.AddComponent<SceneModel>();
            var tool = rig.AddComponent<FurnitureController>();
            SetField(tool, "library", library);
            SetField(tool, "sceneModel", model);

            var item = library.Catalog.FindItem($"{FurnitureLibrary.PartitionsId}/slat-room");
            Assert.NotNull(item);
            Assert.IsTrue(item.IsProcedural);

            var view = tool.Spawn(item, new FurniturePose { Position = Vector3.zero, Yaw = 0f, Valid = true });
            _spawned.Add(view.gameObject);
            yield return null;

            var filter = view.GetComponent<MeshFilter>();
            Assert.NotNull(filter, "the screen exists immediately, without a loader");
            Assert.Greater(filter.sharedMesh.vertexCount, 0);
            Assert.AreEqual(item.Size.x, filter.sharedMesh.bounds.size.x, 0.01f, "it measures its width");

            // Widening it through the inspector rebuilds the mesh and the pick box.
            var rows = view.GetSettings();
            SettingField width = null;
            foreach (var f in rows.Fields) if (f.Id == "w") width = f;
            Assert.NotNull(width, "a generated piece exposes its size");
            width.CommitNumber?.Invoke(view.Size.x, 2.4f);
            yield return null;

            Assert.AreEqual(2.4f, filter.sharedMesh.bounds.size.x, 0.01f, "the mesh followed");
            Assert.AreEqual(2.4f, view.GetComponent<BoxCollider>().size.x, 0.01f, "so did the collider");
        }

        [UnityTest]
        public IEnumerator Project_RoundTripsAPlacedPiece()
        {
            FurnitureLibrary library = null;
            yield return MakeLibrary(l => library = l);

            var rig = Track(new GameObject("Rig"));
            var model = rig.AddComponent<SceneModel>();
            var tool = rig.AddComponent<FurnitureController>();
            SetField(tool, "library", library);
            SetField(tool, "sceneModel", model);

            var item = AnyItem(library);
            var view = tool.Spawn(item, new FurniturePose
            {
                Position = new Vector3(1.25f, 0f, -0.5f), Yaw = 135f, Valid = true,
            });
            _spawned.Add(view.gameObject);
            var selectable = view.GetComponent<Selectable>();
            selectable.Id = "furn-1";
            model.Register(selectable);

            var data = RoomPlanner.Import.ProjectStore.Capture(null, null);
            Assert.AreEqual(1, data.Furniture.Count, "the piece is captured");
            Assert.AreEqual(item.Key, data.Furniture[0].Key);
            Assert.AreEqual(item.Size, data.Furniture[0].Size);

            // Through JSON and back into an empty scene.
            var reloaded = RoomPlanner.Core.Project.ProjectData.FromJson(data.ToJson());
            Assert.NotNull(reloaded, "a v4 file must load");
            Object.DestroyImmediate(view.gameObject);

            tool.RestoreItem(reloaded.Furniture[0]);
            float deadline = Time.realtimeSinceStartup + 15f;
            FurnitureItemView restored = null;
            while (restored == null && Time.realtimeSinceStartup < deadline)
            {
                restored = Object.FindFirstObjectByType<FurnitureItemView>();
                yield return null;
            }
            Assert.NotNull(restored, "the piece must come back");
            _spawned.Add(restored.gameObject);

            Assert.AreEqual(new Vector3(1.25f, 0f, -0.5f), restored.transform.position);
            Assert.AreEqual(135f, restored.Yaw, 0.1f);
            Assert.AreEqual(item.Key, restored.CatalogKey);
            Assert.AreEqual("furn-1", restored.GetComponent<Selectable>().Id);
        }

        [UnityTest]
        public IEnumerator Project_MissingPack_RestoresAPlaceholderInsteadOfNothing()
        {
            FurnitureLibrary library = null;
            yield return MakeLibrary(l => library = l);

            var rig = Track(new GameObject("Rig"));
            var model = rig.AddComponent<SceneModel>();
            var tool = rig.AddComponent<FurnitureController>();
            SetField(tool, "library", library);
            SetField(tool, "sceneModel", model);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not in any installed collection"));
            tool.RestoreItem(new RoomPlanner.Core.Project.ProjectFurniture
            {
                Key = "gone-pack/mystery-chair", Name = "Mystery chair",
                Position = new Vector3(2f, 0f, 3f), Yaw = 0f,
                Size = new Vector3(0.5f, 0.9f, 0.5f),
            });

            float deadline = Time.realtimeSinceStartup + 15f;
            FurnitureItemView restored = null;
            while (restored == null && Time.realtimeSinceStartup < deadline)
            {
                restored = Object.FindFirstObjectByType<FurnitureItemView>();
                yield return null;
            }
            Assert.NotNull(restored, "a missing pack must not swallow the object");
            _spawned.Add(restored.gameObject);

            Assert.AreEqual(new Vector3(2f, 0f, 3f), restored.transform.position);
            Assert.AreEqual(new Vector3(0.5f, 0.9f, 0.5f), restored.Size, "the placeholder keeps its size");
            Assert.AreEqual(new Vector3(0.5f, 0.9f, 0.5f), restored.GetComponent<BoxCollider>().size);
            StringAssert.Contains("Mystery chair", restored.Describe());
        }

        private static SettingField Row(SettingsSchema page, string id)
        {
            foreach (var f in page.Fields) if (f.Id == id) return f;
            Assert.Fail($"row {id} missing");
            return null;
        }

        private static Bounds WorldBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, 0, "the loaded model has no renderers");
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }
    }
}
