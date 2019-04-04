using UnityEngine;

namespace UnityEditor.Rendering.LookDev
{
    public enum ViewIndex
    {
        First,
        Second
    };
    public enum ViewCompositionIndex
    {
        First = ViewIndex.First,
        Second = ViewIndex.Second,
        Composite
    };

    // /!\ WARNING: these value name are used as uss file too.
    // if your rename here, rename in the uss too.
    public enum Layout
    {
        FullFirstView,
        FullSecondView,
        HorizontalSplit,
        VerticalSplit,
        CustomSplit,
        CustomCircular
    }

    [System.Serializable]
    public class Context : ScriptableObject
    {
        [field: SerializeField]
        public LayoutContext layout { get; } = new LayoutContext();

        [SerializeField]
        ViewContext[] m_Views = new ViewContext[2]
        {
            new ViewContext(),
            new ViewContext()
        };

        [SerializeField]
        CameraState[] m_Cameras = new CameraState[2]
        {
            new CameraState(),
            new CameraState()
        };

        public ViewContext GetViewContent(ViewIndex index)
            => m_Views[(int)index];

        public CameraState GetCameraState(ViewIndex index)
            => m_Cameras[(int)index];

        internal void Validate()
        {
            if (m_Views == null || m_Views.Length != 2)
            {
                m_Views = new ViewContext[2]
                {
                    new ViewContext(),
                    new ViewContext()
                };
            }
            if (m_Cameras == null || m_Cameras.Length != 2)
            {
                m_Cameras = new CameraState[2]
                {
                    new CameraState(),
                    new CameraState()
                };
            }
        }
    }
    
    [System.Serializable]
    public class LayoutContext
    {

        public Layout viewLayout;
        public bool showEnvironmentPanel;

        [SerializeField]
        internal GizmoState gizmoState = new GizmoState();

        public bool isSimpleView => viewLayout == Layout.FullFirstView || viewLayout == Layout.FullSecondView;
        public bool isMultiView => viewLayout == Layout.HorizontalSplit || viewLayout == Layout.VerticalSplit;
        public bool isCombinedView => viewLayout == Layout.CustomSplit || viewLayout == Layout.CustomCircular;
    }

    [System.Serializable]
    public class ViewContext
    {
        //TODO: list?
        public GameObject contentPrefab { get; set; }

        public GameObject prefabInstanceInPreview { get; internal set; }
        
        //[TODO: add object position]
        //[TODO: add camera frustum]
        //[TODO: add HDRI]
        //[TODO: manage shadow and lights]
    }

}
