using Unity.Collections;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public unsafe class PopulateInstanceIndexPass : IndirectPass
    {
        IndirectRenderSetting _setting;

        ComputeShader _populateInstanceIndexCS;
        int _populateInstanceIndexKernel;

        GraphicsBuffer _indexSegmentBuffer;
        GraphicsBuffer _instanceIndicesBuffer;
        GraphicsBuffer _instanceIndexOffsetBuffer;

        int[] _indexSegmentCount = new int[4] { 0, 0, 0, 0 };
        int[] _instanceIndexOffset = new int[4] { 0, 0, 0, 0 };

        static readonly int s_indexSegmentCountID = Shader.PropertyToID("_IndexSegmentCount");
        static readonly int s_indexSegmentBufferID = Shader.PropertyToID("IndexSegmentBuffer");
        static readonly int s_instanceIndicesBufferID = Shader.PropertyToID("InstanceIndicesBuffer");
        static readonly int s_instanceIndexOffsetBufferID = Shader.PropertyToID("InstanceIndexOffsetBuffer");

        public void Init(IndirectRenderSetting setting, ComputeShader populateInstanceIndexCS)
        {
            _setting = setting;

            _populateInstanceIndexCS = populateInstanceIndexCS;
            _populateInstanceIndexKernel = _populateInstanceIndexCS.FindKernel("PopulateInstanceIndex");

            _indexSegmentBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, setting.IndexSegmentCapacity, Utility.c_SizeOfInt4);
            _instanceIndicesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, setting.InstanceCapacity, Utility.c_SizeOfInt4);
            _instanceIndexOffsetBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, Utility.c_SizeOfInt4);

            _populateInstanceIndexCS.SetBuffer(_populateInstanceIndexKernel, s_indexSegmentBufferID, _indexSegmentBuffer);
            _populateInstanceIndexCS.SetBuffer(_populateInstanceIndexKernel, s_instanceIndicesBufferID, _instanceIndicesBuffer);
            _populateInstanceIndexCS.SetBuffer(_populateInstanceIndexKernel, s_instanceIndexOffsetBufferID, _instanceIndexOffsetBuffer);
        }

        public void Dispose()
        {
            _indexSegmentBuffer.Dispose();
            _instanceIndicesBuffer.Dispose();
            _instanceIndexOffsetBuffer.Dispose();
        }

        public GraphicsBuffer GetInstanceIndicesBuffer()
        {
            return _instanceIndicesBuffer;
        }

        public void Prepare(IndirectRenderUnmanaged* _unmanaged)
        {
            _indexSegmentCount[0] = _unmanaged->IndexSegmentCount;

            _indexSegmentBuffer.SetData(_unmanaged->IndexSegmentArray, 0, 0, _unmanaged->IndexSegmentCount);

            _instanceIndexOffset[0] = 0;
            _instanceIndexOffsetBuffer.SetData(_instanceIndexOffset);
        }

        static readonly ProfilerMarker s_populateInstanceIndexMarker = new ProfilerMarker("PopulateInstanceIndex");
        public void BuildCommandBuffer(CommandBuffer cmd, CullingHelper cullingHelper)
        {
            cmd.BeginSample(s_populateInstanceIndexMarker);

            cmd.SetComputeIntParams(_populateInstanceIndexCS, s_indexSegmentCountID, _indexSegmentCount);

            int threadGroupsX = (_indexSegmentCount[0] + 63) / 64;
            cmd.DispatchCompute(_populateInstanceIndexCS, _populateInstanceIndexKernel, threadGroupsX, 1, 1);

            cmd.EndSample(s_populateInstanceIndexMarker);
        }
    }
}