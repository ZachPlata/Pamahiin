Shader "Pamahiin/GhostSpectatorFog"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Inner Mist Tint", Color) = (0.50, 0.80, 1.0, 0.09)
        _EdgeColor ("Outer Edge Tint", Color) = (0.16, 0.30, 0.46, 0.25)
        _FogSpeed ("Fog Drift Speed", Float) = 0.08
        _PixelScale ("Pixel Grid Scale", Float) = 260.0
        _VignettePower ("Vignette Power", Float) = 2.2
        _Intensity ("Global Intensity", Range(0, 1)) = 0.52

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
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
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _EdgeColor;
            float _FogSpeed;
            float _PixelScale;
            float _VignettePower;
            float _Intensity;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color;
                return OUT;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float bayer4x4(float2 pixelCoord)
            {
                int x = ((int)floor(pixelCoord.x)) & 3;
                int y = ((int)floor(pixelCoord.y)) & 3;
                int idx = x + y * 4;
                // Standard normalized 4x4 Bayer matrix threshold in [0..1)
                if (idx == 0)  return  0.0 / 16.0;
                if (idx == 1)  return  8.0 / 16.0;
                if (idx == 2)  return  2.0 / 16.0;
                if (idx == 3)  return 10.0 / 16.0;
                if (idx == 4)  return 12.0 / 16.0;
                if (idx == 5)  return  4.0 / 16.0;
                if (idx == 6)  return 14.0 / 16.0;
                if (idx == 7)  return  6.0 / 16.0;
                if (idx == 8)  return  3.0 / 16.0;
                if (idx == 9)  return 11.0 / 16.0;
                if (idx == 10) return  1.0 / 16.0;
                if (idx == 11) return  9.0 / 16.0;
                if (idx == 12) return 15.0 / 16.0;
                if (idx == 13) return  7.0 / 16.0;
                if (idx == 14) return 13.0 / 16.0;
                return 5.0 / 16.0;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                float aspect = max(0.5, _ScreenParams.x / max(1.0, _ScreenParams.y));
                float2 pixelGrid = floor(float2(uv.x * _PixelScale * aspect, uv.y * _PixelScale));
                float2 qUV = pixelGrid / float2(_PixelScale * aspect, _PixelScale);

                float2 centered = (qUV - 0.5) * float2(1.0, 1.0);
                float dist = length(centered) * 1.4142; // 0 center .. ~1 corners
                float edgeMask = saturate(pow(saturate((dist - 0.50) / 0.50), _VignettePower));

                float t = _Time.y * _FogSpeed;
                float n1 = valueNoise(pixelGrid * 0.08 + float2(t * 2.1, t * 0.9));
                float n2 = valueNoise(pixelGrid * 0.17 + float2(-t * 1.4, t * 1.6));
                float wisp = saturate(n1 * 0.65 + n2 * 0.35);

                float density = saturate(edgeMask * (0.55 + 0.45 * wisp)) * _Intensity;
                float dither = bayer4x4(pixelGrid);
                float quantized = (edgeMask <= 0.001) ? 0.0 : (floor(density * 10.0 + dither * 0.6) / 10.0);

                fixed4 col = lerp(_Color, _EdgeColor, edgeMask);
                col.a *= saturate(quantized) * IN.color.a;
                col.rgb *= IN.color.rgb;
                return col;
            }
            ENDCG
        }
    }
}
