// Draws a creature's own mesh pushed out along its normals as a see-through sheen: the Shielded modifier's shell.
// SLSShield gives each shell renderer _Extrude and _Strength through a MaterialPropertyBlock.
Shader "StarLevelSystem/ShieldShell"
{
    Properties
    {
        _Color ("Tint", Color) = (0.62, 0.45, 1.0, 0.075)
        [HDR] _RimColor ("Rim", Color) = (0.45, 0.29, 0.9, 0.4)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _Gloss ("Highlight Sharpness", Range(4, 256)) = 48
        _SpecStrength ("Highlight Strength", Range(0, 2)) = 0.4
        _ShimmerScale ("Shimmer Scale", Float) = 2.5
        _ShimmerSpeed ("Shimmer Speed", Float) = 0.6
        _ShimmerAmount ("Shimmer Amount", Range(0, 1)) = 0.35
        _Extrude ("Extrude (metres)", Float) = 0.03
        _Strength ("Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        // Batching would bake the meshes into world space and lose the object origin the shimmer is anchored to.
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }

        Pass
        {
            // ForwardBase is what fills _WorldSpaceLightPos0 and _LightColor0 with the main directional light.
            Tags { "LightMode"="ForwardBase" }
            // Premultiplied: one pass carries both the faint tint (alpha) and the rim and highlight (added light).
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            // Wings, manes and capes are single planes, so the shell has to show from both sides.
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            fixed4 _Color;
            half4 _RimColor;
            half _RimPower;
            half _Gloss;
            half _SpecStrength;
            float _ShimmerScale;
            float _ShimmerSpeed;
            half _ShimmerAmount;
            float _Extrude;
            half _Strength;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 fromOrigin : TEXCOORD2;
                UNITY_FOG_COORDS(3)
            };

            v2f vert (appdata v)
            {
                v2f o;
                // Pushed out in world space: creature rigs scale their meshes by up to 100, so an object-space
                // distance would mean something different on every creature.
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 worldNormal = normalize(UnityObjectToWorldNormal(v.normal));
                worldPos += worldNormal * _Extrude;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.worldPos = worldPos;
                o.worldNormal = worldNormal;
                o.fromOrigin = worldPos - unity_ObjectToWorld._m03_m13_m23;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            half4 frag (v2f i, float facing : VFACE) : SV_Target
            {
                float3 n = normalize(i.worldNormal) * (facing > 0 ? 1.0 : -1.0);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                half rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
                float3 h = normalize(normalize(_WorldSpaceLightPos0.xyz) + v);
                half spec = pow(saturate(dot(n, h)), _Gloss) * _SpecStrength;

                float t = _Time.y * _ShimmerSpeed;
                float3 p = i.fromOrigin * _ShimmerScale;
                half shimmer = 1.0 + _ShimmerAmount * sin(p.x * 1.7 + t) * sin(p.y * 1.3 - t * 1.4) * sin(p.z * 1.9 + t * 0.8);

                half alpha = saturate(_Color.a * shimmer + rim * _RimColor.a);
                half3 tint = _Color.rgb;
                half3 glow = (_RimColor.rgb * rim + _LightColor0.rgb * spec) * shimmer;
                // Each fog macro declares the same local, so each needs a scope of its own.
                { UNITY_APPLY_FOG(i.fogCoord, tint); }
                { UNITY_APPLY_FOG_COLOR(i.fogCoord, glow, half3(0, 0, 0)); }
                return half4(tint * alpha + glow, alpha) * _Strength;
            }
            ENDCG
        }
    }
}
