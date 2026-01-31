Shader "Custom/MaskedFace"
{
    Properties
    {
        _MainTex ("Webcam (RGB)", 2D) = "white" {}
        _MaskTex ("Mask (R=Alpha)", 2D) = "white" {}
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"}
        LOD 100
        
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

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
                float2 uv : TEXCOORD0; // UV for Webcam (Modified by Tiling/Offset)
                float2 uvMask : TEXCOORD1; // UV for Mask (Static 0-1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _MaskTex;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                
                // Apply Tiling/Offset to MainTex (Controlled by FaceTextureMapper)
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                
                // Keep Mask UVs static (0-1) covering the full mesh
                o.uvMask = v.uv; 
                
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample Webcam (Cropped via UVs)
                fixed4 col = tex2D(_MainTex, i.uv);
                
                // Sample Mask
                // We use the Red channel of the mask texture as Alpha
                fixed4 mask = tex2D(_MaskTex, i.uvMask);
                
                // Combine
                col.a = mask.r; // Assuming mask is white-on-black or grayscale
                
                return col;
            }
            ENDCG
        }
    }
}
