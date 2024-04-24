using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public class CullingHelper
    {
        int[] _cullingParameters = new int[4] { 0, 0, 0, 0 };
        UnsafeList<UnsafeList<PlanePacket4>> _packedPlanesArray;
        Vector4[] _managedPackedPlanes = new Vector4[Utility.c_MaxPackedPlaneCount * 4];

        public static readonly int s_CullingParametersID = Shader.PropertyToID("_CullingParameters");
        public static readonly int s_PackedPlanesID = Shader.PropertyToID("_PackedPlanes");

        public void Init()
        { 
        }

        public void Dispose()
        {
            foreach (var packedPlanes in _packedPlanesArray)
                packedPlanes.Dispose();
            _packedPlanesArray.Dispose();
        }

        public JobHandle Dispose(JobHandle dependency)
        {
            JobHandle jobHandle = new DisposeJob
            {
                PackedPlanesArray = _packedPlanesArray
            }.Schedule(dependency);

            return jobHandle;
        }

        [BurstCompile]
        struct DisposeJob : IJob
        {
            public UnsafeList<UnsafeList<PlanePacket4>> PackedPlanesArray;

            public void Execute()
            {
                foreach (var packedPlanes in PackedPlanesArray)
                    packedPlanes.Dispose();
                PackedPlanesArray.Dispose();
            }
        }

        public void UpdateCullinglanes(ref BatchCullingContext cullingContext)
        {
            CullingPlanes cullingPlanes = CullingUtility.CalculateCullingParameters(ref cullingContext, Allocator.Temp);
            _packedPlanesArray = CullingUtility.BuildPlanePackets(ref cullingPlanes, Allocator.TempJob);
        }

        public UnsafeList<PlanePacket4> GetCullinglanes(int splitIndex)
        {
            return _packedPlanesArray[splitIndex];
        }

        public void BuildCommandBuffer(CommandBuffer cmd, ComputeShader computeShader, int splitIndex)
        {
            UnsafeList<PlanePacket4> packedPlanes = _packedPlanesArray[splitIndex];

            _cullingParameters[0] = packedPlanes.Length;
            cmd.SetComputeIntParams(computeShader, s_CullingParametersID, _cullingParameters);

            for (int i = 0; i < packedPlanes.Length; ++i)
            {
                _managedPackedPlanes[i * 4 + 0] = packedPlanes[i].Xs;
                _managedPackedPlanes[i * 4 + 1] = packedPlanes[i].Ys;
                _managedPackedPlanes[i * 4 + 2] = packedPlanes[i].Zs;
                _managedPackedPlanes[i * 4 + 3] = packedPlanes[i].Distances;
            }

            cmd.SetComputeVectorArrayParam(computeShader, s_PackedPlanesID, _managedPackedPlanes);
        }
    }
}