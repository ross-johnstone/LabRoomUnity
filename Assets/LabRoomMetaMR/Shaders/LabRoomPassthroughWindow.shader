// Passthrough portal for the lab windows (Built-in Render Pipeline, Meta Quest underlay passthrough).
//
// Any mesh rendered with this shader becomes a window onto the real world:
//  * it is drawn right after the opaque room and depth-tested against it, so virtual geometry in front of the
//    window (mullions, desks, avatar hands...) still occludes it;
//  * where it is visible it clears colour and alpha to 0, which lets the underlay OVRPassthroughLayer show through
//    (same blend as Meta's SelectivePassthrough shader: Blend Zero SrcAlpha);
//  * it marks a stencil bit so the full-screen "Passthrough Alpha Guard" pass leaves these pixels alone while it
//    forces alpha back to 1 everywhere else (so transparent materials can never leak passthrough).
// The red channel of _MainTex is a mask (white = full passthrough), so window shapes can be painted or feathered.
Shader "LabRoom/MR/Passthrough Window"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Mask (R: white = passthrough)", 2D) = "white" {}
        _Strength ("Passthrough Strength", Range(0, 1)) = 1
        _Inflation ("Inflation (m, along normals)", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite (hide virtual content behind the window)", Float) = 1
        [IntRange] _StencilBit ("Stencil Bit (must match the Alpha Guard)", Range(1, 255)) = 64
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-1" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "PassthroughWindow"
            Cull Off
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            BlendOp Add
            Blend Zero SrcAlpha

            Stencil
            {
                Ref [_StencilBit]
                WriteMask [_StencilBit]
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Strength;
            float _Inflation;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex.xyz + v.normal * _Inflation);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float mask = tex2D(_MainTex, i.uv).r * _Strength;
                clip(mask - 0.002); // only mark (stencil) pixels that actually show passthrough
                return fixed4(0, 0, 0, 1 - mask);
            }
            ENDCG
        }
    }
    Fallback Off
}
