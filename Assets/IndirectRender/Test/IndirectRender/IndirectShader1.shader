Shader"GPU Driven/IndirectShader1"
{
    Properties
    {
        _IndirectPeoperty0("Color", Color) = (0,0,0,0)
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

            #pragma multi_compile _ ZGAME_INDIRECT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _IndirectPeoperty0;
            CBUFFER_END

            #if ZGAME_INDIRECT
                #define _IndirectPeoperty0 ZGmae_Indirect_Get_Float4(0)
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            #if ZGAME_INDIRECT
            Varyings vert(uint svVertexID : SV_VertexID, uint svInstanceID : SV_InstanceID)
            #else
            Varyings vert(Attributes input)
            #endif
            {
                Varyings output = (Varyings)0;

                #if ZGAME_INDIRECT
                
                ZGmae_Indirect_Setup(svVertexID, svInstanceID);

                IndirectVertexData indirectVertexData = ZGame_Indirect_Get_IndirectVertexData();

                Attributes input = (Attributes)0;
                input.positionOS = indirectVertexData.position;

                #endif

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);

                output.color = _IndirectPeoperty0;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                return input.color;
            }
            
            ENDHLSL
        }
        
        Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }

			ZWrite On
			ZTest LEqual
			ColorMask 0
			Cull Back

			HLSLPROGRAM
			
            #pragma target 4.5
            
            #pragma vertex vert
            #pragma fragment frag

            #pragma editor_sync_compilation
		    #pragma enable_d3d11_debug_symbols

            #pragma multi_compile _ ZGAME_INDIRECT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            #if ZGAME_INDIRECT
            Varyings vert(uint svVertexID : SV_VertexID, uint svInstanceID : SV_InstanceID)
            #else
            Varyings vert(Attributes input)
            #endif
            {
                Varyings output;

                #if ZGAME_INDIRECT
                
                ZGmae_Indirect_Setup(svVertexID, svInstanceID);

                IndirectVertexData indirectVertexData = ZGame_Indirect_Get_IndirectVertexData();

                Attributes input = (Attributes)0;
                input.positionOS = indirectVertexData.position;

                #endif

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                return half4(1.0f, 1.0f, 1.0f, 1.0f);
            }

			ENDHLSL
		}
    }
}