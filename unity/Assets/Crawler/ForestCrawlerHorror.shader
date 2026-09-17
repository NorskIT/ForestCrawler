Shader "ForestCrawler/Horror"
{
    Properties
    {
        _MainTex ("Source texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _HorrorStrength ("Horror strength", Range(0,1)) = 0.9
        _ChromaticPixels ("Chromatic edge pixels", Range(0,5)) = 1.8
        _GrainPixels ("Grain cell pixels", Range(1,4)) = 1.2
        _CrawlerOriginY ("Creature ground height", Float) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.05
        _Metallic ("Metallic", Range(0,1)) = 0
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex;
    float4 _Color;
    float _HorrorStrength, _ChromaticPixels, _GrainPixels, _CrawlerOriginY, _Glossiness, _Metallic;
    float Grain(float2 pixels)
    {
        float2 cell=floor(pixels/max(1,_GrainPixels));
        // Grain evolves locally at six steps/second, without full-screen flashes.
        return frac(sin(dot(cell,float2(12.9898,78.233))+floor(_Time.y*6)*.31)*43758.5453);
    }
    float HeightMask(float3 world)
    {
        return saturate((world.y-_CrawlerOriginY)/2.45);
    }
    struct EdgeInput { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
    struct EdgeOutput { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 world:TEXCOORD1; float3 normal:TEXCOORD2; float4 screen:TEXCOORD3; UNITY_FOG_COORDS(4) };
    EdgeOutput EdgeVertex(EdgeInput v, float offset)
    {
        EdgeOutput o;
        o.pos=UnityObjectToClipPos(v.vertex);
        o.pos.xy+=float2(offset,offset*.18)*_ChromaticPixels*_HorrorStrength*2/_ScreenParams.xy*o.pos.w;
        o.world=mul(unity_ObjectToWorld,v.vertex).xyz; o.normal=UnityObjectToWorldNormal(v.normal);
        o.uv=v.uv; o.screen=ComputeScreenPos(o.pos); UNITY_TRANSFER_FOG(o,o.pos);
        return o;
    }
    EdgeOutput RedVertex(EdgeInput v) { return EdgeVertex(v,1); }
    EdgeOutput GreenVertex(EdgeInput v) { return EdgeVertex(v,0); }
    EdgeOutput BlueVertex(EdgeInput v) { return EdgeVertex(v,-1); }
    fixed4 EdgeFragment(EdgeOutput i):SV_Target
    {
        float h=HeightMask(i.world);
        float noise=Grain(i.screen.xy/i.screen.w*_ScreenParams.xy);
        float rim=pow(1-abs(dot(normalize(i.normal),normalize(_WorldSpaceCameraPos-i.world))),2);
        float luminance=dot(tex2D(_MainTex,i.uv).rgb,float3(.2126,.7152,.0722));
        float glow=_HorrorStrength*(.035+.20*pow(h,5))*(.13+.87*rim)*luminance*lerp(.6,1,noise);
        fixed4 color=fixed4(glow,glow,glow,1);
        UNITY_APPLY_FOG_COLOR(i.fogCoord,color,fixed4(0,0,0,0));
        return color;
    }
    ENDCG
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" "IgnoreProjector"="True" }
        ZWrite On
        LOD 300
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows addshadow
        struct Input { float2 uv_MainTex; float3 worldPos; float4 screenPos; float3 viewDir; };
        void surf(Input i, inout SurfaceOutputStandard o)
        {
            float h=HeightMask(i.worldPos);
            float noise=Grain(i.screenPos.xy/i.screenPos.w*_ScreenParams.xy);
            float2 shift=ddx(i.uv_MainTex)*_ChromaticPixels*_HorrorStrength;
            fixed3 source=fixed3(tex2D(_MainTex,i.uv_MainTex+shift).r,tex2D(_MainTex,i.uv_MainTex).g,tex2D(_MainTex,i.uv_MainTex-shift).b)*_Color.rgb;
            float darkness=lerp(1,.025+.40*pow(h,4),_HorrorStrength);
            // Grain modulates reflected light; it never removes surface coverage.
            o.Albedo=source*darkness*lerp(1,.18,_HorrorStrength)*lerp(1,lerp(.6,1,noise),_HorrorStrength);
            o.Metallic=0; o.Smoothness=.05; o.Occlusion=1;
            // A restrained head-weighted emission keeps grain readable in midnight lighting.
            float2 pixel=i.screenPos.xy/i.screenPos.w*_ScreenParams.xy;
            float3 channelGrain=float3(Grain(pixel+float2(_ChromaticPixels,0)),noise,Grain(pixel-float2(_ChromaticPixels,0)));
            float rim=pow(1-abs(normalize(i.viewDir).z),2);
            o.Emission=source*_HorrorStrength*(.07*pow(h,5)+channelGrain*(.035+.24*rim)*pow(h,5));
            o.Alpha=1;
        }
        ENDCG
        Pass
        {
            Name "RedFringe"
            Tags { "LightMode"="Always" }
            ZWrite Off ZTest LEqual Blend One One ColorMask R
            CGPROGRAM
            #pragma vertex RedVertex
            #pragma fragment EdgeFragment
            #pragma multi_compile_fog
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            Name "GreenFringe"
            Tags { "LightMode"="Always" }
            ZWrite Off ZTest LEqual Blend One One ColorMask G
            CGPROGRAM
            #pragma vertex GreenVertex
            #pragma fragment EdgeFragment
            #pragma multi_compile_fog
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            Name "BlueFringe"
            Tags { "LightMode"="Always" }
            ZWrite Off ZTest LEqual Blend One One ColorMask B
            CGPROGRAM
            #pragma vertex BlueVertex
            #pragma fragment EdgeFragment
            #pragma multi_compile_fog
            #pragma target 3.0
            ENDCG
        }
    }
    Fallback "Diffuse"
}
