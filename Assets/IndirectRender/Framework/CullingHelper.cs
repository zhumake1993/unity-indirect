using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public class CullingHelper
    {
        Plane[] _planeArray = new Plane[6];
        Vector4[] _packedPlanes = new Vector4[Utility.c_MaxPackedPlaneCount * 4];
        int[] _packedPlaneCount = new int[4] { 0, 0, 0, 0 };

        static readonly int s_packedPlaneCountID = Shader.PropertyToID("_PackedPlaneCount");
        static readonly int s_packedPlanesID = Shader.PropertyToID("_PackedPlanes");

        public void Init()
        {
            //
        }

        public void Dispose()
        {
            //
        }

        public void UpdateCameraFrustumPlanes(Camera camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, _planeArray);

            NativeArray<Plane> planes = new NativeArray<Plane>(6, Allocator.Temp);
            for (int i = 0; i < 6; ++i)
            {
                planes[i] = _planeArray[i];
            }

            NativeArray<PlanePacket4> packedPlanes = CullingUtility.BuildSOAPlanePackets(planes, Allocator.Temp);

            if (packedPlanes.Length > Utility.c_MaxPackedPlaneCount)
            {
                Utility.LogWarning($"packed plane count exceeded," +
                    $" packedPlaneCount={packedPlanes.Length}, maxPackedPlaneCount={Utility.c_MaxPackedPlaneCount}");
                return;
            }

            for (int i = 0; i < packedPlanes.Length; ++i)
            {
                _packedPlanes[i * 4 + 0] = packedPlanes[i].Xs;
                _packedPlanes[i * 4 + 1] = packedPlanes[i].Ys;
                _packedPlanes[i * 4 + 2] = packedPlanes[i].Zs;
                _packedPlanes[i * 4 + 3] = packedPlanes[i].Distances;
            }

            _packedPlaneCount[0] = packedPlanes.Length;
        }

        public void SetShaderParams(CommandBuffer cmd, ComputeShader computeShader)
        {
            cmd.SetComputeIntParams(computeShader, s_packedPlaneCountID, _packedPlaneCount);
            cmd.SetComputeVectorArrayParam(computeShader, s_packedPlanesID, _packedPlanes);
        }
    }
}