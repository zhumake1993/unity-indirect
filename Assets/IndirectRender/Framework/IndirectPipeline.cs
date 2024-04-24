using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public unsafe class IndirectPipeline
    {
        IndirectRenderSetting _setting;

        public ComputeShader IndirectPipelineCS => _indirectPipelineCS;
        ComputeShader _indirectPipelineCS;
        int _indirectPipelineNoCullKernel;
        int _indirectPipelineCullKernel;

        BufferManager _bufferManager;

        int[] _indexCount = new int[4] { 0, 0, 0, 0 };

        bool _frustumCull = true;

        public static readonly int s_IndexCountID = Shader.PropertyToID("_IndexCount");

        public void Init(IndirectRenderSetting setting, ComputeShader indirectPipelineCS, BufferManager bufferManager)
        {
            _setting = setting;

            _indirectPipelineCS = indirectPipelineCS;
            _indirectPipelineNoCullKernel = _indirectPipelineCS.FindKernel("IndirectPipelineNoCull");
            _indirectPipelineCullKernel = _indirectPipelineCS.FindKernel("IndirectPipelineCull");

            _bufferManager = bufferManager;

            _indirectPipelineCS.SetBuffer(_indirectPipelineNoCullKernel, BufferManager.s_IndexBufferID, _bufferManager.IndexBuffer);
            _indirectPipelineCS.SetBuffer(_indirectPipelineNoCullKernel, BufferManager.s_InstanceDescriptorBufferID, _bufferManager.InstanceDescriptorBuffer);
            _indirectPipelineCS.SetBuffer(_indirectPipelineNoCullKernel, BufferManager.s_BatchDescriptorBufferID, _bufferManager.BatchDescriptorBuffer);

            _indirectPipelineCS.SetBuffer(_indirectPipelineCullKernel, BufferManager.s_IndexBufferID, _bufferManager.IndexBuffer);
            _indirectPipelineCS.SetBuffer(_indirectPipelineCullKernel, BufferManager.s_InstanceDescriptorBufferID, _bufferManager.InstanceDescriptorBuffer);
            _indirectPipelineCS.SetBuffer(_indirectPipelineCullKernel, BufferManager.s_BatchDescriptorBufferID, _bufferManager.BatchDescriptorBuffer);
        }

        public void Dispose()
        {
        }

        public void SetFrustumCull(bool enable)
        {
            if (enable)
                _indirectPipelineCS.DisableKeyword("_DISABLE_FRUSTUM_CULL");
            else
                _indirectPipelineCS.EnableKeyword("_DISABLE_FRUSTUM_CULL");
        }

        public void BuildCommandBuffer(CommandBuffer cmd, GraphicsBuffer visibilityBuffer, GraphicsBuffer indirectArgsBuffer, UnsafeList<int4> visibleIndices, UnsafeList<int4> partialIndices)
        {
            NativeArray<int4> visibleIndexArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int4>(visibleIndices.Ptr, visibleIndices.Length, Allocator.Invalid);
            NativeArray<int4> partialIndexArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int4>(partialIndices.Ptr, partialIndices.Length, Allocator.Invalid);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref visibleIndexArray, AtomicSafetyHandle.Create());
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref partialIndexArray, AtomicSafetyHandle.Create());
#endif

            _indexCount[0] = visibleIndexArray.Length;
            _indexCount[1] = partialIndexArray.Length;
            _bufferManager.IndexBuffer.SetData(visibleIndexArray, 0, 0, visibleIndexArray.Length);
            _bufferManager.IndexBuffer.SetData(partialIndexArray, 0, visibleIndexArray.Length, partialIndexArray.Length);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(visibleIndexArray));
            AtomicSafetyHandle.Release(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(partialIndexArray));
#endif

            cmd.SetComputeIntParams(_indirectPipelineCS, s_IndexCountID, _indexCount);

            if (_indexCount[0] > 0)
            {
                cmd.SetComputeBufferParam(_indirectPipelineCS, _indirectPipelineNoCullKernel, BufferManager.s_VisibilityBufferID, visibilityBuffer);
                cmd.SetComputeBufferParam(_indirectPipelineCS, _indirectPipelineNoCullKernel, BufferManager.s_IndirectArgsBufferID, indirectArgsBuffer);

                int threadGroupsX = (_indexCount[0] + 63) / 64;
                cmd.DispatchCompute(_indirectPipelineCS, _indirectPipelineNoCullKernel, threadGroupsX, 1, 1);
            }

            if (_indexCount[1] > 0)
            {
                cmd.SetComputeBufferParam(_indirectPipelineCS, _indirectPipelineCullKernel, BufferManager.s_VisibilityBufferID, visibilityBuffer);
                cmd.SetComputeBufferParam(_indirectPipelineCS, _indirectPipelineCullKernel, BufferManager.s_IndirectArgsBufferID, indirectArgsBuffer);

                int threadGroupsX = (_indexCount[1] + 63) / 64;
                cmd.DispatchCompute(_indirectPipelineCS, _indirectPipelineCullKernel, threadGroupsX, 1, 1);
            }
        }
    }
}