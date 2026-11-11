// Full-screen edge lines: hairline white lines along block edges and silhouettes.
// One-sided detection keeps lines a single pixel wide; "Pencil" fades strokes in and
// out along their length for a hand-drawn feel (no speckle).
Shader "Maze/EdgeGlow"
{
    Properties
    {
        _EdgeColor ("Edge colour", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 0.8
        _DepthSensitivity ("Depth sensitivity", Float) = 30
        _NormalSensitivity ("Normal sensitivity", Float) = 1.5
        _NormalThreshold ("Normal threshold", Range(0, 1)) = 0.5
        _FadeDistance ("Fade out distance (m)", Float) = 60
        _Pencil ("Pencil (strokes fade in/out)", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "EdgeGlow"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float4 _EdgeColor;
            float _Intensity, _DepthSensitivity, _NormalSensitivity, _NormalThreshold, _FadeDistance, _Pencil;

            float DepthAt(float2 pix) { return LinearEyeDepth(LoadSceneDepth(uint2(pix)), _ZBufferParams); }
            float3 NormalAt(float2 uv) { return SampleSceneNormals(uv); }
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float a = Hash(i), b = Hash(i + float2(1, 0)), c = Hash(i + float2(0, 1)), d = Hash(i + 1);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float2 pix = floor(uv * _ScreenSize.xy);

                float d0 = DepthAt(pix);
                float dl = DepthAt(pix + float2(-1, 0)), dr = DepthAt(pix + float2(1, 0));
                float dd = DepthAt(pix + float2(0, -1)), du = DepthAt(pix + float2(0, 1));
                // one-sided: only the nearer pixel at a depth jump lights up -> 1 px wide line
                float lapX = (dl + dr - 2 * d0), lapY = (dd + du - 2 * d0);
                float dEdge = saturate(max(max(lapX, lapY), 0) / max(d0, 0.001) * _DepthSensitivity - 0.25);

                // creases between faces: compare only with right/up neighbours -> 1 px wide
                float3 n0 = NormalAt(uv);
                float nd = max(1 - dot(NormalAt(uv + float2(_ScreenSize.z, 0)), n0), 1 - dot(NormalAt(uv + float2(0, _ScreenSize.w)), n0));
                float nEdge = saturate((nd - _NormalThreshold) * _NormalSensitivity);

                float e = max(dEdge, nEdge);
                float stroke = smoothstep(0.2, 0.7, Noise(pix * 0.015 + floor(_Time.y * 6) * 1.7));
                e *= lerp(1, stroke, _Pencil);

                float fade = 1 - saturate(d0 / _FadeDistance);
                float sky = step(d0, _ProjectionParams.z * 0.98);
                half3 col = src.rgb + _EdgeColor.rgb * e * _Intensity * fade * sky;
                return half4(col, src.a);
            }
            ENDHLSL
        }
    }
}
