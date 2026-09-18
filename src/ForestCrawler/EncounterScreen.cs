using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForestCrawler;

// Owned only by the selected client's active encounter. Never alters game graphics preferences.
internal sealed class EncounterScreen : IDisposable
{
    private static readonly System.Reflection.FieldInfo GuiScale = HarmonyLib.AccessTools.Field(typeof(GuiScaler), "m_largeGuiScale");
    private readonly Plugin plugin;
    private readonly bool tease;
    private readonly float started = Time.realtimeSinceStartup;
    private readonly Dictionary<char, Texture2D> glyphs = new();
    private readonly HashSet<Cue> seen = new();
    private CrawlerScreenPass? pass;
    private Camera? camera;
    private Phase phase;
    private readonly Material overlay;
    private readonly Font font;
    private Cue? current, queued;
    private float cueAt, nextPulse, pulseAt = -100, pulseLength;
    private bool disposed;
    internal string Status => $"screen={(!disposed && pass ? "active" : "off")}; rune={current?.ToString() ?? "none"}";
    internal EncounterScreen(Plugin plugin, bool tease)
    {
        this.plugin = plugin; this.tease = tease;
        overlay=new Material(plugin.Assets.RuneShader) { hideFlags=HideFlags.HideAndDontSave };
        font=Font.CreateDynamicFontFromOSFont("Arial",32); nextPulse = started + 4;
        if (plugin.Settings.RuneText) BuildGlyphs(plugin.Assets.RuneAtlas);
    }
    private void BuildGlyphs(Texture2D atlas)
    {
        // Pixel rectangles exclude the supplied Latin labels. Source stays untouched.
        for (int i = 0; i < 26; i++)
        {
            int col = i % 12, row = i / 12;
            int x = row < 2 ? 12 + (int)(col * 145.5f) : col == 0 ? 20 : 238;
            int top = row == 0 ? 160 : row == 1 ? 424 : 676;
            int width = row < 2 ? 139 : col == 0 ? 135 : 116;
            int height = row == 0 ? 184 : row == 1 ? 175 : 168;
            var pixels = atlas.GetPixels(x, atlas.height-top-height, width, height);
            int left=width, right=0, bottom=height, upper=0;
            for(int y=0;y<height;y++) for(int xx=0;xx<width;xx++)
                if(pixels[y*width+xx].r > .08f && pixels[y*width+xx].a > .01f)
                { left=Math.Min(left,xx); right=Math.Max(right,xx); bottom=Math.Min(bottom,y); upper=Math.Max(upper,y); }
            if(left>right) throw new InvalidOperationException("Empty rune " + (char)('A'+i));
            var texture = new Texture2D(right-left+1,upper-bottom+1,TextureFormat.RGBA32,false);
            texture.name = "CrawlerRune_"+(char)('A'+i); texture.wrapMode=TextureWrapMode.Clamp;
            for(int y=bottom;y<=upper;y++) for(int xx=left;xx<=right;xx++)
            {
                var source=pixels[y*width+xx]; float alpha=Mathf.Clamp01((source.r-.035f)*2.8f)*source.a;
                texture.SetPixel(xx-left,y-bottom,new Color(.78f,.24f,.20f,alpha));
            }
            texture.Apply(false,true); glyphs.Add((char)('A'+i),texture);
        }
    }
    internal void SetPhase(Phase value)
    {
        phase=value;
        if(value==Phase.Caught) { current=queued=null; Detach(); }
        else if(value==Phase.Stare || value==Phase.Watching || value==Phase.Reveal)
        { if(current==Cue.Warning || current==Cue.Escape) current=null; queued=null; }
    }
    internal void Show(Cue cue)
    {
        if(disposed || phase==Phase.Caught || !Enum.IsDefined(typeof(Cue),cue)) return;
        if((cue==Cue.Warning || cue==Cue.Escape) && phase!=Phase.Lure) return;
        if(cue==Cue.Start && phase!=Phase.Lure && phase!=Phase.Tease) return;
        if((cue==Cue.Run || cue==Cue.Close) && phase!=Phase.Charge && phase!=Phase.GrabWindup && phase!=Phase.Pulling) return;
        if(!seen.Add(cue)) return;
        if(cue==Cue.Run) { queued=null; current=cue; cueAt=Time.realtimeSinceStartup; }
        else if(cue==Cue.Close && current==Cue.Run) queued=cue;
        else { current=cue; cueAt=Time.realtimeSinceStartup; }
    }
    internal void Update()
    {
        if(disposed || phase==Phase.Caught) return;
        float now=Time.realtimeSinceStartup;
        if(current.HasValue && now-cueAt>=5.25f)
        { current=queued; queued=null; cueAt=now; }
        if(!plugin.Settings.ScreenEffects && !plugin.Settings.RuneText) { Detach(); return; }
        var active=Utils.GetMainCamera();
        if(active!=camera || !pass)
        {
            Detach(); camera=active;
            if(camera) { pass=camera.gameObject.AddComponent<CrawlerScreenPass>(); pass.Initialize(plugin.Assets.ScreenShader, this); }
        }
        if(!pass) return;
        if(now>=nextPulse) { pulseAt=now; pulseLength=UnityEngine.Random.Range(.6f,1.2f); nextPulse=now+UnityEngine.Random.Range(4f,8f); }
        float elapsed=now-started;
        float intro=Mathf.SmoothStep(0,1,elapsed<.4f ? elapsed/.4f : elapsed<1.1f ? 1 : (1.5f-elapsed)/.4f);
        float pulse=now-pulseAt<pulseLength ? Mathf.Sin(Mathf.PI*(now-pulseAt)/pulseLength) : 0;
        float strength=plugin.Settings.ScreenEffects ? plugin.Settings.ScreenStrength*(tease?.5f:1) : 0;
        pass!.Set(strength, intro, pulse*(phase==Phase.Charge || phase==Phase.Pulling ? 1.25f:1));
    }
    internal static string Text(Cue cue) => cue switch
    { Cue.Start=>"HE SEES YOU", Cue.Warning=>"HE IS COMING", Cue.Run=>"RUN", Cue.Close=>"HE IS CLOSE", _=>"YOU CANNOT ESCAPE" };
    internal void Draw(RenderTexture destination, int screenWidth, int screenHeight)
    {
        if(disposed || !plugin.Settings.RuneText || !current.HasValue) return;
        float age=Time.realtimeSinceStartup-cueAt;
        float alpha=Mathf.Clamp01((5.25f-age)/.75f);
        string text=Text(current.Value);
        float hudScale=Mathf.Clamp((float)GuiScale.GetValue(null),.5f,2f);
        float height=screenHeight*.065f*hudScale, spacing=height*.12f, width=0;
        foreach(char c in text) width+=c==' '?height*.4f:height*glyphs[c].width/glyphs[c].height+spacing;
        float scale=Mathf.Min(1,screenWidth*.82f/width); height*=scale; spacing*=scale; width*=scale;
        float left=(screenWidth-width)*.5f, top=screenHeight*.65f-height*.5f;
        var previous=RenderTexture.active;
        RenderTexture.active=destination;
        GL.PushMatrix(); GL.LoadPixelMatrix(0,screenWidth,screenHeight,0);
        try
        {
            float x=left, edge=left+width*Mathf.Clamp01(age);
            foreach(char c in text)
            {
                if(c==' ') { x+=height*.4f; continue; }
                var tex=glyphs[c]; float w=height*tex.width/tex.height;
                float visible=Mathf.Clamp01((edge-x)/w);
                if(visible>0)
                {
                    float glyphAlpha=alpha*Mathf.SmoothStep(0,1,visible);
                    Quad(tex,new Rect(x+1,top+1,w*visible,height),new Rect(0,0,visible,1),new Color(0,0,0,glyphAlpha));
                    Quad(tex,new Rect(x,top,w*visible,height),new Rect(0,0,visible,1),new Color(1,1,1,glyphAlpha));
                }
                x+=w+spacing;
            }
            if(age>=2.5f)
            {
                int size=Mathf.RoundToInt(screenHeight*.024f*hudScale);
                font.RequestCharactersInTexture(text,size,FontStyle.Bold);
                float textWidth=0;
                foreach(char c in text) if(font.GetCharacterInfo(c,out var info,size,FontStyle.Bold)) textWidth+=info.advance;
                float textX=(screenWidth-textWidth)*.5f, baseline=top+height+8+size;
                float opacity=alpha*Mathf.Clamp01((age-2.5f)/.35f);
                foreach(char c in text)
                {
                    if(!font.GetCharacterInfo(c,out var info,size,FontStyle.Bold)) continue;
                    var rect=new Rect(textX+info.minX,baseline-info.maxY,info.maxX-info.minX,info.maxY-info.minY);
                    FontQuad(rect,info,new Color(0,0,0,opacity),1);
                    FontQuad(rect,info,new Color(.86f,.72f,.67f,opacity),0);
                    textX+=info.advance;
                }
            }
        }
        finally { GL.PopMatrix(); RenderTexture.active=previous; }
    }
    private void Quad(Texture texture,Rect rect,Rect uv,Color color)
    {
        overlay.SetFloat("_AlphaOnly",0); overlay.SetTexture("_MainTex",texture); overlay.SetPass(0);
        GL.Begin(GL.QUADS); GL.Color(color);
        Vertex(rect.xMin,rect.yMin,new Vector2(uv.xMin,uv.yMax)); Vertex(rect.xMax,rect.yMin,new Vector2(uv.xMax,uv.yMax));
        Vertex(rect.xMax,rect.yMax,new Vector2(uv.xMax,uv.yMin)); Vertex(rect.xMin,rect.yMax,new Vector2(uv.xMin,uv.yMin)); GL.End();
    }
    private void FontQuad(Rect rect,CharacterInfo info,Color color,float offset)
    {
        overlay.SetFloat("_AlphaOnly",1); overlay.SetTexture("_MainTex",font.material.mainTexture); overlay.SetPass(0);
        GL.Begin(GL.QUADS); GL.Color(color);
        Vertex(rect.xMin+offset,rect.yMin+offset,info.uvTopLeft); Vertex(rect.xMax+offset,rect.yMin+offset,info.uvTopRight);
        Vertex(rect.xMax+offset,rect.yMax+offset,info.uvBottomRight); Vertex(rect.xMin+offset,rect.yMax+offset,info.uvBottomLeft); GL.End();
    }
    private static void Vertex(float x,float y,Vector2 uv) { GL.TexCoord2(uv.x,uv.y); GL.Vertex3(x,y,0); }
    private void Detach()
    {
        if(pass) { pass!.Release(); UnityEngine.Object.Destroy(pass); }
        pass=null; camera=null;
    }
    public void Dispose()
    {
        if(disposed) return; disposed=true; Detach();
        foreach(var glyph in glyphs.Values) UnityEngine.Object.Destroy(glyph);
        glyphs.Clear(); UnityEngine.Object.Destroy(overlay); UnityEngine.Object.Destroy(font); current=queued=null;
    }
}

// Added after the game's own image effects; HUD and the capture overlay render afterwards.
internal sealed class CrawlerScreenPass : MonoBehaviour
{
    private Material? material;
    private EncounterScreen? owner;
    internal int Frames { get; private set; }
    internal void Initialize(Shader shader, EncounterScreen screen) { owner=screen; material=new Material(shader) { hideFlags=HideFlags.HideAndDontSave }; }
    internal void Set(float strength,float negative,float noise)
    { if(material) { material!.SetFloat("_Strength",strength); material.SetFloat("_Negative",negative); material.SetFloat("_Noise",noise); } }
    private void OnRenderImage(RenderTexture source,RenderTexture destination)
    { if(material) { Graphics.Blit(source,destination,material); Frames++; } else Graphics.Blit(source,destination); owner?.Draw(destination,source.width,source.height); }
    internal void Release() { enabled=false; if(material) UnityEngine.Object.Destroy(material); material=null; owner=null; }
    private void OnDestroy() => Release();
}
