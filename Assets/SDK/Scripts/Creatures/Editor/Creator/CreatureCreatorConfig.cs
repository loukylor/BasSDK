using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using static ThunderRoad.CreatureEditorGUI;

namespace ThunderRoad
{
    [Serializable]
    public class CreatureCreatorConfig
    {
        public string saveLocation;
        [Tooltip("ID is used internally by the game, generally formatted like so: \"ExampleCreature\"")]
        public string id;
        public AddressableAssetGroup addressableAssetGroup;

        [Tooltip("Whether to automatically create a wave where this creature will spawn")]
        public bool createWave = true;

        // False by default because eye components need additional setup that cannot be done automatically
        public bool addEyeComponents;
        public string animatorAddress = "Animation.Controller.Human";
        public bool hasSeparateNameAndID;
        public string name;
        public string GetActualName => hasSeparateNameAndID ? name : id;

        public string JsonFolderPathAbsolute => Path.Combine(FileManager.GetFullPath(FileManager.Type.JSONCatalog, FileManager.Source.Mods), saveLocation);
        public string JsonName => $"Creature_{id}.json";
        public string JsonPathAbsolute => Path.Combine(JsonFolderPathAbsolute, JsonName);
        public string HandPoseJsonPathAboslute => Path.Combine(JsonFolderPathAbsolute, "HandPoses");
        public string WaveJsonName => $"Wave_{id}.json";
        public string WaveJsonPathAbsolute => Path.Combine(JsonFolderPathAbsolute, WaveJsonName);
        public string CreatureTableJsonName => $"CreatureTable_{id}.json";
        public string CreatureTableJsonPathAbsolute => Path.Combine(JsonFolderPathAbsolute, CreatureTableJsonName);
        public string PrefabPathAbsolute => Path.Combine(Application.dataPath, saveLocation, $"{id}.prefab");

        public void ReportErrors(List<Error> errors)
        {
            bool saveLocationValid = true;
            if (string.IsNullOrWhiteSpace(saveLocation)
                || !Directory.Exists(Path.Combine(Application.dataPath, saveLocation)))
            {
                errors.Add(new Error(MessageType.Error, "Save location invalid."));
                saveLocationValid = false;
            }

            if (string.IsNullOrWhiteSpace(id))
                errors.Add(new Error(MessageType.Error, "ID is invalid."));
            else
            {
                if (saveLocationValid)
                {
                    if (File.Exists(JsonPathAbsolute))
                        errors.Add(new Error(MessageType.Warning, "An existing CreatureData Json was found. This will be overwritten."));
                    if (File.Exists(PrefabPathAbsolute))
                        errors.Add(new Error(MessageType.Warning, "An existing Creature prefab was found. This will be overwritten."));
                    if (Directory.Exists(HandPoseJsonPathAboslute))
                        errors.Add(new Error(MessageType.Warning, "An existing HandPose Json folder was found. This will be overwritten"));
                }

                if (hasSeparateNameAndID && string.IsNullOrWhiteSpace(name))
                    errors.Add(new Error(MessageType.Error, "Name is invalid."));
            }

            if (addressableAssetGroup == null)
                errors.Add(new Error(MessageType.Error, "AssetBundleGroup is null."));

            // I would like to validate animator address but i dont think i can
        }
    }
}
