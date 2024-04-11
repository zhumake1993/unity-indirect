using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public unsafe class QuadTreeCullingPass : IndirectPass
    {
        IndirectRenderSetting _setting;

        ComputeShader _quadTreeCullingCS;
        int _quadTreeCullingKernel;

        GraphicsBuffer _instanceIndexTodoBuffer;
        GraphicsBuffer _instanceIndexFinalBuffer;

        int[] _totalInstanceCount = new int[4] { 0, 0, 0, 0 };
        int[] _quadTreeLodParam = new int[4] { 0, 0, 0, 0 };

        static readonly int s_totalInstanceCountID = Shader.PropertyToID("_TotalInstanceIndexCount");
        static readonly int s_quadTreeLodParamID = Shader.PropertyToID("_QuadTreeLodParam");
        static readonly int s_quadTreeLodOffsetID = Shader.PropertyToID("_QuadTreeLodOffset");
        static readonly int s_quadTreeNodeVisibilityBufferID = Shader.PropertyToID("QuadTreeNodeVisibilityBuffer");
        static readonly int s_instanceIndexInputBufferID = Shader.PropertyToID("InstanceIndexInputBuffer");
        static readonly int s_instanceDescriptorBufferID = Shader.PropertyToID("InstanceDescriptorBuffer");
        static readonly int s_instanceIndexTodoBufferID = Shader.PropertyToID("InstanceIndexTodoBuffer");
        static readonly int s_instanceIndexFinalBufferID = Shader.PropertyToID("InstanceIndexFinalBuffer");

        public void Init(IndirectRenderSetting setting, ComputeShader quadTreeCullingCS, QuadTreeBuildPass quadTreeBuildPass)
        {
            _setting = setting;

            _quadTreeCullingCS = quadTreeCullingCS;
            _quadTreeCullingKernel = _quadTreeCullingCS.FindKernel("QuadTreeCulling");

            _instanceIndexTodoBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Counter,
                _setting.InstanceCapacity, Utility.c_SizeOfInt4);
            _instanceIndexFinalBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Counter,
                _setting.InstanceCapacity * Utility.c_MaxCullingSet, Utility.c_SizeOfInt4);

            _quadTreeLodParam[0] = _setting.QuadTreeSetting.MaxLodRange.x;
            _quadTreeLodParam[1] = _setting.QuadTreeSetting.MaxLodRange.y;
            _quadTreeLodParam[2] = _setting.QuadTreeSetting.MaxLodRange.z;
            _quadTreeLodParam[3] = _setting.QuadTreeSetting.MaxLod;
            _quadTreeCullingCS.SetInts(s_quadTreeLodParamID, _quadTreeLodParam);

            int[] quadTreeLodOffset = quadTreeBuildPass.CalculateQuadTreeLodOffset(out int totalLodNodeNum);
            _quadTreeCullingCS.SetInts(s_quadTreeLodOffsetID, quadTreeLodOffset);

            _quadTreeCullingCS.SetBuffer(_quadTreeCullingKernel, s_instanceIndexTodoBufferID, _instanceIndexTodoBuffer);
            _quadTreeCullingCS.SetBuffer(_quadTreeCullingKernel, s_instanceIndexFinalBufferID, _instanceIndexFinalBuffer);
        }

        public void Dispose()
        {
            _instanceIndexTodoBuffer.Dispose();
            _instanceIndexFinalBuffer.Dispose();
        }

        public void SetEnable(bool enable)
        {
            if (enable)
                _quadTreeCullingCS.DisableKeyword("_DISABLE");
            else
                _quadTreeCullingCS.EnableKeyword("_DISABLE");
        }

        public GraphicsBuffer GetInstanceIndexTodoBuffer()
        {
            return _instanceIndexTodoBuffer;
        }

        public GraphicsBuffer GetInstanceIndexFinalBuffer()
        {
            return _instanceIndexFinalBuffer;
        }

        public void ConnectBuffer(GraphicsBuffer quadTreeNodeVisibilityBuffer, GraphicsBuffer instanceIndexInputBuffer, GraphicsBuffer instanceDescriptorBuffer)
        {
            _quadTreeCullingCS.SetBuffer(_quadTreeCullingKernel, s_quadTreeNodeVisibilityBufferID, quadTreeNodeVisibilityBuffer);
            _quadTreeCullingCS.SetBuffer(_quadTreeCullingKernel, s_instanceIndexInputBufferID, instanceIndexInputBuffer);
            _quadTreeCullingCS.SetBuffer(_quadTreeCullingKernel, s_instanceDescriptorBufferID, instanceDescriptorBuffer);
        }

        public void Prepare(IndirectRenderUnmanaged* _unmanaged)
        {
            _totalInstanceCount[0] = _unmanaged->TotalActualInstanceCount;
        }

        static readonly ProfilerMarker s_quadTreeCullingMarker = new ProfilerMarker("QuadTreeCulling");
        public void BuildCommandBuffer(CommandBuffer cmd, CullingHelper cullingHelper)
        {
            cmd.BeginSample(s_quadTreeCullingMarker);

            cmd.SetComputeIntParams(_quadTreeCullingCS, s_totalInstanceCountID, _totalInstanceCount);

            cmd.SetBufferCounterValue(_instanceIndexTodoBuffer, 0);
            cmd.SetBufferCounterValue(_instanceIndexFinalBuffer, 0);

            int threadGroupsX = (_totalInstanceCount[0] + 63) / 64;
            cmd.DispatchCompute(_quadTreeCullingCS, _quadTreeCullingKernel, threadGroupsX, 1, 1);

            cmd.EndSample(s_quadTreeCullingMarker);
        }
    }
}