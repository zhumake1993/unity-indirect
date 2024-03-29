using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace ZGame.Indirect
{
    public static class Utility
    {
        public const int c_SizeOfInt = sizeof(int);
        public const int c_SizeOfInt4 = sizeof(int) * 4;
        public const int c_SizeOfFloat4 = sizeof(float) * 4;
        public const int c_SizeOfPackedMatrix = c_SizeOfFloat4 * 3;
        public const int c_SizeOfMatrix = c_SizeOfFloat4 * 4;

        public const int c_SizeOfInt4F4 = 4;
        public const int c_SizeOfFloat4F4 = 1;
        public const int c_SizeOfPackedMatrixF4 = 3;
        public const int c_SizeOfMatrixF4 = 4;

        public const int c_MeshIDInitialCapacity = 256;
        public const int c_MaterialIDInitialCapacity = 256;
        public const int c_UserIDInitialCapacity = 16 * 1024;
        public const int c_CmdIDInitialCapacity = 16 * 1024;

        public const int c_MaxPackedPlaneCount = 16;

        // must be equal to the counterparts in QuadTreeCommon.hlsl
        public const int c_QuadTreeLod0NodeSize = 64;
        public const int c_QuadTreeSubNodeSize = 8;
        public const int c_QuadTreeSubNodeRange = 8;
        public const int c_QuadTreeMaxLodNum = 8;
        public const int c_QuadTreeNodeHeight = 16;

        public static int[] s_IndirectPeopertyIDs = new int[4]
        {   Shader.PropertyToID("_IndirectPeoperty0"),
            Shader.PropertyToID("_IndirectPeoperty1"),
            Shader.PropertyToID("_IndirectPeoperty2"),
            Shader.PropertyToID("_IndirectPeoperty3")};

        // float4x4 extensions

        public static Vector3 ExtractPosition(this float4x4 matrix)
        {
            Vector3 position;
            position.x = matrix[3][0];
            position.y = matrix[3][1];
            position.z = matrix[3][2];

            return position;
        }

        public static Quaternion ExtractRotation(this float4x4 matrix)
        {
            Vector3 forward;
            forward.x = matrix[2][0];
            forward.y = matrix[2][1];
            forward.z = matrix[2][2];

            Vector3 upwards;
            upwards.x = matrix[1][0];
            upwards.y = matrix[1][1];
            upwards.z = matrix[1][2];

            return Quaternion.LookRotation(forward, upwards);
        }

        public static Vector3 ExtractScale(this float4x4 matrix)
        {
            Vector3 scale;
            scale.x = math.length(matrix[0]);
            scale.y = math.length(matrix[1]);
            scale.z = math.length(matrix[2]);

            return scale;
        }

        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        public static void LogError(string message)
        {
            Debug.LogError($"[Indirect] {message}");
        }

        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        public static void LogWarning(string message)
        {
            Debug.Log($"[Indirect] {message}");
        }

        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        public static void Assert(bool value)
        {
            if (!value)
            {
                throw new Exception();
            }
        }
    }
}