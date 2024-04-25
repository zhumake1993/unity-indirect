using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ZGame.Indirect
{
    public class BufferPool
    {
        GraphicsBuffer.Target _target;
        int _count;
        int _stride;
        List<GraphicsBuffer> _unusedBuffers = new List<GraphicsBuffer>();
        List<GraphicsBuffer> _usedBuffers = new List<GraphicsBuffer>();

        public BufferPool(GraphicsBuffer.Target target, int count, int stride)
        {
            _target = target;
            _count = count;
            _stride = stride;
        }

        public void Dispose()
        {
            Recycle();

            foreach (var buffer in _unusedBuffers)
                buffer.Dispose();
        }

        public GraphicsBuffer Get()
        {
            if (_unusedBuffers.Count > 0)
            {
                var buffer = _unusedBuffers[_unusedBuffers.Count - 1];
                _unusedBuffers.RemoveAt(_unusedBuffers.Count - 1);
                _usedBuffers.Add(buffer);
                return buffer;
            }
            else
            {
                var buffer = new GraphicsBuffer(_target, _count, _stride);
                _usedBuffers.Add(buffer);
                return buffer;
            }
        }

        public void Recycle()
        {
            foreach (var buffer in _usedBuffers)
                _unusedBuffers.Add(buffer);
            _usedBuffers.Clear();
        }
    }

    public class BufferManager
    {
        IndirectRenderSetting _setting;

        public GraphicsBuffer InstanceDescriptorBuffer;
        public GraphicsBuffer MeshletDescriptorBuffer;
        public GraphicsBuffer CmdDescriptorBuffer;
        public GraphicsBuffer BatchDescriptorBuffer;
        public GraphicsBuffer InstanceDataBuffer;

        public BufferPool InputIndexBufferPool;
        public BufferPool OutputIndexBufferPool;
        public BufferPool VisibilityBufferPool;
        public BufferPool IndirectArgsBufferPool;

        public static readonly int s_InputIndexBufferID = Shader.PropertyToID("InputIndexBuffer");
        public static readonly int s_OutputIndexBufferID = Shader.PropertyToID("OutputIndexBuffer");
        public static readonly int s_InstanceDescriptorBufferID = Shader.PropertyToID("InstanceDescriptorBuffer");
        public static readonly int s_MeshletDescriptorBufferID = Shader.PropertyToID("MeshletDescriptorBuffer");
        public static readonly int s_CmdDescriptorBufferID = Shader.PropertyToID("CmdDescriptorBuffer");
        public static readonly int s_BatchDescriptorBufferID = Shader.PropertyToID("BatchDescriptorBuffer");
        public static readonly int s_InstanceDataBufferID = Shader.PropertyToID("InstanceDataBuffer");
        public static readonly int s_VisibilityBufferID = Shader.PropertyToID("VisibilityBuffer");
        public static readonly int s_IndirectArgsBufferID = Shader.PropertyToID("IndirectArgsBuffer");

        public void Init(IndirectRenderSetting setting)
        {
            _setting = setting;

            InstanceDescriptorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, setting.InstanceCapacity, InstanceDescriptor.c_Size);
            MeshletDescriptorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, setting.MeshletCapacity, MeshletDescriptor.c_Size);
            CmdDescriptorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, setting.CmdCapacity, CmdDescriptor.c_Size);
            BatchDescriptorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, setting.BatchCapacity, BatchDescriptor.c_Size);
            InstanceDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, (int)(setting.InstanceDataMaxSizeBytes) / Utility.c_SizeOfFloat4, Utility.c_SizeOfFloat4);

            InputIndexBufferPool = new BufferPool(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Counter, setting.MeshletCapacity, Utility.c_SizeOfInt4);
            OutputIndexBufferPool = new BufferPool(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Counter, setting.MeshletCapacity, Utility.c_SizeOfInt4);
            VisibilityBufferPool = new BufferPool(GraphicsBuffer.Target.Structured, setting.InstanceCapacity, Utility.c_SizeOfInt4);
            IndirectArgsBufferPool = new BufferPool(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, setting.BatchCapacity, GraphicsBuffer.IndirectDrawArgs.size);
        }

        public void Dispose()
        {
            InstanceDescriptorBuffer.Dispose();
            MeshletDescriptorBuffer.Dispose();
            CmdDescriptorBuffer.Dispose();
            BatchDescriptorBuffer.Dispose();
            InstanceDataBuffer.Dispose();

            InputIndexBufferPool.Dispose();
            OutputIndexBufferPool.Dispose();
            VisibilityBufferPool.Dispose();
            IndirectArgsBufferPool.Dispose();
        }

        public void RecycleIndexBuffer()
        {
            InputIndexBufferPool.Recycle();
            OutputIndexBufferPool.Recycle();
        }

        public void Recycle()
        {
            InputIndexBufferPool.Recycle();
            OutputIndexBufferPool.Recycle();
            VisibilityBufferPool.Recycle();
            IndirectArgsBufferPool.Recycle();
        }
    }
}