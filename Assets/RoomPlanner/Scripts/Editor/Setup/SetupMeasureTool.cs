#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using RoomPlanner.Measure;
using RoomPlanner.Editing;

namespace RoomPlanner.EditorTools
{
    /// <summary>Measure tool: measurement + "+" button prefabs and controller wiring.</summary>
    internal static class SetupMeasureTool
    {
        private const string MeasurePrefabPath = SetupAssets.PrefabDir + "/Measurement.prefab";
        private const string ContPrefabPath = SetupAssets.PrefabDir + "/ContinueButton.prefab";

        public static void Build(RigContext ctx)
        {
            Measurement measurePrefab = CreateMeasurementPrefab(ctx.LineMat, ctx.MarkerMat, ctx.BadgeMat);
            MeasureContinueButton contPrefab = CreateContinueButtonPrefab(ctx.ContMat);

            ctx.Measure = ctx.Rig.AddComponent<MeasureController>();
            var so = new SerializedObject(ctx.Measure);
            so.FindProperty("pointer").objectReferenceValue = ctx.Pointer;
            so.FindProperty("input").objectReferenceValue = ctx.Input;
            so.FindProperty("raycaster").objectReferenceValue = ctx.Raycaster;
            so.FindProperty("reticle").objectReferenceValue = ctx.Reticle.transform;
            so.FindProperty("measurementPrefab").objectReferenceValue = measurePrefab;
            so.FindProperty("continueButtonPrefab").objectReferenceValue = contPrefab;
            so.FindProperty("sceneModel").objectReferenceValue = ctx.SceneModel;
            so.FindProperty("manager").objectReferenceValue = ctx.Manager;
            so.ApplyModifiedProperties();
        }

        private static Measurement CreateMeasurementPrefab(Material lineMat, Material markerMat, Material badgeMat)
        {
            var root = new GameObject("Measurement");
            var measurement = root.AddComponent<Measurement>();
            root.AddComponent<Selectable>();   // stays on the default layer; markers keep their colliders

            var lineGo = new GameObject("Line");
            lineGo.transform.SetParent(root.transform, false);
            var line = lineGo.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = 0.008f;
            line.numCapVertices = 4;
            line.positionCount = 2;
            line.sharedMaterial = lineMat;

            var markerA = MakeMarker("MarkerA", root.transform, markerMat, true);
            var markerB = MakeMarker("MarkerB", root.transform, markerMat, false);

            // Подпись-бейдж
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localScale = Vector3.one * 0.08f; // мельче шрифт (бейдж масштабируется вместе)
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text = "0 cm";
            tmp.fontSize = 4f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(4f, 1f);
            var label = labelGo.AddComponent<MeasurementLabel>();

            var badge = GameObject.CreatePrimitive(PrimitiveType.Quad);
            badge.name = "Badge";
            badge.transform.SetParent(labelGo.transform, false);
            SetupAssets.RemoveCollider(badge);
            badge.GetComponent<Renderer>().sharedMaterial = badgeMat;
            badge.transform.localScale = new Vector3(4f, 1.4f, 1f);
            badge.transform.localPosition = new Vector3(0f, 0f, 0.02f); // позади текста (к камере — текст)

            var mso = new SerializedObject(measurement);
            mso.FindProperty("line").objectReferenceValue = line;
            mso.FindProperty("label").objectReferenceValue = label;
            mso.FindProperty("markerA").objectReferenceValue = markerA.transform;
            mso.FindProperty("markerB").objectReferenceValue = markerB.transform;
            mso.ApplyModifiedProperties();

            var lso = new SerializedObject(label);
            lso.FindProperty("text").objectReferenceValue = tmp;
            lso.FindProperty("background").objectReferenceValue = badge.transform;
            lso.ApplyModifiedProperties();

            // SaveAsPrefabAsset overwrites in place, preserving the prefab's GUID.
            var asset = PrefabUtility.SaveAsPrefabAsset(root, MeasurePrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return asset.GetComponent<Measurement>();
        }

        private static MeasureContinueButton CreateContinueButtonPrefab(Material mat)
        {
            var root = new GameObject("ContinueButton");
            root.AddComponent<MeasureContinueButton>();
            var col = root.AddComponent<SphereCollider>();
            col.radius = 0.06f;

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Visual";
            sphere.transform.SetParent(root.transform, false);
            sphere.transform.localScale = Vector3.one * 0.05f;
            SetupAssets.RemoveCollider(sphere);
            sphere.GetComponent<Renderer>().sharedMaterial = mat;

            var labelGo = new GameObject("Plus");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.04f);
            labelGo.transform.localScale = Vector3.one * 0.06f;
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text = "+";
            tmp.fontSize = 6f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.black;
            tmp.rectTransform.sizeDelta = new Vector2(2f, 2f);

            var asset = PrefabUtility.SaveAsPrefabAsset(root, ContPrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return asset.GetComponent<MeasureContinueButton>();
        }

        private static GameObject MakeMarker(string name, Transform parent, Material mat, bool isEndA)
        {
            var m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m.name = name;
            m.transform.SetParent(parent, false);
            m.transform.localScale = Vector3.one * 0.04f;
            m.GetComponent<Renderer>().sharedMaterial = mat;

            // коллайдер оставляем (для выделения/таскания), делаем чуть крупнее визуала
            var col = m.GetComponent<SphereCollider>();
            if (col != null) col.radius = 1.6f;

            var handle = m.AddComponent<MeasurePointHandle>();
            var hso = new SerializedObject(handle);
            hso.FindProperty("isEndA").boolValue = isEndA;
            hso.ApplyModifiedProperties();
            return m;
        }
    }
}
#endif
