Shader "Hidden/ForestCrawler/Screen"
{
 Properties { _MainTex ("Source", 2D) = "white" {} }
 SubShader { Cull Off ZWrite Off ZTest Always
 Pass { CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 sampler2D _MainTex;
 float4 _MainTex_TexelSize;
 float _Strength, _Negative, _Noise;
 float hash(float2 p) { return frac(sin(dot(p,float2(12.9898,78.233)))*43758.5453); }
 fixed4 frag(v2f_img i) : SV_Target
 {
     float2 uv=i.uv;
     float time=floor(_Time.y*20);
     float noise=_Noise*_Strength;
     uv.x+=(hash(float2(floor(uv.y*80),time))-.5)*.002*noise;
     float4 source=tex2D(_MainTex,uv);
     float3 color=source.rgb;
     // Compress the negative's white point and expand shadow detail; midnight stays navigable.
     float3 inverted=1-saturate(color/(max(color,0)+.02));
     color=lerp(color,.45*inverted*inverted,_Negative*_Strength);
     float2 centered=i.uv-.5;
     color*=1-.15*_Strength-.16*_Strength*dot(centered,centered);
     float grain=hash(floor(i.uv*abs(_MainTex_TexelSize.zw)*.5)+time)-.5;
     color*=1+grain*.5*noise;
     color+=grain*.006*noise;
     color*=1-(.5+.5*sin(i.uv.y*abs(_MainTex_TexelSize.w)*3.14159))*.06*noise;
     return float4(max(0,color),source.a);
 }
 ENDCG }
 }
}
