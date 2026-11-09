Shader "Skybox/Twilight"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.05, 0.07, 0.22, 1)
        _Mid ("Mid", Color) = (0.42, 0.22, 0.45, 1)
        _Horizon ("Horizon", Color) = (1, 0.45, 0.2, 1)
        _Ground ("Ground", Color) = (0.08, 0.05, 0.1, 1)
        _SunColor ("Sun", Color) = (1, 0.6, 0.3, 1)
        _SunSize ("Sun Size", Range(0.0002, 0.01)) = 0.002
        _Glow ("Glow Tightness", Range(2, 64)) = 10
        [HideInInspector] _Sun ("Sun Dir", Vector) = (0, 0.1, 1, 0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half3 _Zenith, _Mid, _Horizon, _Ground, _SunColor;
            half _SunSize, _Glow;
            float4 _Sun;

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(float4 v : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v);
                o.dir = v.xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;

                half3 c = lerp(_Horizon, _Mid, smoothstep(0, 0.22, h));
                c = lerp(c, _Zenith, smoothstep(0.12, 0.85, h));
                c = lerp(c, _Ground, smoothstep(0, 0.12, -h));

                float s = saturate(dot(d, _Sun.xyz));
                float glow = pow(s, _Glow) * 0.6 + pow(s, 3) * 0.15;
                float disc = smoothstep(1 - _SunSize, 1 - _SunSize * 0.5, s) * step(-0.01, h);
                c += _SunColor * (glow * saturate(1 - h) + disc * 2);

                // dither: evita bandas en el degradado
                c += (frac(sin(dot(i.pos.xy, float2(12.9898, 78.233))) * 43758.5453) - 0.5) / 255.0;
                return half4(c, 1);
            }
            ENDCG
        }
    }
}
