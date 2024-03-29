using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public class MeshMerger
    {
        int _indexCapacity;
        int _vertexCapacity;
        int _unitMeshTriangleCount;
        int _totalIndexCount;
        int _totalVertexCount;

        int[] _unitIndices;
        List<IndirectVertexData> _unitVertices;
        Dictionary<IndirectVertexData, int> _unitVertexMap;

        GraphicsBuffer _indexBuffer;
        GraphicsBuffer _vertexBuffer;

        public void Init(int indexCapacity, int vertexCapacity, int unitMeshTriangleCount)
        {
            _indexCapacity = indexCapacity;
            _vertexCapacity = vertexCapacity;
            _unitMeshTriangleCount = unitMeshTriangleCount;

            _totalIndexCount = 0;
            _totalVertexCount = 0;

            int unitMeshIndexCount = _unitMeshTriangleCount * 3;
            _unitIndices = new int[unitMeshIndexCount];
            _unitVertices = new List<IndirectVertexData>(unitMeshIndexCount);
            _unitVertexMap = new Dictionary<IndirectVertexData, int>(unitMeshIndexCount);

            _indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _indexCapacity, sizeof(int));
            _vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _vertexCapacity, IndirectVertexData.c_Size);
        }

        public void Dispose()
        {
            _indexBuffer.Release();
            _vertexBuffer.Release();
        }

        public int GetUnitMeshIndexCount()
        {
            return _unitMeshTriangleCount * 3;
        }

        public GraphicsBuffer GetIndexBuffer()
        {
            return _indexBuffer;
        }

        public GraphicsBuffer GetVertexBuffer()
        {
            return _vertexBuffer;
        }

        public MeshInfo Merge(MeshKey meshKey)
        {
            if (!Check(meshKey))
                return MeshInfo.s_Invalid;

            Mesh mesh = meshKey.Mesh;
            int submeshIndex = meshKey.SubmeshIndex;
            bool flipZ = meshKey.FlipZ;

            SubMeshDescriptor subMeshDescriptor = mesh.GetSubMesh(submeshIndex);

            int unitMeshIndexCount = _unitMeshTriangleCount * 3;
            List<UnitMeshInfo> unitMeshInfos = new List<UnitMeshInfo>();

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
                int indexCount = math.min(indexLeft, unitMeshIndexCount);
                indexLeft -= indexCount;

                _unitVertices.Clear();
                _unitVertexMap.Clear();

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

                    if (_unitVertexMap.TryGetValue(vertex, out int previousIndex))
                    {
                        _unitIndices[iIndex] = previousIndex;
                    }
                    else
                    {
                        int newIndex = _unitVertices.Count;
                        _unitIndices[iIndex] = newIndex;
                        _unitVertices.Add(vertex);
                        _unitVertexMap.Add(vertex, newIndex);
                    }
                }

                for (; iIndex < unitMeshIndexCount; ++iIndex)
                {
                    _unitIndices[iIndex] = 0;
                }

                // upload

                _indexBuffer.SetData(_unitIndices, 0, _totalIndexCount, unitMeshIndexCount);
                _vertexBuffer.SetData(_unitVertices, 0, _totalVertexCount, _unitVertices.Count);

                UnitMeshInfo unitMeshInfo = new UnitMeshInfo()
                {
                    IndexOffset = _totalIndexCount,
                    VertexOffset = _totalVertexCount,
                    VertexCount = _unitVertices.Count,
                    AABB = CalculateAABB(),
                };
                unitMeshInfos.Add(unitMeshInfo);

                _totalIndexCount += unitMeshIndexCount;
                _totalVertexCount += _unitVertices.Count;

                indexOffset += indexCount;
            }

            MeshInfo meshInfo = new MeshInfo()
            {
                MeshKey = meshKey,
                UnitMeshInfos = unitMeshInfos,
            };

            return meshInfo;
        }

        bool Check(MeshKey meshKey)
        {
            Mesh mesh = meshKey.Mesh;
            int submeshIndex = meshKey.SubmeshIndex;

            if (!mesh.isReadable)
            {
                Utility.LogError($"mesh is not readable, mesh={mesh.name}");
                return false;
            }

            SubMeshDescriptor subMeshDescriptor = mesh.GetSubMesh(submeshIndex);

            int unitMeshIndexCount = _unitMeshTriangleCount * 3;

            int expectedIndexCount = (subMeshDescriptor.indexCount + unitMeshIndexCount - 1) / unitMeshIndexCount * unitMeshIndexCount;
            if (expectedIndexCount + _totalIndexCount > _indexCapacity)
            {
                Utility.LogError($"index capacity exceeded, mesh={mesh.name}, submeshIndex={submeshIndex}" +
                    $" indexCount={subMeshDescriptor.indexCount}, _totalIndexCount={_totalIndexCount}, _indexCapacity={_indexCapacity}");

                return false;
            }

            int expectedVertexCOunt = expectedIndexCount / 3;
            if (expectedVertexCOunt + _totalVertexCount >= _vertexCapacity)
            {
                Utility.LogError($"vertex capacity exceeded, mesh={mesh.name}, submeshIndex={submeshIndex}" +
                    $" vertexCount={mesh.vertexCount}, _totalVertexCount={_totalVertexCount}, _vertexCapacity={_vertexCapacity}");

                return false;
            }

            return true;
        }

        AABB CalculateAABB()
        {
            int unitMeshIndexCount = _unitMeshTriangleCount * 3;

            int firstIndex = _unitIndices[0];
            IndirectVertexData firstVertex = _unitVertices[firstIndex];

            AABB aabb = new AABB()
            {
                Center = firstVertex.Position.xyz,
                Extents = float3.zero,
            };

            for (int iIndex = 1; iIndex < unitMeshIndexCount; ++iIndex)
            {
                int index = _unitIndices[iIndex];
                IndirectVertexData vertex = _unitVertices[index];
                aabb.Encapsulate(vertex.Position.xyz);
            }

            return aabb;
        }

        public Mesh CreateDebugMesh(UnitMeshInfo unitMeshInfo)
        {
            int unitMeshIndexCount = _unitMeshTriangleCount * 3;

            int[] debugIndices = new int[unitMeshIndexCount];
            IndirectVertexData[] debugVertices = new IndirectVertexData[unitMeshInfo.VertexCount];

            _indexBuffer.GetData(debugIndices, 0, unitMeshInfo.IndexOffset, unitMeshIndexCount);
            _vertexBuffer.GetData(debugVertices, 0, unitMeshInfo.VertexOffset, unitMeshInfo.VertexCount);

            Vector3[] positions = new Vector3[unitMeshInfo.VertexCount];
            Vector3[] normals = new Vector3[unitMeshInfo.VertexCount];
            Vector4[] tangents = new Vector4[unitMeshInfo.VertexCount];
            Color[] colors = new Color[unitMeshInfo.VertexCount];
            Vector2[] uv0 = new Vector2[unitMeshInfo.VertexCount];
            Vector2[] uv1 = new Vector2[unitMeshInfo.VertexCount];
            Vector2[] uv2 = new Vector2[unitMeshInfo.VertexCount];
            Vector2[] uv3 = new Vector2[unitMeshInfo.VertexCount];
            for (int iVertex = 0; iVertex < unitMeshInfo.VertexCount; ++iVertex)
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

        public void CreateDebugGameObject(MeshInfo meshInfo, Vector3 position)
        {
            Unity.Mathematics.Random random = new Unity.Mathematics.Random(1234);

            GameObject root = new GameObject($"{meshInfo.MeshKey.Mesh.name}-{meshInfo.MeshKey.SubmeshIndex}-{meshInfo.MeshKey.FlipZ}-{meshInfo.UnitMeshInfos.Count}");
            root.transform.position = position;

            int unitMeshIndex = 0;
            List<UnitMeshInfo> unitMeshInfos = meshInfo.UnitMeshInfos;
            foreach (UnitMeshInfo unitMeshInfo in unitMeshInfos)
            {
                Mesh debugMesh = CreateDebugMesh(unitMeshInfo);

                Material debugMaterial = new Material(Shader.Find("GPU Driven/UnitMeshViewer"));
                debugMaterial.SetColor("_Color", new Color(random.NextFloat(0, 1), random.NextFloat(0, 1), random.NextFloat(0, 1), 1));

                GameObject go = new GameObject($"{unitMeshIndex}-{unitMeshInfo.IndexOffset}-{unitMeshInfo.VertexOffset}-{unitMeshInfo.VertexCount}");
                go.AddComponent<MeshFilter>().sharedMesh = debugMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = debugMaterial;
                go.transform.SetParent(root.transform);
                go.transform.transform.localPosition = Vector3.zero;

                unitMeshIndex++;
            }
        }

        public MeshMergerStats GetMeshMergerStats()
        {
            MeshMergerStats stats = new MeshMergerStats()
            {
                IndexCapacity = _indexCapacity,
                VertexCapacity = _vertexCapacity,
                UnitMeshTriangleCount = _unitMeshTriangleCount,
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
        public int UnitMeshTriangleCount;
        public int TotalIndexCount;
        public int TotalVertexCount;
    }
}