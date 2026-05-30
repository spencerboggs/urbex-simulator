Shader "Urbex/SprayPaintStamp"
{
    Properties
    {
        _MainTex ("Canvas", 2D) = "white" {}
        _BrushTex ("Brush", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _StampData ("Center.xy HalfExtent.xy", Vector) = (0.5, 0.5, 0.1, 0.1)
    }

    SubShader
    {
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _BrushTex;
            fixed4 _Color;
            float4 _StampData;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 canvas = tex2D(_MainTex, i.uv);

                float2 halfExtent = max(_StampData.zw, float2(0.0001, 0.0001));
                float2 stampLocal = (i.uv - _StampData.xy) / halfExtent;
                float2 brushUV = stampLocal * 0.5 + 0.5;

                if (brushUV.x < 0.0 || brushUV.x > 1.0 || brushUV.y < 0.0 || brushUV.y > 1.0)
                    return canvas;

                fixed4 brush = tex2D(_BrushTex, brushUV) * _Color;
                brush.rgb *= brush.a;

                fixed4 result;
                result.rgb = brush.rgb + canvas.rgb * (1.0 - brush.a);
                result.a = brush.a + canvas.a * (1.0 - brush.a);
                return result;
            }
            ENDCG
        }
    }
}
