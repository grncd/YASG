Shader "Custom/ShaderToyUI_2"
{
    Properties
    {
        _TimeMult("Time Multiplier", Float) = 1.0
    }
        SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Transparent" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _TimeMult;

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

            float2 rot(float2 p, float a)
            {
                float c = cos(a);
                float s = sin(a);
                return float2(c * p.x - s * p.y, s * p.x + c * p.y);
            }

            float map(float3 p)
            {
                float3 n = float3(0, 1, 0);
                float3 n2 = float3(1, 0, 0);
                float k1 = 1.9;
                float k2 = (sin(p.x * k1) + sin(p.z * k1)) * 0.8;
                float k3 = (sin(p.y * k1) + sin(p.z * k1)) * 0.8;

                float w1 = 4.0 - dot(abs(p), n) + k2;
                float w2 = 4.0 - dot(abs(p), n2) + k3;

                float s1 = length(fmod(p.xy + float2(sin((p.z + p.x) * 2.0) * 0.3, cos((p.z + p.x) * 1.0) * 0.5), 2.0) - 1.0) - 0.2;
                float s2 = length(fmod(0.5 + p.yz + float2(sin((p.z + p.x) * 2.0) * 0.3, cos((p.z + p.x) * 1.0) * 0.3), 2.0) - 1.0) - 0.2;

                return min(w1, min(w2, min(s1, s2)));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = _Time.y * _TimeMult;
                float2 resolution = _ScreenParams.xy;
                float2 uv = i.uv * 2.0 - 1.0;
                uv.x *= resolution.x / resolution.y;

                float3 dir = normalize(float3(uv, 1.0));
                dir.xz = rot(dir.xz, time * 0.23);
                dir = dir.yzx;
                dir.xz = rot(dir.xz, time * 0.2);
                dir = dir.yzx;

                float3 pos = float3(0, 0, time);
                float3 col = float3(0.0, 0.0, 0.0);  // Corrected here
                float t = 0.0;
                float tt = 0.0;

                for (int i = 0; i < 100; i++)
                {
                    tt = map(pos + dir * t);
                    if (tt < 0.001) break;
                    t += tt * 0.45;
                }

                float3 ip = pos + dir * t;
                col = float3(t * 0.1, t * 0.1, t * 0.1);
                col = sqrt(col);

                fixed4 fragColor = fixed4(0.05 * t + abs(dir) * col + max(0.0, map(ip - 0.1) - tt), 1.0);
                fragColor.a = 1.0 / (t * t * t * t);
                return fragColor;
            }
            ENDCG
        }
    }
        FallBack "Unlit/Texture"
}
