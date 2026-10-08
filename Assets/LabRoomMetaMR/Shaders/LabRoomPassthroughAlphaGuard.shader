// Full-screen alpha guard for window-only passthrough (Built-in Render Pipeline).
//
// With an underlay passthrough layer the compositor blends the real world in wherever the eye buffer alpha is < 1.
// Transparent materials (the faded blockout, glass, foliage...) lower the destination alpha when they blend, which
// would leak passthrough all over the room. This pass runs after every transparent object and writes alpha = 1
// everywhere except the pixels marked in the stencil buffer by "LabRoom/MR/Passthrough Window".
// The mesh position is ignored: the vertex shader expands Unity's built-in Quad to cover the whole viewport.
Shader "LabRoom/MR/Passthrough Alpha Guard"
{
    Properties
    {
        [IntRange] _StencilBit ("Stencil Bit (must match the Passthrough Window material)", Range(1, 255)) = 64
    }

    SubShader
    {
        Tags { "Queue" = "Overlay+500" "RenderType" = "Overlay" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "AlphaGuard"
            Cull Off
            ZWrite Off
            ZTest Always
            ColorMask A

            Stencil
            {
                Ref [_StencilBit]
                ReadMask [_StencilBit]
                Comp NotEqual
                Pass Keep
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // Built-in Quad spans -0.5..0.5; scale it to the full clip-space square.
                o.pos = float4(v.vertex.xy * 2.0, 0.5, 1.0);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return fixed4(0, 0, 0, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
