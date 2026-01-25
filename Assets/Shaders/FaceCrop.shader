Shader "Unlit/FaceCrop"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _FaceRect ("Face Rect (x, y, w, h)", Vector) = (0,0,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        
        Cull Off 

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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _FaceRect; // x, y, w, h in 0..1 UV space

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                // Map Quad UV (0..1) to FaceRect portion of texture
                // Quad UV (0,0) -> FaceRect.xy
                // Quad UV (1,1) -> FaceRect.xy + FaceRect.zw
                // Assuming Quad UV is v.uv
                float2 subUV = v.uv; 
                
                // If needed, flip Y? Start with standard.
                // _FaceRect x,y is usually Top-Left or Bottom-Left.
                // Unity Texture UV 0,0 is Bottom-Left.
                // MediaPipe Bbox Y might be inverted relative to Unity UV.
                
                o.uv = _FaceRect.xy + subUV * _FaceRect.zw;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }
    }
}
