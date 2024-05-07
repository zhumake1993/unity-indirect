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
        int _totalIndexCount;
        int _totalVertexCount;

        int[] _meshletIndices;
        List<IndirectVertexData> _meshletVertices;
        Dictionary<IndirectVertexData, int> _meshletVertexMap;
        List<MeshInfo> _meshInfos;

        GraphicsBuffer _indexBuffer;
        GraphicsBuffer _vertexBuffer;

        public void Init(int indexCapacity, int vertexCapacity, int meshletTriangleCount)
        {
            _indexCapacity = indexCapacity;
            _vertexCapacity = vertexCapacity;
            _meshletTriangleCount = meshletTriangleCount;

            _totalIndexCount = 0;
            _totalVertexCount = 0;

            int meshletIndexCount = _meshletTriangleCount * 3;
            _meshletIndices = new int[meshletIndexCount];
            _meshletVertices = new List<IndirectVertexData>(meshletIndexCount);
            _meshletVertexMap = new Dictionary<IndirectVertexData, int>(meshletIndexCount);
            _meshInfos = new List<MeshInfo>();

            _indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _indexCapacity, sizeof(int));
            _vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _vertexCapacity, IndirectVertexData.c_Size);
        }

        public void Dispose()
        {
            foreach(var info in _meshInfos)
                info.Dispose();

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
            if (!Check(mesh))
                return MeshInfo.s_Invalid;

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

                int indexLeft = subMeshDescriptor.indexCount;
                int indexOffset = 0;
                while (indexLeft > 0)
                {
                    int indexCount = math.min(indexLeft, meshletIndexCount);
                    indexLeft -= indexCount;

                    _meshletVertices.Clear();
                    _meshletVertexMap.Clear();

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
                        };

                        if (_meshletVertexMap.TryGetValue(vertex, out int previousIndex))
                        {
                            _meshletIndices[iIndex] = previousIndex;
                        }
                        else
                        {
                            int newIndex = _meshletVertices.Count;
                            _meshletIndices[iIndex] = newIndex;
                            _meshletVertices.Add(vertex);
                            _meshletVertexMap.Add(vertex, newIndex);
                        }
                    }

                    for (; iIndex < meshletIndexCount; ++iIndex)
                    {
                        _meshletIndices[iIndex] = 0;
                    }

                    // upload

                    _indexBuffer.SetData(_meshletIndices, 0, _totalIndexCount, meshletIndexCount);
                    _vertexBuffer.SetData(_meshletVertices, 0, _totalVertexCount, _meshletVertices.Count);

                    MeshletInfo unitMeshInfo = new MeshletInfo()
                    {
                        IndexOffset = _totalIndexCount,
                        VertexOffset = _totalVertexCount,
                        VertexCount = _meshletVertices.Count,
                        AABB = CalculateAABB(_meshletIndices, _meshletVertices),
                    };
                    meshletInfos.Add(unitMeshInfo);

                    _totalIndexCount += meshletIndexCount;
                    _totalVertexCount += _meshletVertices.Count;

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

        bool Check(Mesh mesh)
        {
            if (!mesh.isReadable)
            {
                Utility.LogError($"mesh is not readable, mesh={mesh.name}");
                return false;
            }

            int meshletIndexCount = _meshletTriangleCount * 3;

            int totalExpectedIndexCount = 0;
            int totalExpectedVertexCount = 0;

            int sunMeshCount = mesh.subMeshCount;
            for (int submeshIndex = 0; submeshIndex < sunMeshCount; ++submeshIndex)
            {
                SubMeshDescriptor subMeshDescriptor = mesh.GetSubMesh(submeshIndex);

                int expectedIndexCount = (subMeshDescriptor.indexCount + meshletIndexCount - 1) / meshletIndexCount * meshletIndexCount;
                int expectedVertexCount = expectedIndexCount / 3;

                totalExpectedIndexCount += expectedIndexCount;
                totalExpectedVertexCount += expectedVertexCount;
            }

            if (totalExpectedIndexCount + _totalIndexCount > _indexCapacity)
            {
                Utility.LogError($"index capacity exceeded, mesh={mesh.name}");
                return false;
            }

            if (totalExpectedVertexCount + _totalVertexCount >= _vertexCapacity)
            {
                Utility.LogError($"vertex capacity exceeded, mesh={mesh.name}");
                return false;
            }

            return true;
        }

        AABB CalculateAABB(int[] meshletIndices, List<IndirectVertexData> meshletVertices)
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
                TotalIndexCount = _totalIndexCount,
                TotalVertexCount = _totalVertexCount,
            };

            return stats;
        }
    }

    public struct MeshMergerStats
    {
        public int IndexCapacity;
        public int VertexCapacity;
        public int MeshletTriangleCount;
        public int TotalIndexCount;
        public int TotalVertexCount;
    }
}