Shader "Custom/OptimizedShaderToyUI"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _TimeMult("Time Multiplier", Float) = 0.5
        _MaxSteps("Max Ray Steps", Int) = 50 // Configurable max steps
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
                #pragma target 3.0 // Explicitly set shader target
                #include "UnityCG.cginc"

                sampler2D _MainTex;
                float _TimeMult;
                int _MaxSteps;

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

                // Simplified helper functions
                float fastTanh(float x)
                {
                    return x / (1.0 + abs(x)); // Faster approximation
                }

                float3 P(float z)
                {
                    return float3(
                        fastTanh(cos(z * 0.4) * 0.5) * 8.0,
                        fastTanh(cos(z * 0.5) * 0.5) * 4.0,
                        z
                    );
                }

                float2 rot(float a, float2 v)
                {
                    float c = cos(a), s = sin(a);
                    return float2(c * v.x - s * v.y, s * v.x + c * v.y);
                }

                fixed4 frag(v2f i) : SV_Target
                {
                    float t = _Time.y * _TimeMult;
                    float2 resolution = _ScreenParams.xy;

                    // Early exit conditions and early optimization
                    if (any(resolution < 1)) return fixed4(0,0,0,1);

                    float s = 0.006, w = 1.0, d = 0.0;
                    float3 r = float3(resolution, 0);
                    float3 q;
                    float3 p = P(t);
                    float3 ro = p;
                    float3 Z = normalize(P(t + 1.0) - p);
                    float3 X = normalize(float3(Z.z,0,-Z.x));

                    float3 D;
                    {
                        float2 uv = i.uv;
                        float2 pos = (uv * resolution - (resolution * 0.5)) / resolution.y;
                        float angle = sin(p.z * 0.15) * 0.3;
                        pos = rot(angle, pos);

                        float3 temp = float3(pos, 1.0);
                        float3x3 rotMat = float3x3(-X, cross(X, Z), Z);
                        D = mul(rotMat, temp);
                    }

                    float3 col = float3(0,0,0);

                    // Reduced iteration count and early exit conditions
                    for (int steps = 0; s > 0.005 && steps < _MaxSteps; steps++)
                    {
                        p = ro + D * d;
                        q = p;
                        q.xy -= P(q.z).xy;
                        s = 2.75 - length(q.xy);
                        q.x -= 3.0;
                        q.xy *= 0.5;

                        w = 0.5;
                        // Reduced inner loop iterations
                        for (int j = 0; j < 4; j++) // Reduced from 8
                        {
                            q = abs(sin(q)) - 1.0;
                            float l = 1.6 / dot(q,q);
                            q *= l;
                            w *= l;
                        }
                        s = length(q) / w;

                        col += abs(sin(p)) * 0.025; // Reduced intensity

                        // Simplified additional detail loop
                        float a = 0.2;
                        [unroll(4)] // Explicit unrolling hint
                        for (int k = 0; k < 4; k++) // Reduced iterations
                        {
                            float dotVal = dot(sin(p * a * (10.0 + sin(t * 0.5))),
                                               float3(0.75, 0.75, 0.75));
                            s -= abs(dotVal) / a * 0.005; // Reduced intensity
                            a += a;
                        }
                        d += s;

                        // Early exit if color is negligible
                        if (length(col) < 0.01) break;
                    }

                    col *= exp(-d / abs(4.0 + sin(p.z)));
                    col = pow(col, float3(0.45,0.45,0.45));
                    return float4(col, 1.0);
                }
                ENDCG
            }
        }
            FallBack "Unlit/Texture"
}