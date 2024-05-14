using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public class MeshMerger
    {
        int _indexCapacity;
        int _vertexCapacity;
        int _meshletTriangleCount;

        List<MeshInfo> _meshInfos;

        BuddyAllocator _indexAllocator;
        BuddyAllocator _vertexAllocator;

        GraphicsBuffer _indexBuffer;
        GraphicsBuffer _vertexBuffer;

        public void Init(int indexCapacity, int vertexCapacity, int meshletTriangleCount)
        {
            _indexCapacity = indexCapacity;
            _vertexCapacity = vertexCapacity;
            _meshletTriangleCount = meshletTriangleCount;

            _meshInfos = new List<MeshInfo>();

            uint minIndexCount = Utility.NextPowerOfTwo((UInt32)(meshletTriangleCount * 3));
            uint maxIndexCount = Utility.NextPowerOfTwo((UInt32)indexCapacity);
            uint minVertexCount = Utility.NextPowerOfTwo((UInt32)(meshletTriangleCount * 3));
            uint maxVertexCount = Utility.NextPowerOfTwo((UInt32)vertexCapacity);

            _indexAllocator.Init(minIndexCount, maxIndexCount, 1);
            _vertexAllocator.Init(minVertexCount, maxVertexCount, 1);

            _indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)maxIndexCount, sizeof(int));
            _vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)maxVertexCount, IndirectVertexData.c_Size);
        }

        public void Dispose()
        {
            foreach(var info in _meshInfos)
                info.Dispose();

            _indexAllocator.Dispose();
            _vertexAllocator.Dispose();

            _indexBuffer.Release();
            _vertexBuffer.Release();
        }

        public int GetMeshletIndexCount()
        {
            return _meshletTriangleCount * 3;
        }

        public GraphicsBuffer GetIndexBuffer()
        {
            return _indexBuffer;
        }

        public GraphicsBuffer GetVertexBuffer()
        {
            return _vertexBuffer;
        }

        public MeshInfo Merge(Mesh mesh)
        {
            if (!mesh.isReadable)
            {
                Utility.LogError($"mesh is not readable, mesh={mesh.name}");
                return MeshInfo.s_Invalid;
            }

            int meshletIndexCount = _meshletTriangleCount * 3;

            MeshInfo meshInfo = new MeshInfo()
            {
                SubMeshInfos = new UnsafeList<SubMeshInfo>(1, Allocator.Persistent),
            };

            int sunMeshCount = mesh.subMeshCount;
            for (int submeshIndex = 0; submeshIndex < sunMeshCount; ++submeshIndex)
            {
                SubMeshDescriptor subMeshDescriptor = mesh.GetSubMesh(submeshIndex);

                UnsafeList<MeshletInfo> meshletInfos = new UnsafeList<MeshletInfo>(1, Allocator.Persistent);

                int[] indices = mesh.triangles;
                Vector3[] positions = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Vector4[] tangents = mesh.tangents;
                Color[] colors = mesh.colors;
                Vector2[] uv0 = mesh.uv;
                Vector2[] uv1 = mesh.uv2;
                Vector2[] uv2 = mesh.uv3;
                Vector2[] uv3 = mesh.uv4;
                Vector2[] uv4 = mesh.uv5;
                Vector2[] uv5 = mesh.uv6;
                Vector2[] uv6 = mesh.uv7;
                Vector2[] uv7 = mesh.uv8;

                int indexLeft = subMeshDescriptor.indexCount;
                int indexOffset = 0;
                while (indexLeft > 0)
                {
                    int indexCount = math.min(indexLeft, meshletIndexCount);
                    indexLeft -= indexCount;

                    NativeList<int> meshletIndices = new NativeList<int>(indexCount, Allocator.Temp);
                    NativeList<IndirectVertexData> meshletVertices = new NativeList<IndirectVertexData>(indexCount, Allocator.Temp);
                    NativeParallelHashMap<IndirectVertexData, int> meshletVertexMap = new NativeParallelHashMap<IndirectVertexData, int>(indexCount, Allocator.Temp);

                    int iIndex = 0;
                    for (; iIndex < indexCount; ++iIndex)
                    {
                        int index = indices[subMeshDescriptor.indexStart + indexOffset + iIndex] + subMeshDescriptor.baseVertex;

                        IndirectVertexData vertex = new IndirectVertexData()
                        {
                            Position = new float4(positions[index], 0),
                            Normal = index < normals.Length ? new float4(normals[index], 0) : float4.zero,
                            Tangent = index < tangents.Length ? tangents[index] : float4.zero,
                            Color = index < colors.Length ? new float4(colors[index].r, colors[index].g, colors[index].b, colors[index].a) : float4.zero,
                            UV0 = index < uv0.Length ? uv0[index] : float2.zero,
                            UV1 = index < uv1.Length ? uv1[index] : float2.zero,
                            UV2 = index < uv2.Length ? uv2[index] : float2.zero,
                            UV3 = index < uv3.Length ? uv3[index] : float2.zero,
                            UV4 = index < uv4.Length ? uv4[index] : float2.zero,
                            UV5 = index < uv5.Length ? uv5[index] : float2.zero,
                            UV6 = index < uv6.Length ? uv6[index] : float2.zero,
                            UV7 = index < uv7.Length ? uv7[index] : float2.zero,
                        };

                        if (meshletVertexMap.TryGetValue(vertex, out int previousIndex))
                        {
                            meshletIndices.Add(previousIndex);
                        }
                        else
                        {
                            int newIndex = meshletVertices.Length;
                            meshletIndices.Add(newIndex);
                            meshletVertices.Add(vertex);
                            meshletVertexMap.Add(vertex, newIndex);
                        }
                    }

                    for (; iIndex < meshletIndexCount; ++iIndex)
                    {
                        meshletIndices.Add(0);
                    }

                    Chunk indexChunk = _indexAllocator.Alloc((UInt32)(meshletIndexCount));
                    if (indexChunk == Chunk.s_InvalidChunk)
                    {
                        Utility.LogErrorBurst($"index allocation failed, index count={meshletIndexCount}");
                        return MeshInfo.s_Invalid;
                    }

                    int startIndex = (int)indexChunk.AddressOf();
                    _indexBuffer.SetData(meshletIndices.AsArray(), 0, startIndex, meshletIndexCount);

                    Chunk vertexChunk = _vertexAllocator.Alloc((UInt32)(meshletVertices.Length));
                    if (vertexChunk == Chunk.s_InvalidChunk)
                    {
                        Utility.LogErrorBurst($"vertex allocation failed, vertex count={meshletVertices.Length}");
                        return MeshInfo.s_Invalid;
                    }

                    int startVertex = (int)vertexChunk.AddressOf();
                    _vertexBuffer.SetData(meshletVertices.AsArray(), 0, startVertex, meshletVertices.Length);

                    MeshletInfo meshletInfo = new MeshletInfo()
                    {
                        IndexOffset = startIndex,
                        VertexOffset = startVertex,
                        VertexCount = meshletVertices.Length,
                        AABB = CalculateAABB(meshletIndices, meshletVertices),
                        IndexChunk = indexChunk,
                        VertexChunk = vertexChunk,
                    };
                    meshletInfos.Add(meshletInfo);

                    indexOffset += indexCount;
                }

                SubMeshInfo subMeshInfo = new SubMeshInfo()
                {
                    MeshletInfos = meshletInfos,
                    AABB = new AABB(subMeshDescriptor.bounds)
                };

                meshInfo.SubMeshInfos.Add(subMeshInfo);
            }

            _meshInfos.Add(meshInfo);

            return meshInfo;
        }

        public void Release(MeshInfo meshInfo)
        {
            foreach (var subMeshInfo in meshInfo.SubMeshInfos)
            {
                foreach (var meshletInfo in subMeshInfo.MeshletInfos)
                {
                    _indexAllocator.Free(meshletInfo.IndexChunk);
                    _vertexAllocator.Free(meshletInfo.VertexChunk);
                }
            }

            _meshInfos.Remove(meshInfo);
            meshInfo.Dispose();
        }

        AABB CalculateAABB(NativeList<int> meshletIndices, NativeList<IndirectVertexData> meshletVertices)
        {
            int meshletIndexCount = _meshletTriangleCount * 3;

            int firstIndex = meshletIndices[0];
            IndirectVertexData firstVertex = meshletVertices[firstIndex];

            AABB aabb = new AABB()
            {
                Center = firstVertex.Position.xyz,
                Extents = float3.zero,
            };

            for (int iIndex = 1; iIndex < meshletIndexCount; ++iIndex)
            {
                int index = meshletIndices[iIndex];
                IndirectVertexData vertex = meshletVertices[index];
                aabb.Encapsulate(vertex.Position.xyz);
            }

            return aabb;
        }

        public Mesh CreateDebugMesh(MeshletInfo meshletInfo)
        {
            int meshletIndexCount = _meshletTriangleCount * 3;

            int[] debugIndices = new int[meshletIndexCount];
            IndirectVertexData[] debugVertices = new IndirectVertexData[meshletInfo.VertexCount];

            _indexBuffer.GetData(debugIndices, 0, meshletInfo.IndexOffset, meshletIndexCount);
            _vertexBuffer.GetData(debugVertices, 0, meshletInfo.VertexOffset, meshletInfo.VertexCount);

            Vector3[] positions = new Vector3[meshletInfo.VertexCount];
            Vector3[] normals = new Vector3[meshletInfo.VertexCount];
            Vector4[] tangents = new Vector4[meshletInfo.VertexCount];
            Color[] colors = new Color[meshletInfo.VertexCount];
            Vector2[] uv0 = new Vector2[meshletInfo.VertexCount];
            Vector2[] uv1 = new Vector2[meshletInfo.VertexCount];
            Vector2[] uv2 = new Vector2[meshletInfo.VertexCount];
            Vector2[] uv3 = new Vector2[meshletInfo.VertexCount];
            for (int iVertex = 0; iVertex < meshletInfo.VertexCount; ++iVertex)
            {
                positions[iVertex] = debugVertices[iVertex].Position.xyz;
                normals[iVertex] = debugVertices[iVertex].Normal.xyz;
                tangents[iVertex] = debugVertices[iVertex].Tangent;
                colors[iVertex] = new Color(debugVertices[iVertex].Color.x, debugVertices[iVertex].Color.y, debugVertices[iVertex].Color.z, debugVertices[iVertex].Color.w);
                uv0[iVertex] = debugVertices[iVertex].UV0;
                uv1[iVertex] = debugVertices[iVertex].UV1;
                uv2[iVertex] = debugVertices[iVertex].UV2;
                uv3[iVertex] = debugVertices[iVertex].UV3;
            }

            Mesh debugMesh = new Mesh();
            debugMesh.vertices = positions;
            debugMesh.normals = normals;
            debugMesh.tangents = tangents;
            debugMesh.colors = colors;
            debugMesh.uv = uv0;
            debugMesh.uv2 = uv1;
            debugMesh.uv3 = uv2;
            debugMesh.uv4 = uv3;
            debugMesh.triangles = debugIndices; // assigning triangles automatically recalculates the bounding volume

            return debugMesh;
        }

        public void CreateDebugGameObject(Mesh mesh, int subMeshIndex, MeshInfo meshInfo, Vector3 position)
        {
            Unity.Mathematics.Random random = new Unity.Mathematics.Random(1234);

            GameObject root = new GameObject($"{mesh.name}-{subMeshIndex}-{meshInfo.SubMeshInfos[subMeshIndex].MeshletInfos.Length}");
            root.transform.position = position;

            int meshletIndex = 0;
            UnsafeList<MeshletInfo> MeshletInfos = meshInfo.SubMeshInfos[subMeshIndex].MeshletInfos;
            foreach (MeshletInfo meshletInfo in MeshletInfos)
            {
                Mesh debugMesh = CreateDebugMesh(meshletInfo);

                Material debugMaterial = new Material(Shader.Find("GPU Driven/UnitMeshViewer"));
                debugMaterial.SetColor("_Color", new Color(random.NextFloat(0, 1), random.NextFloat(0, 1), random.NextFloat(0, 1), 1));

                GameObject go = new GameObject($"{meshletIndex}-{meshletInfo.IndexOffset}-{meshletInfo.VertexOffset}-{meshletInfo.VertexCount}");
                go.AddComponent<MeshFilter>().sharedMesh = debugMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = debugMaterial;
                go.transform.SetParent(root.transform);
                go.transform.transform.localPosition = Vector3.zero;

                meshletIndex++;
            }
        }

        public MeshMergerStats GetMeshMergerStats()
        {
            MeshMergerStats stats = new MeshMergerStats()
            {
                IndexCapacity = _indexCapacity,
                VertexCapacity = _vertexCapacity,
                MeshletTriangleCount = _meshletTriangleCount,
            };

            return stats;
        }
    }

    public struct MeshMergerStats
    {
        public int IndexCapacity;
        public int VertexCapacity;
        public int MeshletTriangleCount;
    }
}