using System;
using System.Collections.Generic;
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
    List<MeshInfo> _meshInfos = new List<MeshInfo>();

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
                MeshKey meshKey = new MeshKey
                {
                    Mesh = testMesh.Mesh,
                    SubmeshIndex = testMesh.SubmeshIndex,
                    FlipZ = testMesh.FlipZ
                };

                MeshInfo meshInfo = _meshMerger.Merge(meshKey);
                _meshInfos.Add(meshInfo);
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
            foreach (var meshInfo in _meshInfos)
            {
                _meshMerger.CreateDebugGameObject(meshInfo, new Vector3(index * 1.5f, 0, 0));
                index++;
            }
        }

        string log = "";

        if (ShowInfo && InfoIndex < _meshInfos.Count)
        {
            var _meshInfo = _meshInfos[InfoIndex];
            var meshKey = _meshInfo.MeshKey;

            log += $"mesh={meshKey.Mesh.name},SubmeshIndex={meshKey.SubmeshIndex},FlipZ={meshKey.FlipZ},UnitMeshCount={_meshInfo.UnitMeshInfos.Count}\n";

            UnityEngine.Rendering.SubMeshDescriptor subMeshDescriptor = meshKey.Mesh.GetSubMesh(meshKey.SubmeshIndex);
            log += $"subMeshDescriptor.indexStart={subMeshDescriptor.indexStart},indexCount={subMeshDescriptor.indexCount}," +
                $"baseVertex={subMeshDescriptor.baseVertex},firstVertex={subMeshDescriptor.firstVertex}," +
                $"vertexCount={subMeshDescriptor.vertexCount}\n";

            List<UnitMeshInfo> unitMeshInfos = _meshInfo.UnitMeshInfos;
            foreach (UnitMeshInfo unitMeshInfo in unitMeshInfos)
            {
                log += $"\tIndexOffset={unitMeshInfo.IndexOffset},VertexOffset={unitMeshInfo.VertexOffset},VertexCount={unitMeshInfo.VertexCount}" +
                    $"AABB.Center={unitMeshInfo.AABB.Center},AABB.Extents={unitMeshInfo.AABB.Extents}\n";
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
            foreach (var meshInfo in _meshInfos)
            {
                List<UnitMeshInfo> unitMeshInfos = meshInfo.UnitMeshInfos;
                foreach (UnitMeshInfo unitMeshInfo in unitMeshInfos)
                {
                    Gizmos.DrawWireCube(unitMeshInfo.AABB.Center + new Unity.Mathematics.float3(index * 1.5f, 0, 0), unitMeshInfo.AABB.Extents * 2);
                }
                index++;
            }
        }
    }
}
