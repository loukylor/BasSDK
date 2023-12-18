using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace ThunderRoad
{
    // TODO: consider converting completely to serialized object/property 
    public class CreatureEditorGUI : EditorWindow
    {
        public Vector2 scrollPos;
        public List<Error> errors = new();
        public bool IsValid => !errors.Any(error => error.level == MessageType.Error);

        public GameObject creatureRoot;
        public bool IsCreature => creatureRoot != null 
            && creatureRoot.GetComponent<Creature>() != null;

        public CreatureCreatorConfig creatorConfig;
        public SerializedObject serializedObj;
        public SerializedProperty creatorConfigProp;
        public SerializedProperty saveLocationProp;
        public SerializedProperty idProp;
        public SerializedProperty createWaveProp;
        public SerializedProperty genderProp;
        public SerializedProperty addressableAssetsGroupProp;
        public SerializedProperty addEyeComponentsProp;
        public SerializedProperty animatorAddressProp;
        public SerializedProperty hasSeparateNameAndIDProp;
        public SerializedProperty nameProp;
        public bool advancedFoldout;

        [MenuItem("ThunderRoad (SDK)/Creature Editor")]
        public static void ShowWindow()
        {
            GetWindow<CreatureEditorGUI>("Creature Editor");
        }

        private void OnEnable()
        {
            if (creatorConfig == null)
                creatorConfig = new CreatureCreatorConfig();

            if (serializedObj == null)
            {
                serializedObj = new SerializedObject(this);
                creatorConfigProp = serializedObj.FindProperty("creatorConfig");
                saveLocationProp = creatorConfigProp.FindPropertyRelative(nameof(creatorConfig.saveLocation));
                idProp = creatorConfigProp.FindPropertyRelative(nameof(creatorConfig.id));
                addressableAssetsGroupProp = creatorConfigProp.FindPropertyRelative(nameof(creatorConfig.addressableAssetGroup));
                createWaveProp = creatorConfigProp.FindPropertyRelative(nameof(creatorConfig.createWave));
                genderProp = creatorConfigProp.FindPropertyRelative(nameof(creatorConfig.gender));
                addEyeComponentsProp = creatorConfigProp.FindPropertyRelative(nameof(creatorConfig.addEyeComponents));
                animatorAddressProp = creatorConfigProp.FindPropertyRelative(nameof(creatorConfig.animatorAddress));
                hasSeparateNameAndIDProp = creatorConfigProp.FindPropertyRelative(nameof(creatorConfig.hasSeparateNameAndID));
                nameProp = creatorConfigProp.FindPropertyRelative(nameof(creatorConfig.name));
            }
        }

        private void OnGUI()
        {
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 200;

            GUILayout.Space(5);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            EditorGUILayout.LabelField("Creature Editor", new GUIStyle("BoldLabel") { fontSize = 15 });
            GUILayout.Space(8);

            creatureRoot = (GameObject)EditorGUILayout.ObjectField("Creature Root", creatureRoot, typeof(GameObject), true);
            GUILayout.Space(5);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            using (EditorGUILayout.ScrollViewScope scope = new(scrollPos, GUILayout.ExpandHeight(false)))
            {
                scrollPos = scope.scrollPosition;

                errors.Clear();
                using (new EditorGUI.DisabledGroupScope(creatureRoot == null))
                {
                    if (IsCreature)
                        OnEdit();
                    else
                        OnCreate();
                }

                GUILayout.Space(3);
                EditorGUILayout.LabelField("Issues", new GUIStyle("BoldLabel") { fontSize = 15 });
                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

                GUIStyle style = new(GUI.skin.label) { wordWrap = true };
                foreach (Error error in errors.OrderByDescending((err) => err.level))
                {
                    Texture2D icon = error.level switch
                    {
                        MessageType.Info => EditorGUIUtility.FindTexture("console.infoicon"),
                        MessageType.Warning => EditorGUIUtility.FindTexture("console.warnicon"),
                        MessageType.Error => EditorGUIUtility.FindTexture("console.erroricon"),
                        _ => null
                    };

                    using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        GUIContent label = new(error.message, icon);
                        GUILayout.Label(label, style);

                        // I have spent too long trying to center this damn button
                        if (error.autoFix != null && GUILayout.Button("Auto Fix", GUILayout.ExpandWidth(false)))
                            error.autoFix.Invoke();
                    }
                }
            }

            EditorGUIUtility.labelWidth = labelWidth;
        }

        private void OnEdit()
        {
            // Validate creature object
            ValidateEditCreature();
        }

        private void OnCreate()
        {
            serializedObj.Update();

            EditorGUILayout.PropertyField(saveLocationProp);
            EditorGUILayout.PropertyField(idProp, new GUIContent("Creature ID"));
            EditorGUILayout.PropertyField(addressableAssetsGroupProp);
            EditorGUILayout.PropertyField(createWaveProp);
            EditorGUILayout.PropertyField(genderProp);
            if (advancedFoldout = EditorGUILayout.Foldout(advancedFoldout, "Advanced (all optional)", true))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(addEyeComponentsProp);
                EditorGUILayout.PropertyField(animatorAddressProp);
                EditorGUILayout.PropertyField(hasSeparateNameAndIDProp);
                using (new EditorGUI.DisabledScope(!hasSeparateNameAndIDProp.boolValue))
                    EditorGUILayout.PropertyField(nameProp);
                EditorGUI.indentLevel--;
            }

            ValidateCreateCreature();

            using (new EditorGUI.DisabledGroupScope(!IsValid))
                if (GUILayout.Button("Create"))
                    creatureRoot = CreatureCreator.CreateCreature(creatureRoot, creatorConfig);
         
            serializedObj.ApplyModifiedProperties();
        }

        private void ValidateCreatureCommon()
        {
            if (creatureRoot == null) return;

            Animator animator = creatureRoot.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                errors.Add(new Error(
                    MessageType.Error,
                    "No Animator detected on the creature. Ensure the mesh has a humanoid rig."));
            }
            else 
            {
                if (!animator.isHuman)
                {
                    errors.Add(new Error(
                        MessageType.Error,
                        "Creature does not have a humanoid rig. The creature creator does not support non-humanoid rigs. Please switch rig type."));
                }
                else 
                {
                    // Necessary to check for eyes like this since GetBoneTransform doesn't work for prefab assets
                    bool hasLeft = false;
                    bool hasRight = false;
                    foreach (HumanBone bone in animator.avatar.humanDescription.human)
                    {
                        if (bone.humanName == HumanBodyBones.LeftEye.ToString())
                            hasLeft = true;
                        else if (bone.humanName == HumanBodyBones.RightEye.ToString())
                            hasRight = true;
                    }

                    if (!hasLeft || !hasRight)
                    {
                        errors.Add(new Error(
                            MessageType.Warning,
                            "One or more eye is missing from animator rig. Without both, the console will spew errors while the creature. They can be a dummy bones."));
                    }
                }

                // B&S will make scale Vector3.one for all bones during runtime, so non all one scale won't work
                static bool CheckChildrenScale(Transform parent)
                {
                    if (parent.localScale.Round(2) != Vector3.one)
                        return false;

                    foreach (Transform child in parent)
                        if (!CheckChildrenScale(child))
                            return false;

                    return true;
                }
                if (!CheckChildrenScale(animator.transform))
                    errors.Add(new Error(MessageType.Error, "Non default scale detected on bones. Ensure the scale on all bones is (1, 1, 1)."));
            }

            foreach (SkinnedMeshRenderer renderer in creatureRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!renderer.sharedMesh.isReadable)
                {
                    errors.Add(new Error(
                        MessageType.Error,
                        $"Mesh \"{renderer.sharedMesh.name}\" is not marked as readable. Please change it in import settings.",
                        () => 
                        {
                            string meshPath = AssetDatabase.GetAssetPath(renderer.sharedMesh);
                            ModelImporter importer = AssetImporter.GetAtPath(meshPath) as ModelImporter;
                            importer.isReadable = true;
                            importer.SaveAndReimport();

                        }
                    ));
                }

                if (renderer.sharedMaterials.Length > 1)
                {
                    errors.Add(new Error(
                        MessageType.Warning,
                        $"Renderer with mesh \"{renderer.sharedMesh.name}\" has multiple materials. Ensure these materials do not have overlapping UV maps, or else decals will not function properly."
                    ));
                }

                for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                {
                    if (renderer.sharedMaterials[i] != null && !renderer.sharedMaterials[i].HasTexture("_RevealMask"))
                    {
                        int j = i;
                        SkinnedMeshRenderer loopedRenderer = renderer;
                        void autoFix()
                        {
                            using EditPrefabScope<SkinnedMeshRenderer> scope = new(loopedRenderer);
                            Material[] newMats = scope.item.sharedMaterials;
                            Material newMat = new(Shader.Find("ThunderRoad/Lit"));
                            Material oldMat = newMats[j];
                            newMat.CopyPropertiesFromMaterial(oldMat);
                            newMat.renderQueue = 2100;
                            newMats[j] = newMat;

                            string path = Path.Combine(
                                "Assets",
                                creatorConfig.saveLocation,
                                oldMat.name + ".mat"
                            );
                            AssetDatabase.CreateAsset(newMat, path);
                            scope.item.sharedMaterials = newMats;
                        }
                        errors.Add(new Error(
                            MessageType.Warning,
                            $"Material \"{renderer.sharedMaterials[i].name}\" does not have a \"_RevealMask\" property. Without it, decals will not function. If unsure how to fix, use the \"ThunderRoad/Lit\" shader on your materials.",
                            IsCreature ? autoFix : null
                        ));
                    }
                }
            }
        }

        private void ValidateEditCreature()
        {
            if (creatureRoot == null) return;

            Creature creature = creatureRoot.GetComponent<Creature>();
            Rigidbody rigidBody = creatureRoot.GetComponent<Rigidbody>();
            ConstantForce constantForce = creatureRoot.GetComponent<ConstantForce>();
            if (creature == null || rigidBody == null || constantForce == null)
            {
                errors.Add(new Error(
                    MessageType.Error, 
                    "No Creature, Rigidbody or Constant Force components detected.", 
                    () => {
                        creatureRoot.TryGetOrAddComponent<Creature>(out _);
                        creatureRoot.TryGetOrAddComponent<Rigidbody>(out _);
                        creatureRoot.TryGetOrAddComponent<ConstantForce>(out _);
                    }
                ));
            }

            if (creature != null && creature.jaw == null)
            {
                // There is no null check on the jaw bone, so it has to be
                // assigned or the listed components will throw an NRE if
                // present.
                errors.Add(new Error(
                    MessageType.Error,
                    "No jaw assigned in Creature component. AI will not function without it. This can be a dummy bone."));
            }

            Locomotion locomotion = creatureRoot.GetComponent<Locomotion>();
            if (locomotion == null)
            {
                errors.Add(new Error(
                    MessageType.Warning, 
                    "No Locomotion component detected. Without it, the creature won't be able to move.", 
                    () => creatureRoot.TryGetOrAddComponent<Locomotion>(out _)));
            }

            Mana mana = creatureRoot.GetComponent<Mana>();
            if (mana == null)
            {
                errors.Add(new Error(
                    MessageType.Warning, 
                    "No Mana component detected. Without it, the creature won't be able to cast spells.", 
                    () => creatureRoot.TryGetOrAddComponent<Locomotion>(out _)));
            }

            LightVolumeReceiver lightVolumeReceiver = creatureRoot.GetComponent<LightVolumeReceiver>();
            if (lightVolumeReceiver == null)
            {
                errors.Add(new Error(
                    MessageType.Warning, 
                    "No LightVolumeReceiver component detected.", 
                    () => creatureRoot.TryGetOrAddComponent<LightVolumeReceiver>(out _)));
            }

            Transform brainParent = creatureRoot.transform.Find("Brain");
            if (brainParent == null || brainParent.GetComponent<Brain>() == null || brainParent.GetComponent<NavMeshAgent>() == null)
            {
                errors.Add(new Error(
                    MessageType.Error, 
                    "No Brain or NavMeshAgent components detected.", 
                    () => CreatureCreator.AddBrainComponents(creatureRoot)));
            }

            Transform containerParent = creatureRoot.transform.Find("Container");
            if (containerParent == null || containerParent.GetComponent<Container>() == null || containerParent.GetComponent<Equipment>() == null)
            {
                errors.Add(new Error(
                    MessageType.Warning, 
                    "No Container or Equipment components found. Without them, the creature will be unable to hold or wear anything.", 
                    () => CreatureCreator.AddContainerComponents(creatureRoot)));
            }

            Transform footstepParent = creatureRoot.transform.Find("Footstep");
            if (footstepParent == null || footstepParent.GetComponent<Footstep>() == null)
            {
                errors.Add(new Error(
                    MessageType.Warning, 
                    "No Footstep component found. Without it, the creature will not make footstep noises.", 
                    () => CreatureCreator.AddFootstepComponents(creatureRoot)));
            }

            Transform climberParent = creatureRoot.transform.Find("Climber");
            if (climberParent == null || climberParent.GetComponent<FeetClimber>() == null)
            {
                errors.Add(new Error(
                    MessageType.Warning, 
                    "No FeetClimber component found. Without it, the creature will not // TODO.", 
                    () => CreatureCreator.AddClimberComponents(creatureRoot)));
            }

            if (creature.allEyes.Any(eye => eye == null))
            {
                errors.Add(new Error(
                    MessageType.Error,
                    "One or more eyes on the creature are null."
                ));
            }

            ValidateCreatureCommon();
        }

        private void ValidateCreateCreature()
        {
            if (creatureRoot == null) return;

            Creature creature = creatureRoot.GetComponent<Creature>();
            if (creature != null)
            {
                errors.Add(new Error(
                    MessageType.Error, 
                    "Existing Creature component detected. Can't create creature based off already existing creature."));
            }

            ValidateCreatureCommon();
            creatorConfig.ReportErrors(errors);
        }

        public class Error
        {
            public MessageType level;
            public string message;
            public Action autoFix;

            public Error(MessageType level, string message, Action autoFix = null)
            {
                this.level = level;
                this.message = message;
                this.autoFix = autoFix;
            }
        }

        public readonly struct EditPrefabScope<T> : IDisposable where T : Component
        {
            public readonly T item;
            private readonly PrefabUtility.EditPrefabContentsScope? scope;

            public EditPrefabScope(T item) {
                Undo.RecordObject(item, "Edit prefab");
                if (PrefabUtility.IsPartOfAnyPrefab(item))
                {
                    // Get path of the prefab asset item belongs to
                    string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(item);

                    // Get path of item's transform from the prefab root
                    string componentPath = item.gameObject.GetPathFromRoot();
                    
                    // Load the prefab contents
                    scope = new PrefabUtility.EditPrefabContentsScope(path);
                    componentPath = componentPath[(2 + scope.Value.prefabContentsRoot.name.Length)..];

                    // Find the component that corresponds with item on the prefab
                    int componentIndex = Array.IndexOf(item.transform.GetComponents<T>(), item);
                    this.item = scope.Value.prefabContentsRoot.transform.Find(componentPath)
                        .GetComponents<T>()[componentIndex];
                }
                else
                {
                    this.item = item;
                    scope = null;
                }
            }

            public void Dispose()
            {
                if (scope != null)
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(item);
                    // Save the new modified prefab and unload it
                    scope.Value.Dispose();
                }
            }
        }
    }
}
