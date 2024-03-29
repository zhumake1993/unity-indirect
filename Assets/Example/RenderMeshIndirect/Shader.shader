Shader"RenderMeshIndirectShader"
{
    SubShader
    {
        Pass
        {
            Tags {"LightMode"="UniversalForward"}

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma editor_sync_compilation
		    #pragma enable_d3d11_debug_symbols

            #include "UnityCG.cginc"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"

            ByteAddressBuffer DataBuffer;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR0;
            };

            uniform float4x4 _ObjectToWorld;

            v2f vert(appdata_base v, uint svInstanceID : SV_InstanceID)
            {
                InitIndirectDrawArgs(0);
                v2f o;
                uint cmdID = GetCommandID(0);
                uint instanceID = GetIndirectInstanceID(svInstanceID);
                float4 wpos = mul(_ObjectToWorld, v.vertex + float4(instanceID, cmdID, 0, 0));
                o.pos = mul(UNITY_MATRIX_VP, wpos);
                o.color = float4(cmdID & 1 ? 0.0f : 1.0f, cmdID & 1 ? 1.0f : 0.0f, instanceID / float(GetIndirectInstanceCount()), 0.0f);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}