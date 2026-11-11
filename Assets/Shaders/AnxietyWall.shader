// World-space (triplanar) wall shader for the anxiety maze.
// Textures tile evenly no matter how tall the walls are, and the walls can
// "breathe" (push toward the player) and ripple via global values set by AnxietyDirector.
Shader "Maze/AnxietyWall"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _EmissionMap ("Emission (unused, mirrors _BaseMap)", 2D) = "white" {}
        _BaseColor ("Color", Color) = (0.35, 0.35, 0.35, 1)
        _EmissionColor ("Glow", Color) = (0.45, 0.45, 0.45, 1)
        _WorldTiling ("World tiling: x = across (per m), y = up (per m)", Vector) = (0.4, 0.18, 0, 0)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        _Warp ("Warp amount", Float) = 1
        _Lift ("Grey lift (keeps dark textures readable)", Range(0, 1)) = 0.4
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _EmissionMap_ST;
                float4 _BaseColor;
                float4 _EmissionColor;
                float4 _WorldTiling;
                float _Smoothness;
                float _Warp;
                float _Lift;
            CBUFFER_END

            float _MazeBreath;   // walls push toward the corridor (metres)
            float _MazeWarp;     // ripple strength (metres)

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            float3 Displace(float3 ws, float3 nw)
            {
                float t = _Time.y;
                float ripple = sin(ws.y * 0.9 + t * 1.3 + ws.x * 0.31) * 0.6
                             + sin(ws.y * 2.7 - t * 2.3 + ws.z * 0.53) * 0.4;
                float3 n = float3(nw.x, 0, nw.z);
                float grow = saturate(ws.y * 0.35);           // keep the base planted, bend the top
                return ws + n * (_MazeBreath + ripple * _MazeWarp) * _Warp * grow;
            }

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(i.normalOS);
                ws = Displace(ws, nw);
                o.positionWS = ws;
                o.normalWS = nw;
                o.positionCS = TransformWorldToHClip(ws);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half3 Tri(float3 ws, float3 n)
            {
                float3 b = abs(n); b = b / (b.x + b.y + b.z + 1e-4);
                float2 off = _BaseMap_ST.zw;
                float2 uvX = float2(ws.z * _WorldTiling.x, ws.y * _WorldTiling.y) + off;
                float2 uvZ = float2(ws.x * _WorldTiling.x, ws.y * _WorldTiling.y) + off;
                float2 uvY = ws.xz * _WorldTiling.x + off;
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb * b.x
                     + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY).rgb * b.y
                     + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb * b.z;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                half3 tex = Tri(i.positionWS, n);
                half3 albedo = (_Lift + tex * (1 - _Lift)) * _BaseColor.rgb;
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float specPow = exp2(_Smoothness * 10 + 1);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = n;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);

                Light ml = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float ndl = saturate(dot(n, ml.direction));
                float att = ml.shadowAttenuation * ml.distanceAttenuation;
                half3 col = albedo * ml.color * ndl * att;
                col += ml.color * pow(saturate(dot(n, normalize(ml.direction + v))), specPow) * _Smoothness * att * 0.4;
                col += albedo * SampleSH(n);

                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1, 1, 1, 1));
                    float a = l.distanceAttenuation * l.shadowAttenuation;
                    col += albedo * l.color * saturate(dot(n, l.direction)) * a;
                    col += l.color * pow(saturate(dot(n, normalize(l.direction + v))), specPow) * _Smoothness * a * 0.4;
                LIGHT_LOOP_END

                col += tex * _EmissionColor.rgb;
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Universal Render Pipeline/Lit"
}
