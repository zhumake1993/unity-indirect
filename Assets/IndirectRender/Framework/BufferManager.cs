using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ZGame.Indirect
{
    public class BufferManager
    {
        IndirectRenderSetting _setting;

        public GraphicsBuffer IndexBuffer;
        public GraphicsBuffer InstanceDescriptorBuffer;
        public GraphicsBuffer BatchDescriptorBuffer;
        public GraphicsBuffer InstanceDataBuffer;

        List<GraphicsBuffer> _unusedVisibilityBuffers = new List<GraphicsBuffer>();
        List<GraphicsBuffer> _usedVisibilityBuffers = new List<GraphicsBuffer>();
        List<GraphicsBuffer> _unusedIndirectArgsBuffers = new List<GraphicsBuffer>();
        List<GraphicsBuffer> _usedIndirectArgsBuffers = new List<GraphicsBuffer>();

        public static readonly int s_IndexBufferID = Shader.PropertyToID("IndexBuffer");
        public static readonly int s_InstanceDescriptorBufferID = Shader.PropertyToID("InstanceDescriptorBuffer");
        public static readonly int s_BatchDescriptorBufferID = Shader.PropertyToID("BatchDescriptorBuffer");
        public static readonly int s_InstanceDataBufferID = Shader.PropertyToID("InstanceDataBuffer");
        public static readonly int s_VisibilityBufferID = Shader.PropertyToID("VisibilityBuffer");
        public static readonly int s_IndirectArgsBufferID = Shader.PropertyToID("IndirectArgsBuffer");

        public void Init(IndirectRenderSetting setting)
        {
            _setting = setting;

            IndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, setting.InstanceCapacity, Utility.c_SizeOfInt4);
            InstanceDescriptorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, setting.InstanceCapacity, InstanceDescriptor.c_Size);
            BatchDescriptorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, setting.BatchCapacity, Utility.c_SizeOfInt4);
            InstanceDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, (int)(setting.InstanceDataMaxSizeBytes * setting.InstanceDataNumMaxSizeBlocks) / Utility.c_SizeOfFloat4, Utility.c_SizeOfFloat4);
        }

        public void Dispose()
        {
            IndexBuffer.Dispose();
            InstanceDescriptorBuffer.Dispose();
            BatchDescriptorBuffer.Dispose();
            InstanceDataBuffer.Dispose();

            foreach (var buffer in _unusedVisibilityBuffers)
                buffer.Dispose();
            _unusedVisibilityBuffers.Clear();

            foreach (var buffer in _usedVisibilityBuffers)
                buffer.Dispose();
            _usedVisibilityBuffers.Clear();

            foreach (var buffer in _unusedIndirectArgsBuffers)
                buffer.Dispose();
            _unusedIndirectArgsBuffers.Clear();

            foreach (var buffer in _usedIndirectArgsBuffers)
                buffer.Dispose();
            _usedIndirectArgsBuffers.Clear();
        }

        public GraphicsBuffer GetVisibilityBuffer()
        {
            if (_unusedVisibilityBuffers.Count > 0)
            {
                var buffer = _unusedVisibilityBuffers[_unusedVisibilityBuffers.Count - 1];
                _unusedVisibilityBuffers.RemoveAt(_unusedVisibilityBuffers.Count - 1);
                _usedVisibilityBuffers.Add(buffer);
                return buffer;
            }
            else
            {
                var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _setting.InstanceCapacity, Utility.c_SizeOfInt4);
                _usedVisibilityBuffers.Add(buffer);
                return buffer;
            }
        }

        public GraphicsBuffer GetIndirectArgsBuffer()
        {
            if (_unusedIndirectArgsBuffers.Count > 0)
            {
                var buffer = _unusedIndirectArgsBuffers[_unusedIndirectArgsBuffers.Count - 1];
                _unusedIndirectArgsBuffers.RemoveAt(_unusedIndirectArgsBuffers.Count - 1);
                _usedIndirectArgsBuffers.Add(buffer);
                return buffer;
            }
            else
            {
                var buffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, _setting.BatchCapacity, GraphicsBuffer.IndirectDrawArgs.size);
                _usedIndirectArgsBuffers.Add(buffer);
                return buffer;
            }
        }

        public void Recycle()
        {
            foreach (var buffer in _usedVisibilityBuffers)
            {
                _unusedVisibilityBuffers.Add(buffer);
            }
            _usedVisibilityBuffers.Clear();

            foreach (var buffer in _usedIndirectArgsBuffers)
            {
                _unusedIndirectArgsBuffers.Add(buffer);
            }
            _usedIndirectArgsBuffers.Clear();
        }
    }
}