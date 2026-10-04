using System.Collections.Generic;
using PushStars.Core;
using PushStars.UI;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    public static class ArenaSetup
    {
        [MenuItem("Tools/Push Stars/Arenas/Import Catalog")]
        public static void Run()
        {
            const string root="Assets/_Project/Resources/Arenas/";
            foreach(var file in System.IO.Directory.GetFiles(root,"*.png"))
            {
                AssetDatabase.ImportAsset(file,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(file);
                importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
                importer.spritePixelsPerUnit=100; importer.mipmapEnabled=false;
                importer.npotScale=TextureImporterNPOTScale.None; importer.wrapMode=TextureWrapMode.Clamp;
                importer.alphaIsTransparency=true; importer.maxTextureSize=2048;
                importer.textureCompression=TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            }
            const string path="Assets/_Project/Resources/ArenaCatalog.asset";
            var catalog=AssetDatabase.LoadAssetAtPath<ArenaCatalog>(path);
            if(catalog==null) {catalog=ScriptableObject.CreateInstance<ArenaCatalog>();AssetDatabase.CreateAsset(catalog,path);}
            var theme=Resources.Load<PushStarsTheme>("PushStarsTheme");
            var entries=new List<ArenaDefinition>();
            entries.Add(new ArenaDefinition {Id="crystal",Title="Crystal Gym",Home=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/IMG_0916.PNG"),
                Battle=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/bg_fight-divider-variant.png")});
            var ids=new[]{"sunset-yard","city-rooftop","school-gym","lava-forge","jungle","dragon-dojo","ice-temple","colosseum","underwater"};
            var names=new[]{"Sunset Yard","City Rooftop","School Gym","Lava Forge","Jungle Ruins","Dragon Dojo","Ice Temple","Colosseum","Underwater Dome"};
            for(int i=0;i<ids.Length;i++)
            {
                var existing=catalog.Find(ids[i]);
                entries.Add(new ArenaDefinition{Id=ids[i],Title=names[i],Home=AssetDatabase.LoadAssetAtPath<Sprite>(root+ids[i]+".png"),
                    Battle=existing?.Battle,Access=existing?.Access??ArenaAccess.Free,Price=existing?.Price??0});
            }
            catalog.Arenas=entries.ToArray();catalog.SelectionPanel=AssetDatabase.LoadAssetAtPath<Sprite>(root+"selection-panel.png");
            EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
            Debug.Log("Arena catalog: current arena + 9 new locations. Free access until acquisition rules are configured.");
        }
    }
}
