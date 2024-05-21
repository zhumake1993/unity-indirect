#define ENABLE_BURST

using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public unsafe class IndirectPipelinePool
    {
        List<IndirectPipeline> _usedPipelines = new List<IndirectPipeline>();
        List<IndirectPipeline> _unusedPipelines = new List<IndirectPipeline>();

        public void Init()
        { 
        }

        public void Dispose()
        {
            Recycle();

            foreach (var pipeline in _unusedPipelines)
                pipeline.Dispose();
        }

        public IndirectPipeline Get()
        {
            if (_unusedPipelines.Count > 0)
            {
                var pipeline = _unusedPipelines[_unusedPipelines.Count - 1];
                _unusedPipelines.RemoveAt(_unusedPipelines.Count - 1);
                _usedPipelines.Add(pipeline);
                return pipeline;
            }
            else
            {
                return null;
            }
        }

        public void Use(IndirectPipeline pipeline)
        {
            _usedPipelines.Add(pipeline);
        }

        public void Recycle()
        {
            foreach (var buffer in _usedPipelines)
                _unusedPipelines.Add(buffer);
            _usedPipelines.Clear();
        }
    }

    public unsafe class IndirectPipeline
    {
        ComputeShader _indirectPipelineCS;
        int _instanceCullKernel;
        int _meshletPopulateKernel;
        int _meshletCullKernel;

        BufferManager _bufferManager;
        DispatchHelper _dispatchHelper;

        CommandBuffer _cmd = new CommandBuffer();

        GraphicsBuffer _inputIndexBuffer;
        GraphicsBuffer _outputIndexBuffer;
        GraphicsBuffer _visibilityBuffer;
        GraphicsBuffer _indirectArgsBuffer;

        UnsafeList<int4>* _visibleIndices;
        UnsafeList<int4>* _partialIndices;

        bool _frustumCull;

        public static readonly int s_EnableLodID = Shader.PropertyToID("_EnableLod");
        public static readonly int s_CameraPositionID = Shader.PropertyToID("_CameraPosition");
        public static readonly int s_CameraMatrixID = Shader.PropertyToID("_CameraMatrix");

        public void Init(IndirectRenderSetting setting, ComputeShader indirectPipelineCS, BufferManager bufferManager, DispatchHelper dispatchHelper)
        {
            // Instantiate a new cs, so every instance has its own data
            _indirectPipelineCS = Object.Instantiate(indirectPipelineCS);

            _instanceCullKernel = _indirectPipelineCS.FindKernel("InstanceCull");
            _meshletPopulateKernel = _indirectPipelineCS.FindKernel("MeshletPopulate");
            _meshletCullKernel = _indirectPipelineCS.FindKernel("MeshletCull");

            _bufferManager = bufferManager;
            _dispatchHelper = dispatchHelper;

            _frustumCull = true;
            _indirectPipelineCS.DisableKeyword("_DISABLE_FRUSTUM_CULL");

            _cmd.name = "IndirectPipeline";

            _inputIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Counter, setting.MeshletCapacity, Utility.c_SizeOfInt4);
            _outputIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Counter, setting.MeshletCapacity, Utility.c_SizeOfInt4);
            _visibilityBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, setting.InstanceCapacity, Utility.c_SizeOfInt4);
            _indirectArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, setting.BatchCapacity, GraphicsBuffer.IndirectDrawArgs.size);

            BuildCommandBuffer();
        }

        public void Dispose()
        {
            _cmd.Dispose();

            _inputIndexBuffer.Dispose();
            _outputIndexBuffer.Dispose();
            _visibilityBuffer.Dispose();
            _indirectArgsBuffer.Dispose();
        }

        public bool EnableFrustumCull
        {
            get { return _frustumCull; }
            set
            {
                _frustumCull = value;
                if (_frustumCull)
                    _indirectPipelineCS.DisableKeyword("_DISABLE_FRUSTUM_CULL");
                else
                    _indirectPipelineCS.EnableKeyword("_DISABLE_FRUSTUM_CULL");
            }
        }

        public GraphicsBuffer GetVisibilityBuffer()
        {
            return _visibilityBuffer;
        }

        public GraphicsBuffer GetIndirectArgsBuffer()
        {
            return _indirectArgsBuffer;
        }

        void BuildCommandBuffer()
        {
            _cmd.Clear();

            // todo: adjust index buffer size

            // InstanceCull
            {
                _cmd.SetComputeBufferParam(_indirectPipelineCS, _instanceCullKernel, BufferManager.s_InputIndexBufferID, _inputIndexBuffer);
                _cmd.SetComputeBufferParam(_indirectPipelineCS, _instanceCullKernel, BufferManager.s_OutputIndexBufferID, _outputIndexBuffer);
                _cmd.SetComputeBufferParam(_indirectPipelineCS, _instanceCullKernel, BufferManager.s_InstanceDescriptorBufferID, _bufferManager.InstanceDescriptorBuffer);

                _dispatchHelper.AdjustThreadGroupX(_cmd, _inputIndexBuffer);
                _dispatchHelper.Dispatch(_cmd, _indirectPipelineCS, _instanceCullKernel);
            }

            // MeshletPopulate
            {
                var tmpBuffer = _inputIndexBuffer;
                _inputIndexBuffer = _outputIndexBuffer;
                _outputIndexBuffer = tmpBuffer;

                _cmd.SetBufferCounterValue(_outputIndexBuffer, 0);

                _cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletPopulateKernel, BufferManager.s_InputIndexBufferID, _inputIndexBuffer);
                _cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletPopulateKernel, BufferManager.s_OutputIndexBufferID, _outputIndexBuffer);
                _cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletPopulateKernel, BufferManager.s_InstanceDescriptorBufferID, _bufferManager.InstanceDescriptorBuffer);
                _cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletPopulateKernel, BufferManager.s_CmdDescriptorBufferID, _bufferManager.CmdDescriptorBuffer);

                _dispatchHelper.AdjustThreadGroupX(_cmd, _inputIndexBuffer);
                _dispatchHelper.Dispatch(_cmd, _indirectPipelineCS, _meshletPopulateKernel);
            }

            // MeshletCull
            {
                var tmpBuffer = _inputIndexBuffer;
                _inputIndexBuffer = _outputIndexBuffer;
                _outputIndexBuffer = tmpBuffer;

                _cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletCullKernel, BufferManager.s_InputIndexBufferID, _inputIndexBuffer);
                _cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletCullKernel, BufferManager.s_MeshletDescriptorBufferID, _bufferManager.MeshletDescriptorBuffer);
                _cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletCullKernel, BufferManager.s_BatchDescriptorBufferID, _bufferManager.BatchDescriptorBuffer);
                _cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletCullKernel, BufferManager.s_VisibilityBufferID, _visibilityBuffer);
                _cmd.SetComputeBufferParam(_indirectPipelineCS, _meshletCullKernel, BufferManager.s_IndirectArgsBufferID, _indirectArgsBuffer);

                _dispatchHelper.AdjustThreadGroupX(_cmd, _inputIndexBuffer);
                _dispatchHelper.Dispatch(_cmd, _indirectPipelineCS, _meshletCullKernel);
            }
        }

        public JobHandle Dispatch(CullingHelper cullingHelper, QuadTree quadTree, int splitIndex)
        {
            cullingHelper.SetPlaneParam(_indirectPipelineCS, splitIndex);

            // todo
            SerLodParam(true);

            _visibleIndices = MemoryUtility.Malloc<UnsafeList<int4>>(Allocator.TempJob);
            _partialIndices = MemoryUtility.Malloc<UnsafeList<int4>>(Allocator.TempJob);
            JobHandle jobHandle = quadTree.Cull(cullingHelper.GetCullinglanes(splitIndex), _visibleIndices, _partialIndices);

            return jobHandle;
        }

        public void Finish()
        {
            //int indexCount = 0;

            //if (indexCount > _currIndexCount)
            //{
            //    throw new System.Exception("indexCount > _currIndexCount");
            //    _currIndexCount = indexCount;
            //    BuildCommandBuffer();
            //}

            SetIndexBuffer();

            Graphics.ExecuteCommandBuffer(_cmd);
        }

        void SerLodParam(bool enable)
        {
            _indirectPipelineCS.SetInt(s_EnableLodID, enable ? 1 : 0);

            if (enable)
            {
                Camera camera = Camera.main;
                _indirectPipelineCS.SetVector(s_CameraPositionID, camera.transform.position);
                _indirectPipelineCS.SetMatrix(s_CameraMatrixID, camera.projectionMatrix);
            }
        }

        void SetIndexBuffer()
        {
            NativeArray<int4> visibleIndexArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int4>(_visibleIndices->Ptr, _visibleIndices->Length, Allocator.Invalid);
            NativeArray<int4> partialIndexArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int4>(_partialIndices->Ptr, _partialIndices->Length, Allocator.Invalid);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref visibleIndexArray, AtomicSafetyHandle.Create());
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref partialIndexArray, AtomicSafetyHandle.Create());
#endif

            _inputIndexBuffer.SetCounterValue((uint)partialIndexArray.Length);
            _inputIndexBuffer.SetData(partialIndexArray, 0, 0, partialIndexArray.Length);
            _outputIndexBuffer.SetCounterValue((uint)visibleIndexArray.Length);
            _outputIndexBuffer.SetData(visibleIndexArray, 0, 0, visibleIndexArray.Length);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(visibleIndexArray));
            AtomicSafetyHandle.Release(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(partialIndexArray));
#endif

            _visibleIndices->Dispose();
            _partialIndices->Dispose();
            MemoryUtility.Free(_visibleIndices, Allocator.TempJob);
            MemoryUtility.Free(_partialIndices, Allocator.TempJob);
        }

        //void SetCmdName(int cameraInstanceID, bool shadow, int splitIndex)
        //{
        //    if (!shadow)
        //    {

        //    }
        //    else
        //    {

        //    }
        //    if (_cachedCameraInstanceID != cameraInstanceID)
        //    {
        //        _cmd.name = $"IndirectPipeline_{cameraInstanceID}";
        //        _cachedCameraInstanceID = cameraInstanceID;
        //    }
        //}
    }

#if ENABLE_BURST
    [BurstCompile(CompileSynchronously = true)]
#endif
    public unsafe struct CreateDrawCmdJob : IJob
    {
        public NativeArray<BatchCullingOutputDrawCommands> OutputDrawCommands;
        [NativeDisableUnsafePtrRestriction]
        public IndirectRenderUnmanaged* Unmanaged;
        public NativeParallelHashMap<int, BatchMaterialID> IdToBatchMaterialID;
        public UnsafeList<GraphicsBufferHandle> VisibilityBufferHandles;
        public UnsafeList<GraphicsBufferHandle> IndirectArgsBufferHandles;
        public int SplitCount;

        public void Execute()
        {
            BatchCullingOutputDrawCommands outputDrawCommand = new BatchCullingOutputDrawCommands();
            outputDrawCommand.indirectDrawCommands = MemoryUtility.MallocNoTrack<BatchDrawCommandIndirect>(Unmanaged->IndirectMap.Count * SplitCount, Allocator.TempJob);
            int indirectIndex = 0;

            for (int iSplit = 0; iSplit < SplitCount; ++iSplit)
            {
                foreach (var pair in Unmanaged->IndirectMap)
                {
                    IndirectKey indirectKey = pair.Key;
                    IndirectBatch indirectBatch = pair.Value;

                    if (!IdToBatchMaterialID.TryGetValue(indirectKey.MaterialID, out BatchMaterialID batchMaterialID))
                    {
                        Utility.LogError($"invalid MaterialID {indirectKey.MaterialID}");
                        continue;
                    }

                    BatchDrawCommandIndirect indirectCmd = new BatchDrawCommandIndirect()
                    {
                        topology = MeshTopology.Triangles,
                        materialID = batchMaterialID,
                        visibilityBufferHandle = VisibilityBufferHandles[iSplit],
                        indirectArgsBufferHandle = IndirectArgsBufferHandles[iSplit],
                        indirectArgsOffset = (uint)indirectBatch.IndirectID,
                        renderingLayerMask = 0xffffffff,
                        layer = indirectKey.Layer,
                        shadowCastingMode = indirectKey.ShadowCastingMode,
                        receiveShadows = indirectKey.ReceiveShadows,
                        splitVisibilityMask = (ushort)(1u << iSplit),
                    };

                    outputDrawCommand.indirectDrawCommands[indirectIndex++] = indirectCmd;
                }
            }

            outputDrawCommand.indirectDrawCommandCount = indirectIndex;
            OutputDrawCommands[0] = outputDrawCommand;

            VisibilityBufferHandles.Dispose();
            IndirectArgsBufferHandles.Dispose();
        }
    }
}