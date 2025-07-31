Shader "Custom/FractalRaymarching"
{
    Properties
    {
        _MaxIterations("Max Iterations", Range(10, 150)) = 77
        _StepSize("Step Size", Range(0.1, 2.0)) = 0.6
        _TimeScale("Time Scale", Range(0.0, 2.0)) = 0.1
        _TimeSpeed("Time Speed", Range(0.0, 5.0)) = 1.0
        _CylinderRadius("Cylinder Radius", Range(0.01, 0.5)) = 0.125
        _PlaneOffset("Plane Offset", Range(0.0, 0.01)) = 0.001
        _SurfaceOffset("Surface Offset", Range(0.0, 0.01)) = 0.001
        _ColorIntensity("Color Intensity", Range(0.1, 2.0)) = 0.5
        _ColorOffset1("Color Offset 1", Range(0, 10)) = 0
        _ColorOffset2("Color Offset 2", Range(0, 10)) = 4
        _ColorOffset3("Color Offset 3", Range(0, 10)) = 3
        _ColorOffset4("Color Offset 4", Range(0, 10)) = 6
        _RotationSpeed1("Rotation Speed 1", Range(0.0, 5.0)) = 1.0
        _RotationSpeed2("Rotation Speed 2", Range(0.0, 5.0)) = 1.0
        _LightingIntensity("Lighting Intensity", Range(0.1, 5.0)) = 1.0
        _ColorFalloff("Color Falloff", Range(0.1, 2.0)) = 0.5
        _ColorFalloffPower("Color Falloff Power", Range(0.5, 5.0)) = 2.0
        _ToneMappingScale("Tone Mapping Scale", Range(1000, 50000)) = 20000
        _NoiseIntensity("Noise Intensity", Range(0.0, 1.0)) = 1.0
    }

        SubShader
    {
        Tags {
            "RenderType" = "Opaque"
            "Queue" = "Background"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            ZWrite Off
            ZTest Always
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

            // Shader properties
            float _MaxIterations;
            float _StepSize;
            float _TimeScale;
            float _TimeSpeed;
            float _CylinderRadius;
            float _PlaneOffset;
            float _SurfaceOffset;
            float _ColorIntensity;
            float _ColorOffset1;
            float _ColorOffset2;
            float _ColorOffset3;
            float _ColorOffset4;
            float _RotationSpeed1;
            float _RotationSpeed2;
            float _LightingIntensity;
            float _ColorFalloff;
            float _ColorFalloffPower;
            float _ToneMappingScale;
            float _NoiseIntensity;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Helper function to create 2x2 rotation matrix
            float2x2 rot2D(float angle)
            {
                float c = cos(angle);
                float s = sin(angle);
                return float2x2(c, -s, s, c);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 C = i.uv * _ScreenParams.xy; // Convert UV to screen coordinates
                float2 r = _ScreenParams.xy; // Screen resolution

                float iteration = 0; // Loop counter
                float d; // Distance to nearest surface
                float z = _NoiseIntensity * (frac(dot(C, sin(C))) - 0.5); // Ray distance + noise
                float4 o = float4(0, 0, 0, 0); // Accumulated color/lighting
                float4 p; // Current 3D position along ray
                float4 O; // Saved position for lighting

                float time = _Time.y * _TimeSpeed;

                for (; iteration < _MaxIterations; z += _StepSize * d)
                {
                    iteration++;

                    // Convert 2D pixel to 3D ray direction
                    p = float4(z * normalize(float3(C - 0.5 * r, r.y)), _TimeScale * time);

                    // Move through 3D space over time
                    p.z += time;

                    // Save position for lighting calculations
                    O = p;

                    // Apply rotation matrices to create fractal patterns
                    // First rotation with time-based animation
                    float angle1 = 2.0 + O.z * _RotationSpeed1;
                    p.xy = mul(rot2D(angle1), p.xy);

                    // Second rotation - the "happy little accident"
                    float angle2 = O.x * _RotationSpeed2;
                    p.xy = mul(rot2D(angle2), p.xy);

                    // Calculate color based on position and space distortion
                    float4 colorOffsets = float4(_ColorOffset1, _ColorOffset2, _ColorOffset3, _ColorOffset4);
                    O = (1.0 + sin(_ColorIntensity * O.z + length(p - O) + colorOffsets)) /
                        (_ColorFalloff + _ColorFalloffPower * dot(O.xy, O.xy));

                    // Domain repetition
                    p = abs(frac(p) - 0.5);

                    // Calculate distance to nearest surface
                    // Combines cylinder with planes
                    float cylinderDist = length(p.xy) - _CylinderRadius;
                    float planeDist = min(p.x, p.y) + _PlaneOffset;
                    d = abs(min(cylinderDist, planeDist)) + _SurfaceOffset;

                    // Add lighting contribution
                    o += O * _LightingIntensity / d;
                }

                // Tone mapping
                float4 result = tanh(o / _ToneMappingScale);
                return result;
            }
            ENDCG
        }
    }
}