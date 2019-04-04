#define TEMPORARY_RENDERDOC_INTEGRATION //require specific c++

using System;
using System.Linq.Expressions;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.LookDev;
using UnityEngine.SceneManagement;
using IDataProvider = UnityEngine.Rendering.LookDev.IDataProvider;

namespace UnityEditor.Rendering.LookDev
{


    class RenderTextureCache
    {
        RenderTexture[] m_RTs = new RenderTexture[3];

        public RenderTexture this[ViewCompositionIndex index]
            => m_RTs[(int)index];

        public void UpdateSize(Rect rect, ViewCompositionIndex index, bool pixelPerfect, Camera renderingCamera)
        {
            float scaleFactor = GetScaleFactor(rect.width, rect.height, pixelPerfect);
            int width = (int)(rect.width * scaleFactor);
            int height = (int)(rect.height * scaleFactor);
            if (m_RTs[(int)index] == null
                || width != m_RTs[(int)index].width
                || height != m_RTs[(int)index].height)
            {
                if (m_RTs[(int)index] != null)
                    UnityEngine.Object.DestroyImmediate(m_RTs[(int)index]);
                
                // Do not use GetTemporary to manage render textures. Temporary RTs are only
                // garbage collected each N frames, and in the editor we might be wildly resizing
                // the inspector, thus using up tons of memory.
                //GraphicsFormat format = camera.allowHDR ? GraphicsFormat.R16G16B16A16_SFloat : GraphicsFormat.R8G8B8A8_UNorm;
                //m_RenderTexture = new RenderTexture(rtWidth, rtHeight, 16, format);
                //m_RenderTexture.hideFlags = HideFlags.HideAndDontSave;
                //TODO: check format
                m_RTs[(int)index] = new RenderTexture(
                    width, height, 0,
                    RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
                m_RTs[(int)index].hideFlags = HideFlags.HideAndDontSave;
                m_RTs[(int)index].name = "LookDevTexture";
                m_RTs[(int)index].Create();

                renderingCamera.targetTexture = m_RTs[(int)index];
            }
        }
        
        float GetScaleFactor(float width, float height, bool pixelPerfect)
        {
            float scaleFacX = Mathf.Max(Mathf.Min(width * 2, 1024), width) / width;
            float scaleFacY = Mathf.Max(Mathf.Min(height * 2, 1024), height) / height;
            float result = Mathf.Min(scaleFacX, scaleFacY) * EditorGUIUtility.pixelsPerPoint;
            if (pixelPerfect)
                result = Mathf.Max(Mathf.Round(result), 1f);
            return result;
        }
    }


    class StageCache
    {
        const string firstStageName = "LookDevFirstView";
        const string secondStageName = "LookDevSecondView";

        Stage[] m_Stages;
        Context m_Contexts;

        public Stage this[ViewIndex index]
            => m_Stages[(int)index];

        public bool initialized { get; private set; }

        public StageCache(IDataProvider dataProvider, Context contexts)
        {
            m_Contexts = contexts;
            m_Stages = new Stage[2]
            {
                InitStage(ViewIndex.First, dataProvider),
                InitStage(ViewIndex.Second, dataProvider)
            };
            initialized = true;
        }


        Stage InitStage(ViewIndex index, IDataProvider dataProvider)
        {
            Stage stage;
            switch (index)
            {
                case ViewIndex.First:
                    stage = new Stage(firstStageName);
                    stage.camera.backgroundColor = new Color32(0, 154, 154, 255);
                    break;
                case ViewIndex.Second:
                    stage = new Stage(secondStageName);
                    stage.camera.backgroundColor = new Color32(255, 37, 4, 255);
                    break;
                default:
                    throw new ArgumentException("Unknown ViewIndex: " + index);
            }

            CustomRenderSettings renderSettings = dataProvider.GetEnvironmentSetup();
            if (Unsupported.SetOverrideRenderSettings(stage.scene))
            {
                RenderSettings.defaultReflectionMode = renderSettings.defaultReflectionMode;
                RenderSettings.customReflection = renderSettings.customReflection;
                RenderSettings.skybox = renderSettings.skybox;
                RenderSettings.ambientMode = renderSettings.ambientMode;
                Unsupported.useScriptableRenderPipeline = true;
                Unsupported.RestoreOverrideRenderSettings();
            }
            else
                throw new System.Exception("Stage's scene was not created correctly");

            dataProvider.SetupCamera(stage.camera);

            return stage;
        }

        public void UpdateScene(ViewIndex index)
        {
            Stage stage = this[index];
            stage.Clear();
            var viewContent = m_Contexts.GetViewContent(index);
            if (viewContent == null)
            {
                viewContent.prefabInstanceInPreview = null;
                return;
            }

            if (viewContent.contentPrefab != null && !viewContent.contentPrefab.Equals(null))
                viewContent.prefabInstanceInPreview = stage.InstantiateIntoStage(viewContent.contentPrefab);
        }
    }


    /// <summary>
    /// Rendering logic
    /// TODO: extract SceneLogic elsewhere
    /// </summary>
    public class Compositer : IDisposable
    {
        IDisplayer m_Displayer;
        Context m_Contexts;
        RenderTextureCache m_RenderTextures = new RenderTextureCache();

        StageCache m_Stages;

        Color m_AmbientColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
        bool m_RenderDocAcquisitionRequested;

        Renderer m_Renderer = new Renderer();
        RenderingData[] m_RenderDataCache;

        public Compositer(
            IDisplayer displayer,
            Context contexts,
            IDataProvider dataProvider)
        {
            m_Displayer = displayer;
            m_Contexts = contexts;
            
            m_Stages = new StageCache(dataProvider, m_Contexts);
            m_RenderDataCache = new RenderingData[2]
            {
                new RenderingData() { stage = m_Stages[ViewIndex.First] },
                new RenderingData() { stage = m_Stages[ViewIndex.Second] }
            };

            m_Displayer.OnRenderDocAcquisitionTriggered += RenderDocAcquisitionRequested;
            EditorApplication.update += Render;
        }

        void RenderDocAcquisitionRequested()
            => m_RenderDocAcquisitionRequested = true;

        void CleanUp()
        {
            m_Displayer.OnRenderDocAcquisitionTriggered -= RenderDocAcquisitionRequested;
            EditorApplication.update -= Render;
        }
        public void Dispose()
        {
            CleanUp();
            GC.SuppressFinalize(this);
        }
        ~Compositer() => CleanUp();

        public void Render()
        {
#if TEMPORARY_RENDERDOC_INTEGRATION
            //TODO: make integration EditorWindow agnostic!
            if (RenderDoc.IsLoaded() && RenderDoc.IsSupported() && m_RenderDocAcquisitionRequested)
                RenderDoc.BeginCaptureRenderDoc(m_Displayer as EditorWindow);
#endif

            switch (m_Contexts.layout.viewLayout)
            {
                case Layout.FullFirstView:
                    RenderSingleAndOutput(ViewIndex.First);
                    break;
                case Layout.FullSecondView:
                    RenderSingleAndOutput(ViewIndex.Second);
                    break;
                case Layout.HorizontalSplit:
                case Layout.VerticalSplit:
                    RenderSingleAndOutput(ViewIndex.First);
                    RenderSingleAndOutput(ViewIndex.Second);
                    break;
                case Layout.CustomSplit:
                case Layout.CustomCircular:
                    RenderCompositeAndOutput();
                    break;
            }

#if TEMPORARY_RENDERDOC_INTEGRATION
            //TODO: make integration EditorWindow agnostic!
            if (RenderDoc.IsLoaded() && RenderDoc.IsSupported() && m_RenderDocAcquisitionRequested)
                RenderDoc.EndCaptureRenderDoc(m_Displayer as EditorWindow);
#endif
            //stating that RenderDoc do not need to acquire anymore should
            //allows to gather both view and composition in render doc at once
            //TODO: check this
            m_RenderDocAcquisitionRequested = false;
        }
        
        void RenderSingleAndOutput(ViewIndex index)
        {
            var renderingData = m_RenderDataCache[(int)index];
            renderingData.viewPort = m_Displayer.GetRect((ViewCompositionIndex)index);
            m_Renderer.Acquire(renderingData);

            //add compositing here if needed

            m_Displayer.SetTexture((ViewCompositionIndex)index, renderingData.output);
        }

        void RenderCompositeAndOutput()
        {
            Rect rect = m_Displayer.GetRect(ViewCompositionIndex.Composite);

            var renderingData = m_RenderDataCache[0];
            renderingData.viewPort = rect;
            m_Renderer.Acquire(renderingData);
            var textureA = renderingData.output;
            renderingData = m_RenderDataCache[1];
            renderingData.viewPort = rect;
            m_Renderer.Acquire(renderingData);
            var textureB = renderingData.output;

            //do composition here
            var compound = (RenderTexture)null;

            m_Displayer.SetTexture(ViewCompositionIndex.Composite, compound);
        }

        RenderTexture Compositing(Rect rect)
        {

            if (m_FinalCompositionTexture.width < 1 || m_FinalCompositionTexture.height < 1)
                return;

            Vector4 gizmoPosition = new Vector4(m_LookDevConfig.gizmo.center.x, m_LookDevConfig.gizmo.center.y, 0.0f, 0.0f);
            Vector4 gizmoZoneCenter = new Vector4(m_LookDevConfig.gizmo.point2.x, m_LookDevConfig.gizmo.point2.y, 0.0f, 0.0f);
            Vector4 gizmoThickness = new Vector4(m_GizmoThickness, m_GizmoThicknessSelected, 0.0f, 0.0f);
            Vector4 gizmoCircleRadius = new Vector4(m_GizmoCircleRadius, m_GizmoCircleRadiusSelected, 0.0f, 0.0f);

            // When we render in single view, map the parameters on same context.
            int index0 = (m_LookDevConfig.lookDevMode == LookDevMode.Single2) ? 1 : 0;
            int index1 = (m_LookDevConfig.lookDevMode == LookDevMode.Single1) ? 0 : 1;

            float exposureValue0 = (DrawCameraMode)m_LookDevConfig.lookDevContexts[index0].shadingMode == DrawCameraMode.Normal || (DrawCameraMode)m_LookDevConfig.lookDevContexts[index0].shadingMode == DrawCameraMode.TexturedWire ? m_LookDevConfig.lookDevContexts[index0].exposureValue : 0.0f;
            float exposureValue1 = (DrawCameraMode)m_LookDevConfig.lookDevContexts[index1].shadingMode == DrawCameraMode.Normal || (DrawCameraMode)m_LookDevConfig.lookDevContexts[index1].shadingMode == DrawCameraMode.TexturedWire ? m_LookDevConfig.lookDevContexts[index1].exposureValue : 0.0f;

            float dragAndDropContext = m_CurrentDragContext == LookDevEditionContext.Left ? 1.0f : (m_CurrentDragContext == LookDevEditionContext.Right ? -1.0f : 0.0f);

            CubemapInfo envInfo0 = m_LookDevEnvLibrary.hdriList[m_LookDevConfig.lookDevContexts[index0].currentHDRIIndex];
            CubemapInfo envInfo1 = m_LookDevEnvLibrary.hdriList[m_LookDevConfig.lookDevContexts[index1].currentHDRIIndex];

            // Prepare shadow information
            float shadowMultiplier0 = envInfo0.shadowInfo.shadowIntensity;
            float shadowMultiplier1 = envInfo1.shadowInfo.shadowIntensity;
            Color shadowColor0 = envInfo0.shadowInfo.shadowColor;
            Color shadowColor1 = envInfo1.shadowInfo.shadowColor;

            Texture texNormal0 = previewContext0.m_PreviewResult[(int)PreviewContext.PreviewContextPass.kView];
            Texture texWithoutSun0 = previewContext0.m_PreviewResult[(int)PreviewContext.PreviewContextPass.kViewWithShadow];
            Texture texShadows0 = previewContext0.m_PreviewResult[(int)PreviewContext.PreviewContextPass.kShadow];

            Texture texNormal1 = previewContext1.m_PreviewResult[(int)PreviewContext.PreviewContextPass.kView];
            Texture texWithoutSun1 = previewContext1.m_PreviewResult[(int)PreviewContext.PreviewContextPass.kViewWithShadow];
            Texture texShadows1 = previewContext1.m_PreviewResult[(int)PreviewContext.PreviewContextPass.kShadow];

            Vector4 compositingParams = new Vector4(m_LookDevConfig.dualViewBlendFactor, exposureValue0, exposureValue1, m_LookDevConfig.currentEditionContext == LookDevEditionContext.Left ? 1.0f : -1.0f);
            Vector4 compositingParams2 = new Vector4(dragAndDropContext, m_LookDevConfig.enableToneMap ? 1.0f : -1.0f, shadowMultiplier0, shadowMultiplier1);

            // Those could be tweakable for the neutral tonemapper, but in the case of the LookDev we don't need that
            const float BlackIn = 0.02f;
            const float WhiteIn = 10.0f;
            const float BlackOut = 0.0f;
            const float WhiteOut = 10.0f;
            const float WhiteLevel = 5.3f;
            const float WhiteClip = 10.0f;
            const float DialUnits = 20.0f;
            const float HalfDialUnits = DialUnits * 0.5f;

            // converting from artist dial units to easy shader-lerps (0-1)
            Vector4 tonemapCoeff1 = new Vector4((BlackIn * DialUnits) + 1.0f, (BlackOut * HalfDialUnits) + 1.0f, (WhiteIn / DialUnits), (1.0f - (WhiteOut / DialUnits)));
            Vector4 tonemapCoeff2 = new Vector4(0.0f, 0.0f, WhiteLevel, WhiteClip / HalfDialUnits);

            RenderTexture oldActive = RenderTexture.active;
            RenderTexture.active = m_FinalCompositionTexture;
            LookDevResources.m_LookDevCompositing.SetTexture("_Tex0Normal", texNormal0);
            LookDevResources.m_LookDevCompositing.SetTexture("_Tex0WithoutSun", texWithoutSun0);
            LookDevResources.m_LookDevCompositing.SetTexture("_Tex0Shadows", texShadows0);
            LookDevResources.m_LookDevCompositing.SetColor("_ShadowColor0", shadowColor0);
            LookDevResources.m_LookDevCompositing.SetTexture("_Tex1Normal", texNormal1);
            LookDevResources.m_LookDevCompositing.SetTexture("_Tex1WithoutSun", texWithoutSun1);
            LookDevResources.m_LookDevCompositing.SetTexture("_Tex1Shadows", texShadows1);
            LookDevResources.m_LookDevCompositing.SetColor("_ShadowColor1", shadowColor1);
            LookDevResources.m_LookDevCompositing.SetVector("_CompositingParams", compositingParams);
            LookDevResources.m_LookDevCompositing.SetVector("_CompositingParams2", compositingParams2);
            LookDevResources.m_LookDevCompositing.SetColor("_FirstViewColor", m_FirstViewGizmoColor);
            LookDevResources.m_LookDevCompositing.SetColor("_SecondViewColor", m_SecondViewGizmoColor);
            LookDevResources.m_LookDevCompositing.SetVector("_GizmoPosition", gizmoPosition);
            LookDevResources.m_LookDevCompositing.SetVector("_GizmoZoneCenter", gizmoZoneCenter);
            LookDevResources.m_LookDevCompositing.SetVector("_GizmoSplitPlane", m_LookDevConfig.gizmo.plane);
            LookDevResources.m_LookDevCompositing.SetVector("_GizmoSplitPlaneOrtho", m_LookDevConfig.gizmo.planeOrtho);
            LookDevResources.m_LookDevCompositing.SetFloat("_GizmoLength", m_LookDevConfig.gizmo.length);
            LookDevResources.m_LookDevCompositing.SetVector("_GizmoThickness", gizmoThickness);
            LookDevResources.m_LookDevCompositing.SetVector("_GizmoCircleRadius", gizmoCircleRadius);
            LookDevResources.m_LookDevCompositing.SetFloat("_BlendFactorCircleRadius", m_BlendFactorCircleRadius);
            LookDevResources.m_LookDevCompositing.SetFloat("_GetBlendFactorMaxGizmoDistance", GetBlendFactorMaxGizmoDistance());
            LookDevResources.m_LookDevCompositing.SetFloat("_GizmoRenderMode", m_ForceGizmoRenderSelector ? (float)LookDevOperationType.GizmoAll : (float)m_GizmoRenderMode);
            LookDevResources.m_LookDevCompositing.SetVector("_ScreenRatio", m_ScreenRatio);
            LookDevResources.m_LookDevCompositing.SetVector("_ToneMapCoeffs1", tonemapCoeff1);
            LookDevResources.m_LookDevCompositing.SetVector("_ToneMapCoeffs2", tonemapCoeff2);
            LookDevResources.m_LookDevCompositing.SetPass((int)m_LookDevConfig.lookDevMode);

            DrawFullScreenQuad(new Rect(0, 0, previewRect.width, previewRect.height));

            RenderTexture.active = oldActive;

            GUI.DrawTexture(previewRect, m_FinalCompositionTexture, ScaleMode.StretchToFill, false);
        }
    }




























































    public class RenderingData
    {
        public Stage stage;
        public Rect viewPort;
        public RenderTexture output;
    }


    /// <summary>
    /// Rendering logic
    /// TODO: extract SceneLogic elsewhere
    /// </summary>
    public class Renderer
    {
        public bool pixelPerfect { get; set; }

        bool IsNullArea(Rect r)
            => r.width < 1f || r.height < 1f
            || float.IsNaN(r.width) || float.IsNaN(r.height);

        public Renderer(bool pixelPerfect = false)
            => this.pixelPerfect = pixelPerfect;

        public void Acquire(RenderingData data)
        {
            if (IsNullArea(data.viewPort))
            {
                data.output = null;
                return;
            }
            
            BeginRendering(data);
            data.stage.camera.Render();
            EndRendering(data);
        }

        void BeginRendering(RenderingData data)
        {
            data.stage.SetGameObjectVisible(true);
            UpdateSizeAndLinkToCamera(data.viewPort, ref data.output, data.stage.camera);
            data.stage.camera.enabled = true;
        }

        void EndRendering(RenderingData data)
        {
            data.stage.camera.enabled = false;
            data.stage.SetGameObjectVisible(false);
        }
        
        public void UpdateSizeAndLinkToCamera(Rect rect, ref RenderTexture renderTexture, Camera renderingCamera)
        {
            float scaleFactor = GetScaleFactor(rect.width, rect.height);
            int width = (int)(rect.width * scaleFactor);
            int height = (int)(rect.height * scaleFactor);
            if (renderTexture == null
                || width != renderTexture.width
                || height != renderTexture.height)
            {
                if (renderTexture != null)
                    UnityEngine.Object.DestroyImmediate(renderTexture);

                // Do not use GetTemporary to manage render textures. Temporary RTs are only
                // garbage collected each N frames, and in the editor we might be wildly resizing
                // the inspector, thus using up tons of memory.
                //GraphicsFormat format = camera.allowHDR ? GraphicsFormat.R16G16B16A16_SFloat : GraphicsFormat.R8G8B8A8_UNorm;
                //m_RenderTexture = new RenderTexture(rtWidth, rtHeight, 16, format);
                //m_RenderTexture.hideFlags = HideFlags.HideAndDontSave;
                //TODO: check format
                renderTexture = new RenderTexture(
                    width, height, 0,
                    RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
                renderTexture.hideFlags = HideFlags.HideAndDontSave;
                renderTexture.name = "LookDevTexture";
                renderTexture.Create();
            }
            if (renderingCamera.targetTexture != renderTexture)
                renderingCamera.targetTexture = renderTexture;
        }

        float GetScaleFactor(float width, float height)
        {
            float scaleFacX = Mathf.Max(Mathf.Min(width * 2, 1024), width) / width;
            float scaleFacY = Mathf.Max(Mathf.Min(height * 2, 1024), height) / height;
            float result = Mathf.Min(scaleFacX, scaleFacY) * EditorGUIUtility.pixelsPerPoint;
            if (pixelPerfect)
                result = Mathf.Max(Mathf.Round(result), 1f);
            return result;
        }
    }
}
