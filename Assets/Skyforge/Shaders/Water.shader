// Sea surface: scrolling noise bends the normal, which drives a sky-coloured fresnel
// reflection and the sun glint. Opaque and unlit by shadows, so it stays cheap on phones.
Shader "Skyforge/Water"
{
    Properties
    {
        _DeepColor ("Deep Colour", Color) = (0.03, 0.16, 0.28, 1)
        _SkyColor ("Reflected Sky", Color) = (0.62, 0.76, 0.90, 1)
        _WaveScale ("Wave Scale", Float) = 0.09
        _WaveSpeed ("Wave Speed", Float) = 0.7
        _WaveStrength ("Wave Strength", Float) = 0.35
        _Gloss ("Sun Glint Sharpness", Float) = 220
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _SkyColor;
                float _WaveScale;
                float _WaveSpeed;
                float _WaveStrength;
                float _Gloss;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Waves(float2 p, float t)
            {
                return ValueNoise(p + t * float2(0.7, 0.3)) + 0.5 * ValueNoise(p * 2.3 - t * float2(0.4, 0.9));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y * _WaveSpeed;
                float2 p = input.positionWS.xz * _WaveScale;
                float h = Waves(p, t);
                float hx = Waves(p + float2(0.35, 0.0), t);
                float hz = Waves(p + float2(0.0, 0.35), t);

                float3 toCamera = GetCameraPositionWS() - input.positionWS;
                float distanceToCamera = length(toCamera);
                float3 viewDir = toCamera / max(distanceToCamera, 0.001);

                // Ripples fade out with distance; far water would only shimmer.
                float ripple = _WaveStrength * saturate(1.0 - distanceToCamera / 900.0);
                float3 normal = normalize(float3((h - hx) * ripple, 1.0, (h - hz) * ripple));

                float fresnel = pow(1.0 - saturate(dot(normal, viewDir)), 4.0);
                float3 color = lerp(_DeepColor.rgb, _SkyColor.rgb, saturate(fresnel * 0.85 + 0.04));

                Light mainLight = GetMainLight();
                float3 halfVector = normalize(mainLight.direction + viewDir);
                float glint = pow(saturate(dot(normal, halfVector)), _Gloss);
                color += mainLight.color * glint * 0.8;

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
