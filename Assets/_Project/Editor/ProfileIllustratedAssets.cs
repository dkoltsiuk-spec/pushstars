using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    public static class ProfileIllustratedAssets
    {
        public static void ImportFlags()
        {
            AssetDatabase.Refresh();AssetDatabase.StartAssetEditing();
            try
            {
                foreach(var path in System.IO.Directory.GetFiles("Assets/_Project/Resources/CountryFlags","*.png"))
                {
                    var normalized=path.Replace('\\','/');var importer=(TextureImporter)AssetImporter.GetAtPath(normalized);
                    importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
                    importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.maxTextureSize=128;
                    importer.textureCompression=TextureImporterCompression.Uncompressed;importer.filterMode=FilterMode.Bilinear;
                    AssetDatabase.WriteImportSettingsIfDirty(normalized);AssetDatabase.ImportAsset(normalized);
                }
            }
            finally{AssetDatabase.StopAssetEditing();}
        }
        public static void Import()
        {
            AssetDatabase.Refresh();
            Slice("icons",3,2,new[]{"trophy","flame","dumbbell","shield","bronze","silver"},false);
            Slice("characters",2,2,new[]{"shadow","striker","blaze","bolt"},false);
            Slice("plates",2,2,new[]{"name","action","selected","idle"},true);
            string[] achievements={"first-set","reps-100","set-10","win-1","days-5","reps-500","set-25","wins-10","reps-1000","set-50","days-20","modes-3","boss-1","wins-50","reps-5000","wins-100"};
            Slice("achievements-bronze",4,4,achievements,false);
            Slice("achievements-silver",4,4,achievements,false);
            var background=(TextureImporter)AssetImporter.GetAtPath("Assets/_Project/UI/Sprites/ProfileBrawl/background.png");
            background.textureType=TextureImporterType.Sprite;background.spriteImportMode=SpriteImportMode.Single;
            background.mipmapEnabled=false;background.maxTextureSize=2048;background.textureCompression=TextureImporterCompression.Uncompressed;background.SaveAndReimport();
        }
        static void Slice(string file,int columns,int rows,string[] names,bool panels)
        {
            string path="Assets/_Project/UI/Sprites/ProfileBrawl/"+file+".png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Multiple;
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.isReadable=true;
            importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);var pixels=texture.GetPixels32();
            int width=texture.width/columns,height=texture.height/rows;
            var sprites=new SpriteMetaData[names.Length];
            var components=file.StartsWith("achievements-",StringComparison.Ordinal)?MedalBounds(pixels,texture.width,texture.height,names.Length):null;
            for(int i=0;i<names.Length;i++)
            {
                int left=i%columns*width,bottom=(rows-1-i/columns)*height;
                int minX=left+width,minY=bottom+height,maxX=left,maxY=bottom;
                // Exclude the neighboring trophy outline that crosses the flame cell's left gutter.
                int cropLeft = file == "icons" && i == 1 ? left + 70 : left;
                for(int y=bottom;y<bottom+height;y++)for(int x=cropLeft;x<left+width;x++)
                    if(pixels[y*texture.width+x].a>16){minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);}
                if(maxX<=minX)throw new InvalidOperationException("Empty illustrated sprite: "+names[i]);
                if(components!=null)
                {var box=components[i];minX=box.xMin;minY=box.yMin;maxX=box.xMax-1;maxY=box.yMax-1;}
                sprites[i]=new SpriteMetaData{name=names[i],rect=new Rect(minX,minY,maxX-minX+1,maxY-minY+1),pivot=new Vector2(.5f,.5f),alignment=0,border=panels?new Vector4(30,26,30,26):Vector4.zero};
                Debug.Log("[ProfileArt] "+names[i]+" "+sprites[i].rect);
            }
            importer.spritesheet=sprites;importer.isReadable=false;importer.SaveAndReimport();
        }
        // Generated sheets have clear gutters, but their row spacing is not pixel-exact.
        // Slice complete opaque islands so later rows never inherit a neighboring medal tip.
        static RectInt[] MedalBounds(Color32[] pixels,int width,int height,int count)
        {
            var visited=new bool[pixels.Length];var queue=new int[pixels.Length];var boxes=new List<RectInt>();
            for(int start=0;start<pixels.Length;start++)
            {
                if(visited[start] || pixels[start].a<=16)continue;
                int head=0,tail=1,minX=width,minY=height,maxX=0,maxY=0;
                queue[0]=start;visited[start]=true;
                while(head<tail)
                {
                    int p=queue[head++],x=p%width,y=p/width;
                    minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
                    Visit(x>0?p-1:-1);Visit(x+1<width?p+1:-1);Visit(y>0?p-width:-1);Visit(y+1<height?p+width:-1);
                }
                if(tail>pixels.Length/2000)boxes.Add(new RectInt(minX,minY,maxX-minX+1,maxY-minY+1));
                void Visit(int p)
                {if(p<0 || visited[p] || pixels[p].a<=16)return;visited[p]=true;queue[tail++]=p;}
            }
            if(boxes.Count!=count)throw new InvalidOperationException("Expected "+count+" separated achievement medals, found "+boxes.Count);
            var ordered=boxes.OrderByDescending(b=>b.y+b.height*.5f).ToArray();
            return Enumerable.Range(0,count/4).SelectMany(row=>ordered.Skip(row*4).Take(4).OrderBy(b=>b.x)).ToArray();
        }
    }
}
