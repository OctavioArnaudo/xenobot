// Noche azul cerrada con estrellas brillantes, 100% procedural (sin texturas). Se dibuja sobre cualquier skybox.
Shader "Sky/NightOverlay"
{
    Properties { _Blend ("Blend", Range(0,1)) = 0 }
    SubShader
    {
        Tags { "Queue"="Geometry+501" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Front ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half _Blend;
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(float4 v : POSITION) { v2f o; o.pos = UnityObjectToClipPos(v); o.dir = v.xyz; return o; }

            float3 H3(float3 p)
            {
                p = frac(p * float3(.1031, .1030, .0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                half3 c = lerp(half3(.02, .045, .12), half3(.003, .008, .03), saturate(d.y * 2.5));   // horizonte -> cenit
                float3 p = d * 220, id = floor(p), h = H3(id);                                        // 220 = celdas de estrellas
                float r = clamp(1.5 * length(fwidth(p)), .15, .35);                                   // radio >= ~1.5 px (sin parpadeo)
                float s = saturate(1 - distance(p, id + .35 + .3 * H3(id + 7.3)) / r);
                float b = (.5 + 5.5 * h.y * h.y * h.y) * (.8 + .2 * sin(_Time.y * (1 + 3 * h.z) + 100 * h.x));   // HDR + titileo
                c += step(h.x, .03) * s * s * b * lerp(half3(.7, .8, 1), half3(1, .9, .7), h.z);
                return half4(c, _Blend);
            }
            ENDCG
        }
    }
}
