using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.LookDev;

namespace UnityEditor.Rendering.LookDev
{
    //TODO: add undo support
    public class Stage : IDisposable
    {
        const int k_PreviewCullingLayerIndex = 31; //Camera.PreviewCullingLayer; //TODO: expose or reflection

        private readonly Scene m_PreviewScene;

        // Everything except camera
        private readonly List<GameObject> m_GameObjects = new List<GameObject>();
        private readonly Camera m_Camera;

        /// <summary>Get access to the stage's camera</summary>
        public Camera camera => m_Camera;

        /// <summary>Get access to the stage's scene</summary>
        public Scene scene => m_PreviewScene;

        /// <summary>
        /// Construct a new stage to let your object live.
        /// A stage is a scene with visibility isolation.
        /// </summary>
        /// <param name="sceneName">Name of the scene used.</param>
        public Stage(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
                throw new System.ArgumentNullException("sceneName");

            m_PreviewScene = EditorSceneManager.NewPreviewScene();
            m_PreviewScene.name = sceneName;
            
            var camGO = EditorUtility.CreateGameObjectWithHideFlags("Look Dev Camera", HideFlags.HideAndDontSave, typeof(Camera));
            
            SceneManager.MoveGameObjectToScene(camGO, m_PreviewScene);
            camGO.transform.position = new Vector3(0, 0, -6);
            camGO.transform.rotation = Quaternion.identity;
            camGO.hideFlags = HideFlags.HideAndDontSave;
            camGO.layer = k_PreviewCullingLayerIndex;

            m_Camera = camGO.GetComponent<Camera>();
            m_Camera.cameraType = CameraType.Preview;
            m_Camera.enabled = false;
            m_Camera.clearFlags = CameraClearFlags.Depth;
            m_Camera.fieldOfView = 90;
            m_Camera.farClipPlane = 10.0f;
            m_Camera.nearClipPlane = 2.0f;
            m_Camera.cullingMask = 1 << k_PreviewCullingLayerIndex;
            m_Camera.renderingPath = RenderingPath.DeferredShading;
            m_Camera.useOcclusionCulling = false;
            m_Camera.scene = m_PreviewScene;
        }

        /// <summary>
        /// Change the renderSettings to use in this scene.
        /// </summary>
        /// <param name="renderSettings">The parameters to use</param>
        public void ChangeRenderSettings(CustomRenderSettings renderSettings)
        {
            // Setup default render settings for this preview scene
            //false if
            //  - scene is not loaded
            //  - application cannot update
            //  - scene do not have a LevelGameManager for RenderSettings
            if (Unsupported.SetOverrideRenderSettings(m_PreviewScene))
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
        }


        /// <summary>
        /// Move a GameObject into the stage's scene at origin.
        /// </summary>
        /// <param name="gameObject">The gameObject to move.</param>
        /// <seealso cref="InstantiateIntoStage"/>
        public void MoveIntoStage(GameObject gameObject)
            => MoveIntoStage(gameObject, Vector3.zero, Quaternion.identity);

        /// <summary>
        /// Move a GameObject into the stage's scene at specific position and
        /// rotation.
        /// </summary>
        /// <param name="gameObject">The gameObject to move.</param>
        /// <param name="position">The new world position</param>
        /// <param name="rotation">The new world rotation</param>
        /// <seealso cref="InstantiateIntoStage"/>
        public void MoveIntoStage(GameObject gameObject, Vector3 position, Quaternion rotation)
        {
            if (m_GameObjects.Contains(gameObject))
                return;

            SceneManager.MoveGameObjectToScene(gameObject, m_PreviewScene);
            gameObject.transform.position = position;
            gameObject.transform.rotation = rotation;
            m_GameObjects.Add(gameObject);

            InitAddedObjectsRecursively(gameObject);
        }

        /// <summary>
        /// Instantiate a scene GameObject or a prefab into the stage's scene.
        /// It is instantiated at origin.
        /// </summary>
        /// <param name="prefabOrSceneObject">The element to instantiate</param>
        /// <returns>The instance</returns>
        /// <seealso cref="MoveIntoStage"/>
        public GameObject InstantiateIntoStage(GameObject prefabOrSceneObject)
            => InstantiateIntoStage(prefabOrSceneObject, Vector3.zero, Quaternion.identity);

        /// <summary>
        /// Instantiate a scene GameObject or a prefab into the stage's scene
        /// at a specific position and rotation.
        /// </summary>
        /// <param name="prefabOrSceneObject">The element to instantiate</param>
        /// <param name="position">The new world position</param>
        /// <param name="rotation">The new world rotation</param>
        /// <returns>The instance</returns>
        /// <seealso cref="MoveIntoStage"/>
        public GameObject InstantiateIntoStage(GameObject prefabOrSceneObject, Vector3 position, Quaternion rotation)
        {
            var handle = GameObject.Instantiate(prefabOrSceneObject);
            MoveIntoStage(handle, position, rotation);
            return handle;
        }

        /// <summary>Clear and close the stage's scene.</summary>
        public void Dispose()
        {
            Clear();
            UnityEngine.Object.DestroyImmediate(camera);
            EditorSceneManager.ClosePreviewScene(m_PreviewScene);
        }

        /// <summary>Clear all scene object except camera.</summary>
        public void Clear()
        {
            foreach (var go in m_GameObjects)
                UnityEngine.Object.DestroyImmediate(go);
            m_GameObjects.Clear();
        }

        static void InitAddedObjectsRecursively(GameObject go)
        {
            go.hideFlags = HideFlags.HideAndDontSave;
            go.layer = k_PreviewCullingLayerIndex;
            foreach (Transform child in go.transform)
                InitAddedObjectsRecursively(child.gameObject);
        }

        /// <summary>Changes stage scene's objects visibility.</summary>
        /// <param name="visible">
        /// True: make them visible.
        /// False: hide them.
        /// </param>
        public void SetGameObjectVisible(bool visible)
        {
            foreach (GameObject go in m_GameObjects)
            {
                if (go == null || go.Equals(null))
                    continue;
                foreach (UnityEngine.Renderer renderer in go.GetComponentsInChildren<UnityEngine.Renderer>())
                    renderer.enabled = visible;
                foreach (Light light in go.GetComponentsInChildren<Light>())
                    light.enabled = visible;
            }

            // in case we add camera frontal light and such
            foreach (UnityEngine.Renderer renderer in m_Camera.GetComponentsInChildren<UnityEngine.Renderer>())
                renderer.enabled = visible;
            foreach (Light light in m_Camera.GetComponentsInChildren<Light>())
                light.enabled = visible;
        }
    }
}
