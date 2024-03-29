Shader"GPU Driven/MatrixVector1"
{
    Properties
    {
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"
        }

        Pass
        {
            Name "Forward"
            Tags
            {
                "LightMode"="UniversalForward"
            }

            Cull Back

            HLSLPROGRAM
            
            #pragma target 4.5
            
            #pragma vertex vert
            #pragma fragment frag

            #pragma editor_sync_compilation
		    #pragma enable_d3d11_debug_symbols

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"

            struct Descriptor
            {
                int batchID;
                int dataOffset;
            };

            StructuredBuffer<Descriptor> DescriptorBuffer;
            ByteAddressBuffer DataBuffer;
            StructuredBuffer<int> BatchOffsetBuffer;
            StructuredBuffer<int> VisibilityBuffer;

            struct IndirectVertexData
            {
                 float3 position;
                 float3 normal;
                 float4 tangent;
                 float2 uv;
            };

            StructuredBuffer<int> IndirectIndexBuffer;
            StructuredBuffer<IndirectVertexData> IndirectVertexBuffer;

            float4x4 LoadMatrix(int offset)
            {
                offset += 24;
                
                float4 p1 = asfloat(DataBuffer.Load4(offset + 0 * 16));
                float4 p2 = asfloat(DataBuffer.Load4(offset + 1 * 16));
                float4 p3 = asfloat(DataBuffer.Load4(offset + 2 * 16));

                return float4x4(
                    p1.x, p1.w, p2.z, p3.y,
                    p1.y, p2.x, p2.w, p3.z,
                    p1.z, p2.y, p3.x, p3.w,
                    0.0,  0.0,  0.0,  1.0
                );
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(uint svVertexID : SV_VertexID, uint svInstanceID : SV_InstanceID)
            {
                Varyings output;

                InitIndirectDrawArgs(0);

                uint cmdID = GetCommandID(0);
                uint instanceID = GetIndirectInstanceID(svInstanceID);
                int batchOffset = BatchOffsetBuffer[cmdID];
                int instanceIndex = VisibilityBuffer[batchOffset + instanceID];
                Descriptor descriptor = DescriptorBuffer[instanceIndex];
                float4x4 worldMatrix = LoadMatrix(descriptor.dataOffset);

                //int index = GetIndirectVertexID_Base(svVertexID);
                int index = svVertexID + globalIndirectDrawArgs.baseVertexIndex;
                float3 position = IndirectVertexBuffer[index].position;

                // test index buffer
                position.z += IndirectIndexBuffer[svVertexID] * 0.00001f;

                float3 positionWS = mul(worldMatrix, float4(position, 1.0)).xyz;
                output.positionCS = TransformWorldToHClip(positionWS);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                return half4(1.0f, 1.0f, 1.0f, 1.0f) * (1 - 0.0002f);
            }
            
            ENDHLSL
        }
        
//        Pass
//		{
//			Name "ShadowCaster"
//			Tags { "LightMode" = "ShadowCaster" }
//
//			ZWrite On
//			ZTest LEqual
//			ColorMask 0
//			Cull Back
//
//			HLSLPROGRAM
//			
//            #pragma target 4.5
//            
//            #pragma vertex UnlitPassVertex
//            #pragma fragment UnlitPassFragment
//
//            #pragma editor_sync_compilation
//		    #pragma enable_d3d11_debug_symbols
//
//            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
//
//            struct Attributes
//            {
//                float4 positionOS : POSITION;
//                
//                UNITY_VERTEX_INPUT_INSTANCE_ID
//            };
//
//            struct Varyings
//            {
//                float4 positionCS : SV_POSITION;
//
//                UNITY_VERTEX_INPUT_INSTANCE_ID
//            };
//
//            Varyings UnlitPassVertex(Attributes input)
//            {
//                Varyings output;
//
//                UNITY_SETUP_INSTANCE_ID(input);
//                UNITY_TRANSFER_INSTANCE_ID(input, output);
//
//                float3 positionWS = mul(GetObjectToWorldMatrix(), float4(input.positionOS.xyz, 1.0)).xyz;
//                output.positionCS = TransformWorldToHClip(positionWS);
//
//                return output;
//            }
//
//            half4 UnlitPassFragment(Varyings input) : SV_Target
//            {
//                UNITY_SETUP_INSTANCE_ID(input);
//                
//                return half4(1.0f, 1.0f, 1.0f, 1.0f);
//            }
//
//			ENDHLSL
//		}
    }
}