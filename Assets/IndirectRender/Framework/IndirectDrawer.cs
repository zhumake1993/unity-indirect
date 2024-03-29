using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ZGame.Indirect
{
    public unsafe class IndirectDrawer
    {
        IndirectRenderSetting _setting;

        MeshMerger _meshMerger;
        AssetManager _assetManager;
        IndirectRenderUnmanaged* _unmanaged;

        GraphicsBuffer _instanceDescriptorBuffer;
        GraphicsBuffer _batchDescriptorBuffer;
        GraphicsBuffer _instanceDataBuffer;
        GraphicsBuffer _visibilityBuffer;
        GraphicsBuffer _indirectArgsBuffer; // connected

        MaterialPropertyBlock _mpb;

        static readonly int s_instanceDescriptorBufferID = Shader.PropertyToID("InstanceDescriptorBuffer");
        static readonly int s_batchDescriptorBufferID = Shader.PropertyToID("BatchDescriptorBuffer");
        static readonly int s_instanceDataBufferID = Shader.PropertyToID("InstanceDataBuffer");
        static readonly int s_visibilityBufferID = Shader.PropertyToID("VisibilityBuffer");
        static readonly int s_indirectIndexBufferID = Shader.PropertyToID("IndirectIndexBuffer");
        static readonly int s_indirectVertexBufferID = Shader.PropertyToID("IndirectVertexBuffer");

        public void Init(IndirectRenderSetting setting, MeshMerger meshMerger, AssetManager assetManager, IndirectRenderUnmanaged* unmanaged)
        {
            _setting = setting;

            _meshMerger = meshMerger;
            _assetManager = assetManager;
            _unmanaged = unmanaged;

            _instanceDescriptorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, setting.InstanceCapacity, InstanceDescriptor.c_Size);
            _batchDescriptorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, setting.BatchCapacity, Utility.c_SizeOfInt4);
            _instanceDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, (int)(setting.InstanceDataMaxSizeBytes * setting.InstanceDataNumMaxSizeBlocks) / Utility.c_SizeOfFloat4, Utility.c_SizeOfFloat4);
            _visibilityBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, setting.InstanceCapacity, Utility.c_SizeOfInt4);

            _mpb = new MaterialPropertyBlock();
            _mpb.SetBuffer(s_instanceDescriptorBufferID, _instanceDescriptorBuffer);
            _mpb.SetBuffer(s_batchDescriptorBufferID, _batchDescriptorBuffer);
            _mpb.SetBuffer(s_instanceDataBufferID, _instanceDataBuffer);
            _mpb.SetBuffer(s_visibilityBufferID, _visibilityBuffer);
            _mpb.SetBuffer(s_indirectVertexBufferID, _meshMerger.GetVertexBuffer());
            _mpb.SetBuffer(s_indirectIndexBufferID, _meshMerger.GetIndexBuffer());
        }

        public void Dispose()
        {
            _instanceDescriptorBuffer.Dispose();
            _batchDescriptorBuffer.Dispose();
            _instanceDataBuffer.Dispose();
            _visibilityBuffer.Dispose();

            _mpb.Clear();
        }

        public GraphicsBuffer GetInstanceDescriptorBuffer()
        {
            return _instanceDescriptorBuffer;
        }

        public GraphicsBuffer GetBatchDescriptorBuffer()
        {
            return _batchDescriptorBuffer;
        }

        public GraphicsBuffer GetInstanceDataBuffer()
        {
            return _instanceDataBuffer;
        }

        public GraphicsBuffer GetVisibilityBuffer()
        {
            return _visibilityBuffer;
        }

        public void ConnectBuffer(GraphicsBuffer indirectArgsBuffer)
        {
            _indirectArgsBuffer = indirectArgsBuffer;
        }

        public void DrawIndirect()
        {
            foreach (var pair in _unmanaged->IndirectMap)
            {
                IndirectKey indirectKey = pair.Key;
                IndirectBatch indirectBatch = pair.Value;

                Material material = _assetManager.GetMaterial(indirectKey.MaterialID);
                int indirectID = indirectBatch.IndirectID;

                RenderParams renderParams = new RenderParams(material);
                renderParams.worldBounds = new Bounds(Vector3.zero, 10000 * Vector3.one);
                renderParams.matProps = _mpb;

                Graphics.RenderPrimitivesIndirect(renderParams, MeshTopology.Triangles, _indirectArgsBuffer, 1, indirectID);
            }
        }
    }
}