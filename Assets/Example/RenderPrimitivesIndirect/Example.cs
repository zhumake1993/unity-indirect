using UnityEngine;

namespace RenderPrimitivesIndirect
{
    public class ExampleClass : MonoBehaviour
    {
        public Material material;
        public Mesh mesh;

        GraphicsBuffer meshTriangles;
        GraphicsBuffer meshPositions;
        GraphicsBuffer commandBuf;
        GraphicsBuffer.IndirectDrawArgs[] commandData;
        const int commandCount = 2;

        void Start()
        {
            // note: remember to check "Read/Write" on the mesh asset to get access to the geometry data
            meshTriangles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, mesh.triangles.Length, sizeof(int));
            meshTriangles.SetData(mesh.triangles);
            meshPositions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, mesh.vertices.Length, 3 * sizeof(float));
            meshPositions.SetData(mesh.vertices);
            commandBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, commandCount, GraphicsBuffer.IndirectDrawArgs.size);
            commandData = new GraphicsBuffer.IndirectDrawArgs[commandCount];
        }

        void OnDestroy()
        {
            meshTriangles?.Dispose();
            meshTriangles = null;
            meshPositions?.Dispose();
            meshPositions = null;
            commandBuf?.Dispose();
            commandBuf = null;
        }

        void Update()
        {
            RenderParams rp = new RenderParams(material);
            rp.worldBounds = new Bounds(Vector3.zero, 10000 * Vector3.one); // use tighter bounds
            rp.matProps = new MaterialPropertyBlock();
            rp.matProps.SetBuffer("_Triangles", meshTriangles);
            rp.matProps.SetBuffer("_Positions", meshPositions);
            rp.matProps.SetInt("_BaseVertexIndex", (int)mesh.GetBaseVertex(0));
            rp.matProps.SetMatrix("_ObjectToWorld", Matrix4x4.Translate(new Vector3(-4.5f, 0, 0)));
            commandData[0].vertexCountPerInstance = mesh.GetIndexCount(0);
            commandData[0].instanceCount = 10;
            commandData[1].vertexCountPerInstance = mesh.GetIndexCount(0);
            commandData[1].instanceCount = 10;
            commandBuf.SetData(commandData);
            Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, commandBuf, commandCount);
        }
    }
}