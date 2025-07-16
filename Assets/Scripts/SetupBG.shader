Shader "Custom/WaterSurface"
{
    Properties
    {
        [Header(Color Settings)]
        _BackgroundColor ("Background Color", Color) = (0.3, 0.2, 0.8, 1.0)
        _GradientColor ("Gradient Color", Color) = (0.1, 0.4, 1.0, 1.0)
        _GradientStrength ("Gradient Strength", Range(0.0, 2.0)) = 0.8
        _GradientDirection ("Gradient Direction", Range(0.0, 1.0)) = 0.0
        
        [Header(Wave Settings)]
        _WaveSpeed ("Wave Speed", Range(0.1, 2.0)) = 0.3
        _WaveHeight ("Wave Height", Range(0.0, 0.5)) = 0.1
        _WaveFrequency ("Wave Frequency", Range(0.5, 5.0)) = 2.0
        _WavePosition ("Wave Position Y", Range(0.0, 1.0)) = 0.6
        _WaveIntensity ("Wave Mask Intensity", Range(0.0, 0.5)) = 0.1
        
        [Header(Bubble Settings)]
        _BubbleCount ("Bubble Count", Range(5, 50)) = 20
        _BubbleSize ("Bubble Size", Range(0.01, 0.1)) = 0.04
        _BubbleSpeed ("Bubble Speed", Range(0.01, 0.2)) = 0.06
        _BubbleIntensity ("Bubble Brightness", Range(0.0, 0.5)) = 0.1
        _BubbleSpread ("Bubble Spread", Range(0.5, 3.0)) = 1.75
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType"="Opaque" 
            "Queue"="Geometry"
        }
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
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
            };
            
            float3 _BackgroundColor;
            float3 _GradientColor;
            float _GradientStrength;
            float _GradientDirection;
            float _WaveSpeed;
            float _WaveHeight;
            float _WaveFrequency;
            float _WavePosition;
            float _WaveIntensity;
            int _BubbleCount;
            float _BubbleSize;
            float _BubbleSpeed;
            float _BubbleIntensity;
            float _BubbleSpread;
            
            float get_particle_x(int i) 
            {
                return (sin(float(i)) * 0.5) * (sin(float(i) * 6.0) + 0.5) * _BubbleSpread + 0.5;
            }
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                float2 UV = i.uv;
                
                // Calculate aspect ratio and correct UV coordinates
                float aspectRatio = _ScreenParams.x / _ScreenParams.y;
                UV.x *= aspectRatio;
                UV.x = (UV.x - aspectRatio * 0.5) + 0.5; // Center the corrected UV
                
                // Create gradient based on direction
                float gradientFactor = lerp(UV.y, UV.x, _GradientDirection);
                float3 col = lerp(_BackgroundColor, _GradientColor, gradientFactor * _GradientStrength);
                
                // Wave animation, layered sin waves
                float sine = sin(UV.x * _WaveFrequency + (_Time.y * _WaveSpeed)) * _WaveHeight + _WavePosition;
                float sine_2 = abs(sin(UV.x * (_WaveFrequency * 2.5) + (_Time.y * _WaveSpeed * 2.67)) * (_WaveHeight * 0.5));
                float sine_3 = abs(sin(UV.x * (_WaveFrequency * 6.0) + (_Time.y * _WaveSpeed * 4.0)) * (_WaveHeight * 0.2));
                float mask = 1.0 - step(UV.y, sine + sine_2 + sine_3);
                col += mask * _WaveIntensity;
                
                // BUBBLES!
                for (int j = 1; j < _BubbleCount; j++) 
                {
                    float particle_x = get_particle_x(j);
                    float time_offset = get_particle_x(j + 2) * 40.0;
                    float speed_offset = get_particle_x(j + 3) * 0.75;
                    float scale_factor = get_particle_x(j + 5) * 0.01;
                    
                    // Generate a particle position that animates upward based on values generated above
                    float2 particle = float2(particle_x, (frac((_Time.y + time_offset) * (-_BubbleSpeed * speed_offset)) * 2.0) - 0.5);
                    
                    // Generate circles based on length from point in UV
                    float circle = step(length(UV - particle), _BubbleSize - scale_factor);
                    float inner_circle = step(length(UV - particle), (_BubbleSize - scale_factor) - 0.005);
                    
                    // Add lighter bubble
                    col += (circle - (inner_circle * 0.7)) * _BubbleIntensity;
                }
                
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}