Shader "Custom/ShaderToyUI_Audio_Reactive"
{
    Properties
    {
        _TimeMult("Time Multiplier", Float) = 1.0
        _LowIntensity("Low Frequency Intensity", Range(0, 5)) = 0.0
        _MidIntensity("Mid Frequency Intensity", Range(0, 5)) = 0.0
        _HighIntensity("High Frequency Intensity", Range(0, 5)) = 0.0
        // --- NEW PROPERTY ---
        _GlowRadius("Glow Radius", Range(0.1, 1.0)) = 0.6
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
            float _LowIntensity;
            float _MidIntensity;
            float _HighIntensity;
            float _GlowRadius; // New variable for the radius

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

            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
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
                float3 col = float3(0.0, 0.0, 0.0);
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

                // --- BLURRY AUDIO REACTIVE GLOW LOGIC ---

                // 1. Define the scale for the glowing regions.
                // This is the same value used in the previous version.
                float regionScale = 3.0;
                float3 p_scaled = ip * regionScale;

                // 2. Get the integer part for the cell ID and the fractional part for the position inside the cell.
                float3 p_int = floor(p_scaled);  // The cell's unique ID
                float3 p_frac = frac(p_scaled); // The position within the cell (0..1)

                // 3. Generate a random value based on the cell's ID.
                float h = hash(p_int);

                // 4. Determine the base intensity for this cell based on the hash.
                float audioIntensity = 0.0;
                if (h < 0.33)
                {
                    audioIntensity = pow(_LowIntensity, 2.0);
                }
                else if (h < 0.66)
                {
                    audioIntensity = pow(_MidIntensity, 2.0);
                }
                else
                {
                    audioIntensity = pow(_HighIntensity, 2.0);
                }

                // 5. Calculate the blur/falloff.
                // Find the distance from the center of the cell (0.5).
                float distFromCenter = length(p_frac - 0.5);

                // Use smoothstep to create a soft, circular glow.
                // The glow is 1.0 at the center and fades to 0.0 at the edge defined by _GlowRadius.
                // A smaller _GlowRadius makes the spots smaller/sharper. A larger one makes them bigger/softer.
                float blurFalloff = 1.0 - smoothstep(0.0, _GlowRadius, distFromCenter);

                // 6. Calculate the final glow, which is now white as you requested.
                float3 audioGlow = float3(1.0, 1.0, 1.0) * audioIntensity * blurFalloff;

                // --- MODIFIED FINAL COLOR CALCULATION ---

                // Calculate the original scene color
                float3 sceneColor = 0.05 * t + abs(dir) * col + max(0.0, map(ip - 0.1) - tt);

                // Add the audio-reactive glow on top
                sceneColor += audioGlow;

                // Final output
                fixed4 fragColor = fixed4(sceneColor, 1.0);
                fragColor.a = 1.0 / (t * t * t * t);
                return fragColor;
            }
            ENDCG
        }
    }
        FallBack "Unlit/Texture"
}