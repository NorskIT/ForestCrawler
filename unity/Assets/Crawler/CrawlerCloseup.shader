Shader "ForestCrawler/Closeup"
{
    Properties { _MainTex ("Source texture", 2D) = "white" {} _CrawlerOriginY ("Origin", Float) = 0 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            ZWrite On Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float _CrawlerOriginY;
            struct Input { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
            struct Output { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float3 normal:TEXCOORD1; float height:TEXCOORD2; float4 screen:TEXCOORD3; };
            Output vert(Input v)
            {
                Output o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv;
                o.normal=UnityObjectToWorldNormal(v.normal); o.height=mul(unity_ObjectToWorld,v.vertex).y-_CrawlerOriginY;
                o.screen=ComputeScreenPos(o.vertex); return o;
            }
            fixed4 frag(Output i):SV_Target
            {
                float2 pixel=i.screen.xy/i.screen.w*_ScreenParams.xy;
                float grain=frac(sin(dot(floor(pixel),float2(12.9898,78.233))+floor(_Time.y*24)) * 43758.5453);
                float3 n=normalize(i.normal);
                float lighting=pow(saturate(dot(n,normalize(float3(-.2,.6,1)))),1.5);
                float face=smoothstep(2.23,2.39,i.height);
                float silhouette=lerp(.02,1,face);
                float value=tex2D(_MainTex,i.uv).r;
                float shade=saturate((value-.23)*1.6)*lighting*silhouette;
                float shoulder=smoothstep(1.95,2.1,i.height)*(1-smoothstep(2.12,2.24,i.height));
                shade=max(shade,value*lighting*shoulder*.23);
                float2 shift=ddx(i.uv)*2;
                float3 channels=float3(tex2D(_MainTex,i.uv+shift).r,value,tex2D(_MainTex,i.uv-shift).r);
                return float4(shade*1.8*lerp(.48,1.1,grain)*lerp(1,channels,.3),1);
            }
            ENDCG
        }
    }
}
