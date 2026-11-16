Shader "TimeToExplore/BodyLit"
{
    Properties
    {
        _Color ("Colour", Color) = (1,1,1,1)
        _Ambient ("Ambient", Range(0,1)) = 0.055
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 normal : TEXCOORD1;
            };

            fixed4 _Color;
            float _Ambient;
            float4 _SunPosition; // set globally, in render-world units

            v2f vert (appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.worldPosition = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 toSun = normalize(_SunPosition.xyz - i.worldPosition);
                float ndl = saturate(dot(n, toSun));
                float3 colour = _Color.rgb * (_Ambient + (1.0 - _Ambient) * ndl);
                return fixed4(colour, 1.0);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Color"
}
