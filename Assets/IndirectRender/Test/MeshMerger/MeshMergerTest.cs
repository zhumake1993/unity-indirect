using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using ZGame.Indirect;

public class MeshMergerTest : MonoBehaviour
{
    [Serializable]
    public class TestMesh
    {
        public Mesh Mesh;
        public int SubmeshIndex;
        public bool FlipZ;
    }

    public int IndexCapacity = 265 * 1024;
    public int VertexCapacity = 256 * 1024;
    public int UnitMeshTriangleCount = 16;

    public TestMesh[] TestMeshes;

    public bool ShowInfo = false;
    public int InfoIndex = 0;
    public bool DrawAABB = false;

    MeshMerger _meshMerger;
    Dictionary<Mesh, MeshInfo> _meshInfos = new Dictionary<Mesh, MeshInfo>();

    int _buttonSize = 100;
    GUIStyle _style;

    void Start()
    {
        _meshMerger = new MeshMerger();
        _meshMerger.Init(IndexCapacity, VertexCapacity, UnitMeshTriangleCount);

        foreach (var testMesh in TestMeshes)
        {
            if (testMesh.Mesh != null)
            {
                MeshInfo meshInfo = _meshMerger.Merge(testMesh.Mesh);
                _meshInfos.Add(testMesh.Mesh, meshInfo);
            }
        }

        _style = new GUIStyle();
        _style.fontSize = 15;
        _style.normal.textColor = Color.white;
    }

    void OnDestroy()
    {
        _meshMerger.Dispose();
    }

    void Update()
    {
        
    }

    void OnGUI()
    {
        if (GUILayout.Button("Create", GUILayout.Width(_buttonSize), GUILayout.Height(_buttonSize)))
        {
            int index = 0;
            foreach (var pair in _meshInfos)
            {
                Mesh mesh = pair.Key;
                MeshInfo meshInfo = pair.Value;

                _meshMerger.CreateDebugGameObject(mesh, 0, meshInfo, new Vector3(index * 1.5f, 0, 0));
                index++;
            }
        }

        string log = "";

        if (ShowInfo && InfoIndex < _meshInfos.Count)
        {
            var itr = _meshInfos.GetEnumerator();
            for (int i = 0; i < InfoIndex; ++i)
            {
                itr.MoveNext();
            }
            Mesh mesh = itr.Current.Key;
            MeshInfo meshInfo = itr.Current.Value;

            log += $"mesh={mesh.name},SubmeshIndex={0},UnitMeshCount={meshInfo.SubMeshInfos[0].MeshletInfos.Length}\n";

            UnityEngine.Rendering.SubMeshDescriptor subMeshDescriptor = mesh.GetSubMesh(0);
            log += $"subMeshDescriptor.indexStart={subMeshDescriptor.indexStart},indexCount={subMeshDescriptor.indexCount}," +
                $"baseVertex={subMeshDescriptor.baseVertex},firstVertex={subMeshDescriptor.firstVertex}," +
                $"vertexCount={subMeshDescriptor.vertexCount}\n";

            UnsafeList<MeshletInfo> meshletInfos = meshInfo.SubMeshInfos[0].MeshletInfos;
            foreach (MeshletInfo meshletInfo in meshletInfos)
            {
                log += $"\tIndexOffset={meshletInfo.IndexOffset},VertexOffset={meshletInfo.VertexOffset},VertexCount={meshletInfo.VertexCount}" +
                    $"AABB.Center={meshletInfo.AABB.Center},AABB.Extents={meshletInfo.AABB.Extents}\n";
            }
        }
        GUILayout.Label(log, _style);
    }

    void OnDrawGizmos()
    {
        if (DrawAABB)
        {
            Gizmos.color = Color.green;

            int index = 0;
            foreach (var pair in _meshInfos)
            {
                MeshInfo meshInfo = pair.Value;

                UnsafeList<MeshletInfo> meshletInfos = meshInfo.SubMeshInfos[0].MeshletInfos;
                foreach (MeshletInfo meshletInfo in meshletInfos)
                {
                    Gizmos.DrawWireCube(meshletInfo.AABB.Center + new Unity.Mathematics.float3(index * 1.5f, 0, 0), meshletInfo.AABB.Extents * 2);
                }
                index++;
            }
        }
    }
}
