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
        int _instanceCullKernel;
        int _meshletPopulateKernel;
        int _meshletCullKernel;

        BufferManager _bufferManager;
        DispatchHelper _dispatchHelper;

        public static readonly int s_EnableLodID = Shader.PropertyToID("_EnableLod");
        public static readonly int s_CameraPositionID = Shader.PropertyToID("_CameraPosition");
        public static readonly int s_CameraMatrixID = Shader.PropertyToID("_CameraMatrix");

        public void Init(IndirectRenderSetting setting, ComputeShader indirectPipelineCS, BufferManager bufferManager, DispatchHelper dispatchHelper)
        {
            _setting = setting;

            _indirectPipelineCS = indirectPipelineCS;
            _instanceCullKernel = _indirectPipelineCS.FindKernel("InstanceCull");
            _meshletPopulateKernel = _indirectPipelineCS.FindKernel("MeshletPopulate");
            _meshletCullKernel = _indirectPipelineCS.FindKernel("MeshletCull");

            _bufferManager = bufferManager;
            _dispatchHelper = dispatchHelper;
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

        public void SerLodParam(CommandBuffer cmd, bool enable)
        {
            cmd.SetComputeIntParam(_indirectPipelineCS, s_EnableLodID, enable ? 1 : 0);

            if (enable)
            {
                cmd.SetComputeVectorParam(_indirectPipelineCS, s_CameraPositionID, Camera.main.transform.position);
                cmd.SetComputeMatrixParam(_indirectPipelineCS, s_CameraMatrixID, Camera.main.projectionMatrix);
            }
        }

        public void BuildCommandBuffer(CommandBuffer cmd, 
            GraphicsBuffer visibilityBuffer, GraphicsBuffer indirectArgsBuffer,
            GraphicsBuffer inputIndexBuffer, GraphicsBuffer outputIndexBuffer,
            UnsafeList<int4> visibleIndices, UnsafeList<int4> partialIndices)
        {
            NativeArray<int4> visibleIndexArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int4>(visibleIndices.Ptr, visibleIndices.Length, Allocator.Invalid);
            NativeArray<int4> partialIndexArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int4>(partialIndices.Ptr, partialIndices.Length, Allocator.Invalid);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref visibleIndexArray, AtomicSafetyHandle.Create());
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref partialIndexArray, AtomicSafetyHandle.Create());
#endif

            inputIndexBuffer.SetCounterValue((uint)partialIndexArray.Length);
            inputIndexBuffer.SetData(partialIndexArray, 0, 0, partialIndexArray.Length);
            outputIndexBuffer.SetCounterValue((uint)visibleIndexArray.Length);
            outputIndexBuffer.SetData(visibleIndexArray, 0, 0, visibleIndexArray.Length);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(visibleIndexArray));
            AtomicSafetyHandle.Release(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(partialIndexArray));
#endif

            // InstanceCull
            {
                cmd.SetComputeBufferParam(_indirectPipelineCS, _instanceCullKernel, BufferManager.s_InputIndexBufferID, inputIndexBuffer);
                cmd.SetComputeBufferParam(_indirectPipelineCS, _instanceCullKernel, BufferManager.s_OutputIndexBufferID, outputIndexBuffer);
                cmd.SetComputeBufferParam(_indirectPipelineCS, _instanceCullKernel, BufferManager.s_InstanceDescriptorBufferID, _bufferManager.InstanceDescriptorBuffer);

                int threadGroupsX = (partialIndexArray.Length + 63) / 64;
                threadGroupsX = math.max(threadGroupsX, 1);
                cmd.DispatchCompute(_indirectPipelineCS, _instanceCullKernel, threadGroupsX, 1, 1);
            }

            // MeshletPopulate
            {
                var tmpBuffer = inputIndexBuffer;
                inputIndexBuffer = outputIndexBuffer;
                outputIndexBuffer = tmpBuffer;

                cmd.SetBufferCounterValue(outputIndexBuffer, 0);

                cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletPopulateKernel, BufferManager.s_InputIndexBufferID, inputIndexBuffer);
                cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletPopulateKernel, BufferManager.s_OutputIndexBufferID, outputIndexBuffer);
                cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletPopulateKernel, BufferManager.s_InstanceDescriptorBufferID, _bufferManager.InstanceDescriptorBuffer);
                cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletPopulateKernel, BufferManager.s_CmdDescriptorBufferID, _bufferManager.CmdDescriptorBuffer);

                _dispatchHelper.AdjustThreadGroupX(cmd, inputIndexBuffer);
                _dispatchHelper.Dispatch(cmd, _indirectPipelineCS, _meshletPopulateKernel);
            }

            // MeshletCull
            {
                var tmpBuffer = inputIndexBuffer;
                inputIndexBuffer = outputIndexBuffer;
                outputIndexBuffer = tmpBuffer;

                cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletCullKernel, BufferManager.s_InputIndexBufferID, inputIndexBuffer);
                cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletCullKernel, BufferManager.s_MeshletDescriptorBufferID, _bufferManager.MeshletDescriptorBuffer);
                cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletCullKernel, BufferManager.s_BatchDescriptorBufferID, _bufferManager.BatchDescriptorBuffer);
                cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletCullKernel, BufferManager.s_VisibilityBufferID, visibilityBuffer);
                cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletCullKernel, BufferManager.s_IndirectArgsBufferID, indirectArgsBuffer);

                _dispatchHelper.AdjustThreadGroupX(cmd, inputIndexBuffer);
                _dispatchHelper.Dispatch(cmd, _indirectPipelineCS, _meshletCullKernel);
            }
        }
    }
}