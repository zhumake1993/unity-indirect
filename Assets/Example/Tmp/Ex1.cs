using System;
using UnityEngine;
using UnityEngine.Rendering;

// https://forum.unity.com/threads/gpu-driven-rendering-with-srp-no-drawproceduralindirectnow-for-commandbuffers.1301712/

public class MultiIndirectMeshRenderer : IDisposable
{
    public RenderParams rp;
    public GraphicsBuffer commandBuf;
    public GraphicsBuffer.IndirectDrawIndexedArgs[] multiDrawCommands;
    public Mesh[] mesh { get; private set; }
    public int[] meshInstanceCount;
    public int InstanceCount { get; set; }
    public bool castshadow;
    private Mesh mergedMesh;
    public int totalCount = 0;

    public void IndirectMeshRenderer(Mesh[] _Mesh, Material _Material, int[] _InstanceCount, bool _CastShadow)
    {
        mesh = _Mesh;
        meshInstanceCount = _InstanceCount;
        commandBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, mesh.Length, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        multiDrawCommands = new GraphicsBuffer.IndirectDrawIndexedArgs[mesh.Length];

        if (mergedMesh != null)
        {
            UnityEngine.Object.Destroy(mergedMesh);
        }
        mergedMesh = new Mesh();

        int vertexCount = 0;
        int indexCount = 0;
        foreach (Mesh m in mesh)
        {

            vertexCount += m.vertexCount;
            indexCount += (int)m.triangles.Length;
        }

        castshadow = _CastShadow;

        int currentVertexCount = 0;
        int currentIndexCount = 0;
        int Startinstance = 0;
        totalCount = 0;

        Vector3[] vertices = new Vector3[vertexCount];
        int[] indices = new int[indexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector2[] uv = new Vector2[vertexCount];
        Vector2[] uv2 = new Vector2[vertexCount];
        Vector2[] uv3 = new Vector2[vertexCount];
        Vector2[] uv4 = new Vector2[vertexCount];
        for (int i = 0; i < mesh.Length; i++)
        {
            Mesh m = generateVertexId(mesh[i], i, Startinstance);
            Array.Copy(m.vertices, 0, vertices, currentVertexCount, m.vertexCount);
            Array.Copy(m.triangles, 0, indices, currentIndexCount, m.triangles.Length);
            Array.Copy(m.normals, 0, normals, currentVertexCount, m.vertexCount);
            Array.Copy(m.uv, 0, uv, currentVertexCount, m.vertexCount);
            Array.Copy(m.uv2, 0, uv2, currentVertexCount, m.vertexCount);

            multiDrawCommands[i].baseVertexIndex = (uint)currentVertexCount;
            multiDrawCommands[i].indexCountPerInstance = (uint)m.triangles.Length;
            multiDrawCommands[i].instanceCount = (uint)meshInstanceCount[i]; // Instance par mesh
            multiDrawCommands[i].startIndex = (uint)currentIndexCount;
            multiDrawCommands[i].startInstance = (uint)Startinstance;

            currentVertexCount += m.vertexCount;
            currentIndexCount += (int)m.triangles.Length;

            Startinstance += meshInstanceCount[i];
            totalCount += meshInstanceCount[i];
        }

        mergedMesh.vertices = vertices;
        mergedMesh.triangles = indices;
        mergedMesh.normals = normals;
        mergedMesh.uv = uv;
        mergedMesh.uv2 = uv2;
        mergedMesh.RecalculateTangents();

        rp = new RenderParams(_Material);
        rp.worldBounds = new Bounds(Vector3.zero, 10000 * Vector3.one); // use tighter bounds for better FOV culling
        if (_CastShadow) rp.shadowCastingMode = ShadowCastingMode.On;
        else rp.shadowCastingMode = ShadowCastingMode.Off;
        rp.receiveShadows = true;
        rp.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
        rp.lightProbeUsage = LightProbeUsage.BlendProbes;
        rp.camera = Camera.current;

        commandBuf.SetData(multiDrawCommands);
    }

    public void Render()
    {
        Graphics.RenderMeshIndirect(rp, mergedMesh, commandBuf, mesh.Length, 0);
    }

    public void Dispose()
    {
        if (commandBuf != null)
        {
            commandBuf.Release();
            commandBuf = null;
        }

    }

    Mesh generateVertexId(Mesh m, int mId, int id)
    {
        Mesh mid = new Mesh();
        mid.vertices = m.vertices;
        mid.triangles = m.triangles;
        mid.normals = m.normals;
        mid.tangents = m.tangents;
        mid.uv = m.uv;

        mid.colors = m.colors;
        Vector2[] uvid = new Vector2[m.vertices.Length];
        for (int i = 0; i < m.vertices.Length; i++)
        {
            uvid[i] = new Vector2(mId, id);
        }
        mid.uv2 = uvid;
        return mid;
    }
}