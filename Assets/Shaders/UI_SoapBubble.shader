Shader "Nekolpos/UI/SoapBubble"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Phase ("Phase", Float) = 0
        _Selected ("Selected", Float) = 0
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.55
        _FilmStrength ("Film Strength", Range(0, 1)) = 0.22
        _SurfaceWobble ("Surface Wobble", Range(0, 1)) = 0.08

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float _Phase;
            float _Selected;
            float _RimStrength;
            float _FilmStrength;
            float _SurfaceWobble;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.uv = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 sprite = tex2D(_MainTex, IN.uv) * IN.color;
                float2 centered = IN.uv * 2.0 - 1.0;
                float radius = length(centered);
                float inside = smoothstep(1.0, 0.965, radius);

                float phase = _Phase * 0.25;
                float angle = atan2(centered.y, centered.x);
                float wobble = sin(angle * 3.0 + phase * 2.7) * 0.018 * _SurfaceWobble;
                float rimRadius = saturate(radius + wobble);
                float rim = smoothstep(0.72, 1.0, rimRadius) * smoothstep(1.02, 0.94, rimRadius);
                float outerLine = smoothstep(0.90, 1.0, rimRadius) * smoothstep(1.01, 0.985, rimRadius);
                float centerFilm = (1.0 - smoothstep(0.05, 0.72, radius)) * 0.018;

                float sweep = sin(angle * 2.0 + phase * 1.9) * 0.5 + 0.5;
                float micro = sin((centered.x * 5.1 + centered.y * 3.7) + phase * 2.2) * 0.5 + 0.5;
                float filmMask = rim * (0.40 + sweep * 0.35 + micro * 0.25);
                fixed3 filmColor = fixed3(
                    0.62 + 0.26 * sin(angle + phase),
                    0.82 + 0.14 * sin(angle * 1.7 + phase + 1.4),
                    1.00
                );
                filmColor = lerp(filmColor, fixed3(1.0, 0.68, 0.86), sweep * 0.22);

                float crescent = smoothstep(0.46, 0.38, length(centered - float2(-0.36, 0.42)))
                               * smoothstep(0.20, 0.29, length(centered - float2(-0.29, 0.36)));
                crescent *= smoothstep(0.98, 0.78, radius);

                float lowerGlow = smoothstep(0.34, 0.20, length(centered - float2(0.39, -0.45))) * 0.55;
                lowerGlow *= smoothstep(0.94, 0.66, radius);

                fixed3 color = filmColor * filmMask * _FilmStrength;
                color += fixed3(1.0, 1.0, 1.0) * (outerLine * _RimStrength + crescent * 0.28 + lowerGlow * 0.10);
                color += fixed3(0.72, 0.92, 1.0) * centerFilm;
                color *= 1.0 + _Selected * 0.18;

                float alpha = inside * sprite.a;
                alpha *= centerFilm + rim * 0.28 + outerLine * 0.28 + crescent * 0.24 + lowerGlow * 0.10;
                alpha = saturate(alpha * (1.0 + _Selected * 0.18));

                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif

                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
}
