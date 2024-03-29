using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public unsafe class PopulateVisibilityAndIndirectArgPass : IndirectPass
    {
        IndirectRenderSetting _setting;

        ComputeShader _PopulateVisibilityAndIndirectArgCS;
        int _PopulateVisibilityAndIndirectArgKernel;

        DispatchHelper _dispatchHelper;

        GraphicsBuffer _instanceIndexBuffer; // connected
        GraphicsBuffer _instanceDescriptorBuffer;  // connected
        GraphicsBuffer _batchDescriptorBuffer;  // connected
        GraphicsBuffer _visibilityBuffer;  // connected
        GraphicsBuffer _indirectArgsBuffer;
        
        static readonly int s_instanceIndexBufferID = Shader.PropertyToID("InstanceIndexBuffer");
        static readonly int s_instanceDescriptorBufferID = Shader.PropertyToID("InstanceDescriptorBuffer");
        static readonly int s_indirectArgsBufferID = Shader.PropertyToID("IndirectArgsBuffer");
        static readonly int s_batchDescriptorBufferID = Shader.PropertyToID("BatchDescriptorBuffer");
        static readonly int s_visibilityBufferID = Shader.PropertyToID("VisibilityBuffer");

        public void Init(IndirectRenderSetting setting, ComputeShader frustumCullingCS, DispatchHelper dispatchHelper)
        {
            _setting = setting;

            _PopulateVisibilityAndIndirectArgCS = frustumCullingCS;
            _PopulateVisibilityAndIndirectArgKernel = _PopulateVisibilityAndIndirectArgCS.FindKernel("PopulateVisibilityAndIndirectArg");

            _dispatchHelper = dispatchHelper;

            _indirectArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, setting.BatchCapacity, GraphicsBuffer.IndirectDrawArgs.size);

            _PopulateVisibilityAndIndirectArgCS.SetBuffer(_PopulateVisibilityAndIndirectArgKernel, s_indirectArgsBufferID, _indirectArgsBuffer);
        }

        public void Dispose()
        {
            _indirectArgsBuffer.Dispose();
            _batchDescriptorBuffer.Dispose();
            _visibilityBuffer.Dispose();
        }

        public GraphicsBuffer GetIndirectArgsBuffer()
        {
            return _indirectArgsBuffer;
        }

        public void ConnectBuffer(GraphicsBuffer instanceIndexBuffer, GraphicsBuffer instanceDescriptorBuffer, GraphicsBuffer batchDescriptorBuffer, GraphicsBuffer visibilityBuffer)
        {
            _instanceIndexBuffer = instanceIndexBuffer;
            _instanceDescriptorBuffer = instanceDescriptorBuffer;
            _batchDescriptorBuffer = batchDescriptorBuffer;
            _visibilityBuffer = visibilityBuffer;

            _PopulateVisibilityAndIndirectArgCS.SetBuffer(_PopulateVisibilityAndIndirectArgKernel, s_instanceIndexBufferID, _instanceIndexBuffer);
            _PopulateVisibilityAndIndirectArgCS.SetBuffer(_PopulateVisibilityAndIndirectArgKernel, s_instanceDescriptorBufferID, _instanceDescriptorBuffer);
            _PopulateVisibilityAndIndirectArgCS.SetBuffer(_PopulateVisibilityAndIndirectArgKernel, s_batchDescriptorBufferID, _batchDescriptorBuffer);
            _PopulateVisibilityAndIndirectArgCS.SetBuffer(_PopulateVisibilityAndIndirectArgKernel, s_visibilityBufferID, _visibilityBuffer);
        }

        public void Prepare(IndirectRenderUnmanaged* _unmanaged)
        {
            _batchDescriptorBuffer.SetData(_unmanaged->BatchDescriptorArray, 0, 0, _unmanaged->MaxIndirectID + 1);
            _indirectArgsBuffer.SetData(_unmanaged->IndirectArgsArray, 0, 0, _unmanaged->MaxIndirectID + 1);
        }

        static readonly ProfilerMarker s_populateVisibilityAndIndirectArgMarker = new ProfilerMarker("PopulateVisibilityAndIndirectArg");
        public void BuildCommandBuffer(CommandBuffer cmd)
        {
            cmd.BeginSample(s_populateVisibilityAndIndirectArgMarker);

            _dispatchHelper.AdjustThreadGroupX(cmd, _instanceIndexBuffer);
            _dispatchHelper.Dispatch(cmd, _PopulateVisibilityAndIndirectArgCS, _PopulateVisibilityAndIndirectArgKernel);

            cmd.EndSample(s_populateVisibilityAndIndirectArgMarker);
        }
    }
}