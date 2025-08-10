Shader "Unlit/ProceduralCheckerboardUI"
{
    Properties
    {
        _ColorA("Color A", Color) = (0,0,0,1)
        _ColorB("Color B", Color) = (1,1,1,1)
        _TileSize("Tile Size (pixels)", Float) = 32.0
        _Rotation("Rotation (degrees)", Range(0,360)) = 0
        _Offset("Offset", Vector) = (0,0,0,0)       // manual offset in UV-space
        _ScrollSpeed("Scroll Speed (UV/sec)", Vector) = (0.0, 0.0, 0.0, 0.0)
        _Alpha("Alpha", Range(0,1)) = 1.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        LOD 100

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            sampler2D _MainTex; // not used, but keeps UI material happy
            float4 _ColorA;
            float4 _ColorB;
            float _TileSize;
            float _Rotation;
            float4 _Offset;
            float4 _ScrollSpeed;
            float _Alpha;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            // rotate a point p (relative to origin) by angle radians
            float2 rotate2d(float2 p, float s, float c)
            {
                return float2(p.x * c - p.y * s, p.x * s + p.y * c);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // time-based offset
                float t = _Time.y;
                float2 scroll = _Offset.xy + _ScrollSpeed.xy * t;

                // get uv centered at (0.5,0.5)
                float2 uv = i.uv - 0.5;

                // aspect correction so tiles remain square visually
                float aspect = _ScreenParams.x / _ScreenParams.y;
                // multiply x by aspect so rotation and scale look correct on any canvas size
                uv.x *= aspect;

                // rotation
                float rad = radians(_Rotation);
                float s = sin(rad), c = cos(rad);
                uv = rotate2d(uv, s, c);

                // un-center, apply scroll in UV space (account for aspect on x)
                uv += 0.5 + float2(scroll.x * aspect, scroll.y);

                // compute scale from tile size in pixels -> convert to UV units
                // TileSize = pixels per checker tile (approx). Convert pixels -> UV using screen params:
                // If TileSize is small, many tiles. If large, fewer tiles.
                float2 tilesPerUV = float2(_ScreenParams.x / max(_TileSize, 0.0001), _ScreenParams.y / max(_TileSize, 0.0001));
                // apply aspect-aware scaling to uv so we can floor properly.
                float2 p = uv * tilesPerUV;

                // checker: floor(x) + floor(y) mod 2
                float fx = floor(p.x);
                float fy = floor(p.y);
                float sum = fx + fy;
                // use frac to get 0 or 1
                float checker = fmod(sum, 2.0);

                // optionally smooth edges slightly (antialias) using distance to grid lines
                // compute fractional part
                float2 f = frac(p);
                // distance to nearest grid line (minimum of f and 1-f) -> control width for soft edges
                float edge = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y));
                // smooth threshold (in UV of tile). Very small to keep crisp.
                float aa = 0.02; // small antialiasing
                float smoothMask = smoothstep(0.0, aa, edge);

                // blend between checker and its inverse on edges for subtle antialias (optional)
                // but we want crisp, so keep strong.
                float value = checker;

                // pick colors
                float4 color = lerp(_ColorB, _ColorA, value);

                color.a *= _Alpha;

                return color;
            }
            ENDCG
        }
    }
}
