using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RoomPlanner.Core;
using RoomPlanner.Core.Ifc;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Materials of imported objects (design/29, issue #124). Revit exports no textures at
    /// all and its colours are often placeholders, so we split a product by its surface
    /// styles, read the material name where the geometry has no style of its own, and map
    /// that name onto the CC0 catalog. Fixture: two cubes in one furnishing element with a
    /// style each, plus a cube whose look lives only in its material.
    /// </summary>
    public class ObjectMaterialTests
    {
        private const string Doc = @"ISO-10303-21;
HEADER;
ENDSEC;
DATA;
#1=IFCCARTESIANPOINT((0.,0.,0.));
#2=IFCCARTESIANPOINT((1.,0.,0.));
#3=IFCCARTESIANPOINT((1.,1.,0.));
#4=IFCCARTESIANPOINT((0.,1.,0.));
#5=IFCCARTESIANPOINT((0.,0.,1.));
#6=IFCCARTESIANPOINT((1.,0.,1.));
#7=IFCCARTESIANPOINT((1.,1.,1.));
#8=IFCCARTESIANPOINT((0.,1.,1.));
#10=IFCPOLYLOOP((#1,#4,#3,#2));
#11=IFCFACEOUTERBOUND(#10,.T.);
#12=IFCFACE((#11));
#13=IFCPOLYLOOP((#5,#6,#7,#8));
#14=IFCFACEOUTERBOUND(#13,.T.);
#15=IFCFACE((#14));
#16=IFCPOLYLOOP((#1,#2,#6,#5));
#17=IFCFACEOUTERBOUND(#16,.T.);
#18=IFCFACE((#17));
#19=IFCPOLYLOOP((#3,#4,#8,#7));
#20=IFCFACEOUTERBOUND(#19,.T.);
#21=IFCFACE((#20));
#22=IFCPOLYLOOP((#2,#3,#7,#6));
#23=IFCFACEOUTERBOUND(#22,.T.);
#24=IFCFACE((#23));
#25=IFCPOLYLOOP((#4,#1,#5,#8));
#26=IFCFACEOUTERBOUND(#25,.T.);
#27=IFCFACE((#26));
#30=IFCCLOSEDSHELL((#12,#15,#18,#21,#24,#27));
#31=IFCFACETEDBREP(#30);
#101=IFCCARTESIANPOINT((2.,0.,0.));
#102=IFCCARTESIANPOINT((3.,0.,0.));
#103=IFCCARTESIANPOINT((3.,1.,0.));
#104=IFCCARTESIANPOINT((2.,1.,0.));
#105=IFCCARTESIANPOINT((2.,0.,1.));
#106=IFCCARTESIANPOINT((3.,0.,1.));
#107=IFCCARTESIANPOINT((3.,1.,1.));
#108=IFCCARTESIANPOINT((2.,1.,1.));
#110=IFCPOLYLOOP((#101,#104,#103,#102));
#111=IFCFACEOUTERBOUND(#110,.T.);
#112=IFCFACE((#111));
#113=IFCPOLYLOOP((#105,#106,#107,#108));
#114=IFCFACEOUTERBOUND(#113,.T.);
#115=IFCFACE((#114));
#130=IFCCLOSEDSHELL((#112,#115));
#131=IFCFACETEDBREP(#130);
#34=IFCCARTESIANPOINT((0.,0.,0.));
#35=IFCAXIS2PLACEMENT3D(#34,$,$);
#36=IFCLOCALPLACEMENT($,#35);
#40=IFCSHAPEREPRESENTATION($,'Body','Brep',(#31,#131));
#41=IFCPRODUCTDEFINITIONSHAPE($,$,(#40));
#42=IFCFURNISHINGELEMENT('f1',$,'Sofa',$,$,#36,#41,$);
#50=IFCCOLOURRGB($,0.1,0.1,0.1);
#51=IFCSURFACESTYLERENDERING(#50,0.,$,$,$,$,$,.FLAT.);
#52=IFCSURFACESTYLE('Textile - Leather - Black',.BOTH.,(#51));
#53=IFCPRESENTATIONSTYLEASSIGNMENT((#52));
#54=IFCSTYLEDITEM(#31,(#53),$);
#55=IFCCOLOURRGB($,0.9,0.9,0.9);
#56=IFCSURFACESTYLERENDERING(#55,0.,$,$,$,$,$,.FLAT.);
#57=IFCSURFACESTYLE('Metal - Aluminium',.BOTH.,(#56));
#58=IFCPRESENTATIONSTYLEASSIGNMENT((#57));
#59=IFCSTYLEDITEM(#131,(#58),$);
#132=IFCFACETEDBREP(#30);
#60=IFCSHAPEREPRESENTATION($,'Body','Brep',(#132));
#61=IFCPRODUCTDEFINITIONSHAPE($,$,(#60));
#62=IFCFURNISHINGELEMENT('f2',$,'Cabinet',$,$,#36,#61,$);
#70=IFCMATERIAL('Walnut wood veneer (F900)');
#71=IFCCOLOURRGB($,0.5,0.5,0.5);
#72=IFCSURFACESTYLERENDERING(#71,0.,$,$,$,$,$,.FLAT.);
#73=IFCSURFACESTYLE('Walnut wood veneer (F900)',.BOTH.,(#72));
#74=IFCPRESENTATIONSTYLEASSIGNMENT((#73));
#75=IFCSTYLEDITEM($,(#74),$);
#76=IFCSTYLEDREPRESENTATION($,'Style','Material',(#75));
#77=IFCMATERIALDEFINITIONREPRESENTATION($,$,(#76),#70);
#78=IFCFURNITURETYPE('t1',$,'CabinetType',$,$,$,$,$,$,.TABLE.);
#79=IFCRELASSOCIATESMATERIAL('r1',$,$,$,(#78),#70);
#80=IFCRELDEFINESBYTYPE('r2',$,$,$,(#62),#78);
ENDSEC;
END-ISO-10303-21;";

        private static ImportedBuilding _building;

        private static ImportedBuilding Building =>
            _building ??= IfcImporter.Import(StepFile.Parse(Doc));

        private static ImportedMep Element(string name) =>
            Building.Plumbing.Single(m => m.Name == name);

        // ------------------------------------------------------------------ parts

        [Test]
        public void ProductWithTwoStylesSplitsIntoTwoParts()
        {
            var sofa = Element("Sofa");
            Assert.AreEqual(2, sofa.Parts.Count, "one part per surface style");

            var leather = sofa.Parts.Single(p => p.Name == "Textile - Leather - Black");
            var metal = sofa.Parts.Single(p => p.Name == "Metal - Aluminium");
            Assert.AreEqual(36, leather.TriCount, "the full cube: 6 quads");
            Assert.AreEqual(12, metal.TriCount, "the second shell has two faces");
            Assert.AreEqual(0.1f, leather.Color.r, 1e-4);
            Assert.AreEqual(0.9f, metal.Color.r, 1e-4);
        }

        [Test]
        public void PartRangesAreContiguousAndCoverEveryTriangle()
        {
            foreach (var mep in Building.Plumbing)
            {
                int expected = 0;
                foreach (var part in mep.Parts)
                {
                    Assert.AreEqual(expected, part.TriStart, $"{mep.Name}: parts are ranges");
                    Assert.Greater(part.TriCount, 0);
                    expected += part.TriCount;
                }
                Assert.AreEqual(mep.Triangles.Count, expected,
                    $"{mep.Name}: every triangle belongs to exactly one part");
            }
        }

        [Test]
        public void MaterialSuppliesNameAndColourWhenGeometryHasNoStyle()
        {
            var cabinet = Element("Cabinet");
            Assert.AreEqual(1, cabinet.Parts.Count);
            Assert.AreEqual("Walnut wood veneer (F900)", cabinet.Parts[0].Name,
                "the material rides in through the TYPE (IfcRelDefinesByType)");
            Assert.IsTrue(cabinet.Parts[0].HasColor,
                "colour from IfcMaterialDefinitionRepresentation, not IfcStyledItem");
            Assert.AreEqual(0.5f, cabinet.Parts[0].Color.g, 1e-4);
            Assert.IsTrue(cabinet.HasColor, "the element itself is no longer grey");
        }

        [Test]
        public void BakedMeshesCarryMetricUvs()
        {
            foreach (var mep in Building.Plumbing)
                Assert.AreEqual(mep.Vertices.Count, mep.Uvs.Count,
                    $"{mep.Name}: one UV per vertex");
        }

        // ------------------------------------------------------------------ box UV

        [Test]
        public void BoxUvUnwrapsOneToOneWithWorldMetres()
        {
            // a 2 × 3 quad on each axis plane: the UV area must equal the world area
            foreach (var (a, b, c) in new[]
            {
                (new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(2, 0, 3)),   // XZ (floor)
                (new Vector3(0, 0, 0), new Vector3(0, 2, 0), new Vector3(0, 2, 3)),   // ZY (side)
                (new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(2, 3, 0)),   // XY (front)
            })
            {
                var verts = new List<Vector3> { a, b, c };
                var tris = new List<int> { 0, 1, 2 };
                var uvs = new List<Vector2>();
                BoxUv.Fill(verts, tris, uvs);

                float world = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                var u0 = uvs[0]; var u1 = uvs[1]; var u2 = uvs[2];
                float uv = Mathf.Abs((u1.x - u0.x) * (u2.y - u0.y) - (u1.y - u0.y) * (u2.x - u0.x)) * 0.5f;
                Assert.AreEqual(world, uv, 1e-4, "1 m of the world = 1 unit of UV");
            }
        }

        [Test]
        public void BoxUvSurvivesDegenerateInput()
        {
            var verts = new List<Vector3> { Vector3.zero, Vector3.zero, Vector3.zero };
            var uvs = new List<Vector2>();
            BoxUv.Fill(verts, new List<int> { 0, 1, 2 }, uvs);
            Assert.AreEqual(3, uvs.Count);
            foreach (var uv in uvs) Assert.IsFalse(float.IsNaN(uv.x) || float.IsNaN(uv.y));
        }

        // ------------------------------------------------------------------ name map

        [TestCase("Walnut wood veneer (F900)", "wood-walnut")]
        [TestCase("Cherry", "wood-walnut")]
        [TestCase("Wood - Birch", "wood-birch")]
        [TestCase("Wood - Stained", "wood-dark")]
        [TestCase("Black Brown", "wood-dark")]
        [TestCase("IKEA natural wood", "wood-oak")]
        [TestCase("Textile - Leather - Black", "leather-black")]
        [TestCase("Textile - Slate Blue", "fabric-blue")]
        [TestCase("Metal - Stainless Steel,Polished", "metal-steel")]
        [TestCase("Metal - Aluminium", "metal-aluminium")]
        [TestCase("Steel for furniture", "metal-brushed")]
        [TestCase("Appliance - Steel - Black", "metal-painted-black")]
        [TestCase("Plastic, Opaque Black", "plastic-black")]
        [TestCase("Пластик жёлтый", "plastic-white")]
        [TestCase("Пластик серый глянцевый", "plastic-grey")]
        [TestCase("Керамика белая", "ceramic-white")]
        [TestCase("Корпус умывальника", "ceramic-white")]
        [TestCase("Корпус смесителя", "metal-steel")]
        [TestCase("Изделие из полимерного материала", "plastic-white")]
        [TestCase("Concrete, Cast-in-Place gray", "concrete-034")]
        // v1.4 breadth: brass/copper, natural weaves, stone counters, more species
        [TestCase("Metal - Brass, Polished", "metal-brass")]
        [TestCase("Латунь шлифованная", "metal-brass")]
        [TestCase("Copper - Pipe", "metal-copper")]
        [TestCase("Ротанг натуральный", "wicker-natural")]
        [TestCase("Rattan weave", "wicker-natural")]
        [TestCase("Пробковое покрытие", "cork-natural")]
        [TestCase("Terrazzo - Beige", "stone-terrazzo")]
        [TestCase("Столешница кварцевая", "stone-white")]
        [TestCase("Wood - Teak", "wood-teak")]
        [TestCase("Wood - Ash", "wood-ash")]
        [TestCase("Ясень светлый", "wood-ash")]
        [TestCase("Textile - Felt Grey", "fabric-felt")]
        [TestCase("Leather - White", "leather-white")]
        [TestCase("Велюр изумрудный", "fabric-grey")]
        public void MaterialNamesResolveToCatalogFinishes(string name, string expected)
        {
            Assert.AreEqual(expected, IfcMaterialMap.Resolve(name).FinishId);
        }

        /// <summary>«Washer» and «washing machine» contain «ash» — a naive substring for
        /// the wood species would have turned every appliance into ash veneer.</summary>
        [TestCase("Appliance - Washer")]
        [TestCase("Washing machine body")]
        public void AshDoesNotSwallowWashers(string name)
        {
            Assert.AreNotEqual("wood-ash", IfcMaterialMap.Resolve(name).FinishId);
        }

        [TestCase("Default Wall")]
        [TestCase("<Unnamed>")]
        [TestCase("Layer 5")]
        [TestCase("")]
        [TestCase(null)]
        public void UnknownNamesGetNoFinish(string name)
        {
            Assert.IsTrue(IfcMaterialMap.Resolve(name).IsNone, $"'{name}' must not be guessed");
        }

        // ------------------------------------------------- doors and windows (#133)

        /// <summary>Revit ships a door as a material LIST; the leaf and frame are the
        /// wood when there is wood, the metal otherwise, and never the glazing.</summary>
        [Test]
        public void FramePickPrefersWoodThenMetalNeverGlass()
        {
            Assert.AreEqual("Wood - Birch", IfcMaterialMap.PickFrame(new[]
            {
                "Aluminum", "Wood - Birch", "Metal - Paint Finish - Grey",
            }), "passage door: the leaf is birch, not its aluminium hardware");

            Assert.AreEqual("Cherry", IfcMaterialMap.PickFrame(new[]
            {
                "Cherry", "Metal - Stainless Steel", "Metal - Painted - Grey", "Glass",
            }), "exterior door: cherry leaf");

            Assert.AreEqual("Wood - Stained", IfcMaterialMap.PickFrame(new[]
            {
                "Wood - Stained", "Glass",
            }), "window: the frame, not the pane");

            Assert.AreEqual("Metal - Stainless Steel", IfcMaterialMap.PickFrame(new[]
            {
                "Glass", "Metal - Stainless Steel",
            }), "no wood in the list → the metal");

            Assert.IsNull(IfcMaterialMap.PickFrame(new[] { "Glass", "Стекло" }),
                "glass only → nothing to dress, the pane has its own material");
            Assert.IsNull(IfcMaterialMap.PickFrame(null));
        }

        [Test]
        public void FramePickFallsBackToAnUnknownName()
        {
            // unknown names are still returned: the caller decides (a finish comes out
            // None and the joinery keeps the rig material), but the name is not lost
            Assert.AreEqual("Default Wall",
                IfcMaterialMap.PickFrame(new[] { "Glass", "Default Wall" }));
        }

        [Test]
        public void GlassIsRecognisedAndCarriesNoTexture()
        {
            Assert.IsTrue(IfcMaterialMap.IsGlass("Glass"));
            Assert.IsTrue(IfcMaterialMap.IsGlass("Стекло витрины"));
            Assert.IsTrue(IfcMaterialMap.Resolve("Glass").IsNone, "glass wears the see-through material");
        }

        [Test]
        public void OnlyWhiteBasedFinishesAreTinted()
        {
            Assert.IsTrue(IfcMaterialMap.Resolve("Пластик жёлтый").Tintable,
                "yellow plastic = white plastic × yellow");
            Assert.IsTrue(IfcMaterialMap.Resolve("Керамика белая").Tintable);
            Assert.IsFalse(IfcMaterialMap.Resolve("Wood - Birch").Tintable,
                "wood carries its colour in the texture — tinting it grey would kill it");
            Assert.IsFalse(IfcMaterialMap.Resolve("Metal - Aluminium").Tintable);
        }
    }
}
