Shader "Hidden/ForestCrawler/Runes"
{
 Properties { _MainTex ("Glyph", 2D) = "white" {} }
 SubShader { Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
 Pass { CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 sampler2D _MainTex; float _AlphaOnly;
 struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
 struct Output { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
 Output vert(Input i) { Output o; o.pos=UnityObjectToClipPos(i.vertex); o.uv=i.uv; o.color=i.color; return o; }
 fixed4 frag(Output i):SV_Target { float4 sample=tex2D(_MainTex,i.uv); sample.rgb=lerp(sample.rgb,float3(1,1,1),_AlphaOnly); return sample*i.color; }
 ENDCG }
 }
}
