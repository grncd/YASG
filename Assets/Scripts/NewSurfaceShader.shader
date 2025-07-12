Shader "Custom/AbstractBackground"
{
    Properties
    {
        _Speed ("Animation Speed", Float) = 0.65
        _Scale ("Pattern Scale", Float) = -0.75
        _Intensity ("Color Intensity", Float) = 0.7
        _ColorTint ("Color Tint", Color) = (1,1,1,1)
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" }
        LOD 100
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD1;
            };
            
            float _Speed;
            float _Scale;
            float _Intensity;
            float4 _ColorTint;
            
            // Rotation function
            float2 rotate(float2 p, float a)
            {
                float c = cos(a);
                float s = sin(a);
                return float2(p.x * c - p.y * s, p.x * s + p.y * c);
            }
            
            // Noise function for pattern generation
            float noise(float2 p)
            {
                return sin(p.x * 2.0) * sin(p.y * 2.0) * 0.5 + 0.5;
            }
            
            // Pattern function inspired by the original map function
            float pattern(float3 p)
            {
                float time = _Time.y * _Speed;
                
                // Animated sine waves similar to k2 and k3 in original
                float k1 = 1.9 * _Scale;
                float k2 = (sin(p.x * k1) + sin(p.z * k1)) * 0.8;
                float k3 = (sin(p.y * k1) + sin(p.z * k1)) * 0.8;
                
                // Modulated patterns similar to s1 and s2
                float2 mod1 = fmod(p.xy + float2(sin((p.z + p.x + time) * 2.0) * 0.3, 
                                                cos((p.z + p.x + time) * 1.0) * 0.5), 2.0) - 1.0;
                float2 mod2 = fmod(0.5 + p.yz + float2(sin((p.z + p.x + time) * 2.0) * 0.3, 
                                                      cos((p.z + p.x + time) * 1.0) * 0.3), 2.0) - 1.0;
                
                float s1 = length(mod1) - 0.2;
                float s2 = length(mod2) - 0.2;
                
                return min(s1, s2) + k2 + k3;
            }
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                float time = _Time.y * _Speed;
                
                // Create UV coordinates similar to the original
                float2 uv = (i.uv * 2.0 - 1.0);
                uv.x *= _ScreenParams.x / _ScreenParams.y;
                
                // Create a direction vector
                float3 dir = normalize(float3(uv, 1.0));
                
                // Apply rotations similar to the original
                dir.xz = rotate(dir.xz, time * 0.23);
                dir = dir.yzx;
                dir.xz = rotate(dir.xz, time * 0.2);
                dir = dir.yzx;
                
                // Create position with time offset
                float3 pos = float3(0, 0, time * 0.5);
                
                // Sample multiple layers for depth
                float layer1 = pattern(pos + dir * 2.0);
                float layer2 = pattern(pos + dir * 4.0);
                float layer3 = pattern(pos + dir * 6.0);
                
                // Combine layers
                float combined = (layer1 + layer2 * 0.7 + layer3 * 0.5) / 2.2;
                
                // Create color based on direction and pattern
                float3 col = abs(dir) * (1.0 + combined * 0.5);
                
                // Apply distance-based coloring similar to original
                float dist = length(uv) + combined * 0.3;
                col *= (1.0 - dist * 0.3);
                
                // Add some animated highlights
                float highlight = sin(time * 2.0 + length(uv) * 5.0) * 0.1 + 0.9;
                col *= highlight;
                
                // Apply intensity and tint
                col *= _Intensity;
                col = sqrt(col); // Similar to the original's sqrt
                
                return fixed4(col * _ColorTint.rgb, 1.0);
            }
            ENDCG
        }
    }
    
    Fallback "Diffuse"
}