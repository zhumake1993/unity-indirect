Shader "XD/TestShader"
{
    Properties
    {
		[MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}

		[HideInInspector] _SrcBlend("__src", Float) = 1.0
		[HideInInspector] _DstBlend("__dst", Float) = 0.0
		[HideInInspector] _ZWrite("__zw", Float) = 1.0
		[HideInInspector] _Cull("__cull", Float) = 2.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Name "TestShader"

			Blend[_SrcBlend][_DstBlend]
			ZWrite[_ZWrite]
			Cull[_Cull]
			
            HLSLPROGRAM

			#pragma target 4.5
			#pragma editor_sync_compilation
			#pragma enable_d3d11_debug_symbols
            
            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

			sampler2D _BaseMap;

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseColor;
                float4 _BaseMap_ST;
			CBUFFER_END

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                float2 texcoord     : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS               : SV_POSITION;
                float2 uv                       : TEXCOORD0;
                float3 positionWS               : TEXCOORD2;
                float3 normalWS                 : TEXCOORD3;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings LitPassVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = vertexInput.positionCS;
                output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
				
                return output;
            }

            half4 LitPassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                //half4 texColor = tex2D(_BaseMap, input.uv);
            	half4 texColor = half4(0,0,0,0);

                Light mainLight = GetMainLight();
                half3 diffuse = _BaseColor.xyz * mainLight.color * saturate(dot(mainLight.direction, input.normalWS));

                return texColor + half4(diffuse, 1);
            }

            ENDHLSL
        }
    }
}