Shader "Custom/OptimizedShaderToyUI"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _TimeMult("Time Multiplier", Float) = 0.5
        _MaxSteps("Max Ray Steps", Int) = 50 // Configurable max steps
        _ResolutionScale("Resolution Scale", Range(0.1, 1.0)) = 1.0 // Lower for better performance, will appear blurry
        _TargetFPS("Target FPS", Range(30, 120)) = 60.0 // Target frame rate for background rendering
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
                float _ResolutionScale;
                float _TargetFPS;

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
                    // Quantize time to target FPS to reduce GPU load on high refresh rate monitors
                    // Apply time multiplier AFTER quantization to avoid affecting FPS
                    float t = floor(_Time.y * _TargetFPS) / _TargetFPS * _TimeMult;
                    float2 resolution = _ScreenParams.xy;

                    // Apply resolution scaling with bilinear filtering for blur
                    float2 pixelSize = 1.0 / (_ScreenParams.xy * _ResolutionScale);
                    float2 uvInPixel = frac(i.uv / pixelSize);
                    float2 pixelUV = floor(i.uv / pixelSize) * pixelSize;

                    // Smooth interpolation factor at pixel edges
                    float2 smoothUV = smoothstep(0.0, 1.0, uvInPixel);
                    i.uv = lerp(pixelUV, pixelUV + pixelSize, smoothUV);

                    // Early exit conditions and early optimization
                    if (any(resolution < 1)) return fixed4(0,0,0,1);

                    float s = 0.006, w = 1.0, d = 0.0;
                    float3 q;
                    float3 p = P(t);
                    float3 ro = p;
                    float3 Z = normalize(P(t + 1.0) - p);
                    float3 X = normalize(float3(Z.z,0,-Z.x));

                    // Precompute values used in inner loop
                    float sinHalfT = sin(t * 0.5);
                    float detailFreqBase = 10.0 + sinHalfT;
                    static const float3 dotWeight = float3(0.75, 0.75, 0.75);

                    float3 D;
                    {
                        float2 pos = (i.uv * resolution - (resolution * 0.5)) * rcp(resolution.y);
                        float angle = sin(p.z * 0.15) * 0.3;
                        pos = rot(angle, pos);

                        float3x3 rotMat = float3x3(-X, cross(X, Z), Z);
                        D = mul(rotMat, float3(pos, 1.0));
                    }

                    half3 col = half3(0,0,0);

                    // Reduced iteration count and early exit conditions
                    [loop]
                    for (int steps = 0; s > 0.005 && steps < _MaxSteps; steps++)
                    {
                        p = mad(D, d, ro);
                        q = p;
                        q.xy -= P(q.z).xy;
                        s = 2.75 - length(q.xy);
                        q.x -= 3.0;
                        q.xy *= 0.5;

                        w = 0.5;
                        // Unrolled fractal loop - avoids loop overhead
                        [unroll]
                        for (int j = 0; j < 4; j++)
                        {
                            q = abs(sin(q)) - 1.0;
                            float l = 1.6 * rcp(dot(q,q));
                            q *= l;
                            w *= l;
                        }
                        s = length(q) * rcp(w);

                        col += (half3)abs(sin(p)) * 0.025h;

                        // Precomputed detail frequencies - unrolled with MAD
                        float a = 0.2;
                        float3 pScaled = p * detailFreqBase;
                        [unroll]
                        for (int k = 0; k < 4; k++)
                        {
                            s -= abs(dot(sin(pScaled * a), dotWeight)) * rcp(a) * 0.005;
                            a += a;
                        }
                        d += s;

                        // Early exit if color is negligible
                        if (dot(col, col) < 0.0001h) break;
                    }

                    float expFactor = exp(-d * rcp(abs(4.0 + sin(p.z))));
                    col *= (half)expFactor;
                    col = pow(col, 0.45h);
                    return half4(col, 1.0h);
                }
                ENDCG
            }
        }
            FallBack "Unlit/Texture"
}