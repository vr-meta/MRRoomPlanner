#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Создаёт URP-ассет (+ Universal Renderer) и назначает его как рендер-пайплайн
    /// проекта. Реализовано через рефлексию, чтобы файл компилировался даже когда пакет
    /// URP ещё не установлен. Меню: RoomPlanner → Switch To URP.
    /// Нужно, потому что шейдеры MRUK написаны под URP.
    /// </summary>
    public static class URPSetup
    {
        private const string Dir = "Assets/Settings";
        private const string RendererPath = Dir + "/URP-Renderer.asset";
        private const string AssetPath = Dir + "/URP-Asset.asset";

        [MenuItem("RoomPlanner/Switch To URP")]
        public static void SwitchToURP()
        {
            Type urpAssetType = FindType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset");
            Type rendererDataType = FindType("UnityEngine.Rendering.Universal.UniversalRendererData");

            if (urpAssetType == null || rendererDataType == null)
            {
                EditorUtility.DisplayDialog("Switch To URP",
                    "Пакет URP ещё не установлен/не импортирован.\n\n" +
                    "Переключись в Unity, дождись импорта пакета " +
                    "com.unity.render-pipelines.universal (он уже в manifest), потом запусти снова.",
                    "OK");
                return;
            }

            Directory.CreateDirectory(Dir);

            // 1. Renderer Data
            var rendererData = ScriptableObject.CreateInstance(rendererDataType);
            AssetDatabase.CreateAsset(rendererData, RendererPath);

            // 2. URP Asset через статический Create(rendererData)
            var create = urpAssetType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Create" && m.GetParameters().Length == 1);
            if (create == null)
            {
                EditorUtility.DisplayDialog("Switch To URP",
                    "Не нашёл метод UniversalRenderPipelineAsset.Create — версия URP отличается. Сообщи мне.", "OK");
                return;
            }

            var urp = (RenderPipelineAsset)create.Invoke(null, new object[] { rendererData });
            AssetDatabase.CreateAsset(urp, AssetPath);
            AssetDatabase.SaveAssets();

            // 3. Назначить как пайплайн проекта (Graphics + все уровни Quality)
            GraphicsSettings.defaultRenderPipeline = urp;
            int active = QualitySettings.GetQualityLevel();
            string[] levels = QualitySettings.names;
            for (int i = 0; i < levels.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(active, false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Switch To URP",
                "Готово! URP-ассет создан и назначен.\n\n" +
                "Теперь:\n" +
                "1) Если маркеры/линия рулетки стали розовыми — удали объект MeasureRig и префаб\n" +
                "   Assets/RoomPlanner/Prefabs/Measurement, затем снова RoomPlanner → Setup Measure Rig\n" +
                "   (материалы пересоздадутся под URP).\n" +
                "2) Собери заново: Ctrl+B.",
                "OK");
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
#endif
