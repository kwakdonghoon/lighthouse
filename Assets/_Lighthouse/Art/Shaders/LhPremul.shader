// M4: 연출 층(알파 + 가산을 한 장으로 합친 premultiplied RGBA)을 장면 위에 얹는 단순 스프라이트 셰이더.
// 화면 = 장면 * (1 - A) + P   (Blend One OneMinusSrcAlpha). 알파가 0 인 픽셀의 색은 그대로 더해진다(가산 'lighter').
Shader "Lighthouse/SpritePremul"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Name "Universal2D"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; };
            V vert(A a) { V v; v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv; v.col = a.col; return v; }
            half4 frag(V v) : SV_Target { half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, v.uv); return c * v.col.a; }
            ENDHLSL
        }
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; };
            V vert(A a) { V v; v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv; v.col = a.col; return v; }
            half4 frag(V v) : SV_Target { half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, v.uv); return c * v.col.a; }
            ENDHLSL
        }
    }
}
