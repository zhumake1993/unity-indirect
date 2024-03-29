using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public unsafe partial class IndirectRender
    {
        MeshMerger _meshMerger = new MeshMerger();
        AssetManager _assetManager = new AssetManager();
        IndirectRenderUnmanaged* _unmanaged;

        DispatchHelper _dispatchHelper = new DispatchHelper();
        CullingHelper _cullingHelper = new CullingHelper();

        QuadTreeBuildPass _quadTreeBuildPass = new QuadTreeBuildPass();
        PopulateInstanceIndexPass _populateInstanceIndexPass = new PopulateInstanceIndexPass();
        QuadTreeCullingPass _quadTreeCullingPass = new QuadTreeCullingPass();
        FrustumCullingPass _frustumCullingPass = new FrustumCullingPass();
        PopulateVisibilityAndIndirectArgPass _populateVisibilityAndIndirectArgPass = new PopulateVisibilityAndIndirectArgPass();

        IndirectDrawer _indirectDrawer = new IndirectDrawer();

        CommandBuffer _cmd;

        JobHandle _dispatchJobHandle;

        bool _draw;
        bool _drawQuadTree;

        public void Init(IndirectRenderSetting setting, ComputerShaderCollection computerShaderCollection)
        {
            if (!CheckSetting(setting))
                return;

            _meshMerger.Init(setting.IndexCapacity, setting.VertexCapacity, setting.UnitMeshTriangleCount);
            _assetManager.Init(_meshMerger);

            _unmanaged = MemoryUtility.Malloc<IndirectRenderUnmanaged>(Allocator.Persistent);
            _unmanaged->Init(setting);

            _dispatchHelper.Init(computerShaderCollection.AdjustDispatchArgCS);
            _cullingHelper.Init();

            _quadTreeBuildPass.Init(setting, computerShaderCollection.QuadTreeBuildCS, _dispatchHelper, _cullingHelper);
            _populateInstanceIndexPass.Init(setting, computerShaderCollection.PopulateInstanceIndexCS);
            _quadTreeCullingPass.Init(setting, computerShaderCollection.QuadTreeCullingCS, _quadTreeBuildPass);
            _frustumCullingPass.Init(setting, computerShaderCollection.FrustumCullingCS, _dispatchHelper, _cullingHelper);
            _populateVisibilityAndIndirectArgPass.Init(setting, computerShaderCollection.PopulateVisibilityAndIndirectArgCS, _dispatchHelper);

            _indirectDrawer.Init(setting, _meshMerger, _assetManager, _unmanaged);

            _quadTreeCullingPass.ConnectBuffer(_quadTreeBuildPass.GetQuadTreeNodeVisibilityBuffer(),
                _populateInstanceIndexPass.GetInstanceIndicesBuffer(), _indirectDrawer.GetInstanceDescriptorBuffer());
            _frustumCullingPass.ConnectBuffer(_quadTreeCullingPass.GetInstanceIndexTodoBuffer(),
                _quadTreeCullingPass.GetInstanceIndexFinalBuffer(), _indirectDrawer.GetInstanceDescriptorBuffer());
            _populateVisibilityAndIndirectArgPass.ConnectBuffer(_frustumCullingPass.GetInstanceIndexOuutputBuffer(),
                _indirectDrawer.GetInstanceDescriptorBuffer(), _indirectDrawer.GetBatchDescriptorBuffer(), _indirectDrawer.GetVisibilityBuffer());
            _indirectDrawer.ConnectBuffer(_populateVisibilityAndIndirectArgPass.GetIndirectArgsBuffer());

            _cmd = new CommandBuffer();
            _cmd.name = "IndirectRenderCmd";

            _dispatchJobHandle = new JobHandle();

            _draw = true;
            _drawQuadTree = true;

            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        }

        bool CheckSetting(IndirectRenderSetting setting)
        {
            if (setting.MaxInstanceCountPerCmd * setting.NumMaxInstanceCountPerCmd > setting.InstanceCapacity)
            {
                Utility.LogError($"InstanceCapacity({setting.InstanceCapacity}) must be at least " +
                    $"MaxInstanceCountPerCmd({setting.MaxInstanceCountPerCmd}) * NumMaxInstanceCountPerCmd({setting.NumMaxInstanceCountPerCmd})");

                return false;
            }

            if (setting.QuadTreeSetting.MaxLod + 1 > Utility.c_QuadTreeMaxLodNum)
            {
                Utility.LogError($"MaxLod({setting.QuadTreeSetting.MaxLod} exceeds {Utility.c_QuadTreeMaxLodNum})");

                return false;
            }

            return true;
        }

        public void Dispose()
        {
            _meshMerger.Dispose();
            _assetManager.Dispose();

            _unmanaged->Dispose();
            MemoryUtility.Free(_unmanaged, Allocator.Persistent);

            _dispatchHelper.Dispose();
            _cullingHelper.Dispose();

            _quadTreeBuildPass.Dispose();
            _populateInstanceIndexPass.Dispose();
            _quadTreeCullingPass.Dispose();
            _frustumCullingPass.Dispose();
            _populateVisibilityAndIndirectArgPass.Dispose();

            _indirectDrawer.Dispose();

            _cmd.Dispose();

            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
        }

        void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            Execute();
        }

        public int RegisterMesh(MeshKey meshKey)
        {
            return _assetManager.RegisterMesh(meshKey);
        }

        public int RegisterMaterial(Material material)
        {
            return _assetManager.RegisterMaterial(material);
        }

        public Material GetMaterial(int id)
        {
            return _assetManager.GetMaterial(id);
        }

        public void SetQuadTreeCullingEnable(bool enable)
        {
            _quadTreeCullingPass.SetEnable(enable);
        }

        public void SetFrustumCullingEnable(bool enable)
        {
            _frustumCullingPass.SetEnable(enable);
        }

        public int AddBatch(IndirectKey indirectKey, int meshID, bool needInverse, UnsafeList<float4x4> matrices, UnsafeList<UnsafeList<float4>> properties)
        {
            int result = _unmanaged->AddBatchImpl(indirectKey, meshID, needInverse, matrices, properties,
                _assetManager, _indirectDrawer.GetInstanceDataBuffer(), _indirectDrawer.GetInstanceDescriptorBuffer(), _quadTreeBuildPass);

            matrices.Dispose();
            for (int i = 0; i < properties.Length; ++i)
                properties[i].Dispose();
            properties.Dispose();

            return result;
        }

        public void RemoveBatch(int id)
        {
            _unmanaged->RemoveBatch(id);
        }

        static readonly ProfilerMarker s_updateCameraFrustumPlanesMarker = new ProfilerMarker("IndirectRender.UpdateCameraFrustumPlanes");
        public void UpdateCameraFrustumPlanes(Camera camera)
        {
            using (s_updateCameraFrustumPlanesMarker.Auto())
            {
                _cullingHelper.UpdateCameraFrustumPlanes(camera);
            }
        }

        bool ShouldDraw()
        {
            return _draw && _unmanaged->CmdMap.Count > 0;
        }

        static readonly ProfilerMarker s_dispatchMarker = new ProfilerMarker("IndirectRender.Dispatch");
        public void Dispatch()
        {
            if (!ShouldDraw())
                return;

            using (s_dispatchMarker.Auto())
            {
                DispatchJob dispatchJob = new DispatchJob
                {
                    Unmanaged = _unmanaged,
                    UnitMeshIndexCount = _meshMerger.GetUnitMeshIndexCount()
                };
                _dispatchJobHandle = dispatchJob.Schedule();
            }
        }

        static readonly ProfilerMarker s_executeMarker = new ProfilerMarker("IndirectRender.Execute");
        void Execute()
        {
            if (!ShouldDraw())
                return;

            using (s_executeMarker.Auto())
            {
                _dispatchJobHandle.Complete();

                Prepare();

                BuildCommandBuffer();
                Graphics.ExecuteCommandBuffer(_cmd);

                DrawIndirect();
            }
        }

        static readonly ProfilerMarker s_prepareMarker = new ProfilerMarker("IndirectRender.Prepare");
        void Prepare()
        {
            using (s_prepareMarker.Auto())
            {
                _quadTreeBuildPass.Prepare(_unmanaged);
                _populateInstanceIndexPass.Prepare(_unmanaged);
                _quadTreeCullingPass.Prepare(_unmanaged);
                _frustumCullingPass.Prepare(_unmanaged);
                _populateVisibilityAndIndirectArgPass.Prepare(_unmanaged);
            }
        }

        static readonly ProfilerMarker s_buildCommandBufferMarker = new ProfilerMarker("IndirectRender.BuildCommandBuffer");
        void BuildCommandBuffer()
        {
            using (s_buildCommandBufferMarker.Auto())
            {
                _cmd.Clear();

                _quadTreeBuildPass.BuildCommandBuffer(_cmd);
                _populateInstanceIndexPass.BuildCommandBuffer(_cmd);
                _quadTreeCullingPass.BuildCommandBuffer(_cmd);
                _frustumCullingPass.BuildCommandBuffer(_cmd);
                _populateVisibilityAndIndirectArgPass.BuildCommandBuffer(_cmd);
            }
        }

        static readonly ProfilerMarker s_drawIndirectMarker = new ProfilerMarker("IndirectRender.DrawIndirect");
        void DrawIndirect()
        {
            using (s_drawIndirectMarker.Auto())
                _indirectDrawer.DrawIndirect();
        }
    }
}