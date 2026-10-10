// Smoke, dust, fire and sparks: a soft sprite tinted by the particle's colour. One shader serves
// both kinds of blending; the material picks which through _SrcBlend and _DstBlend.
Shader "Skyforge/Particle"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _Glow ("Brightness", Float) = 1
        _Additive ("Additive (0 or 1)", Float) = 0
        [HideInInspector] _SrcBlend ("Src", Float) = 5
        [HideInInspector] _DstBlend ("Dst", Float) = 10
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Glow;
                float _Additive;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 sprite = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 color = sprite * input.color;
                color.rgb *= _Glow;
                half clear = ComputeFogIntensity(input.fogFactor);
                // Smoke takes on the colour of the haze; light simply fades in it.
                half3 hazed = lerp(unity_FogColor.rgb, color.rgb, clear);
                color.rgb = lerp(hazed, color.rgb, _Additive);
                color.a *= lerp(1.0, clear, _Additive);
                return color;
            }
            ENDHLSL
        }
    }
}
