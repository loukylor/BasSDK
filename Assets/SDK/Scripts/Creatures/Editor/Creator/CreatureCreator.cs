using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace ThunderRoad
{
    public static class CreatureCreator
    {
        public static GameObject CreateCreature(GameObject creatureBase, CreatureCreatorConfig config)
        {
            // Change hierarchy to look like this:
            // Root
            // - Mesh
            //   - Hips
            // Rather than:
            // Mesh
            //   - Hips
            GameObject creatureMesh = UnityEngine.Object.Instantiate(creatureBase);
            GameObject creatureRoot = new(creatureBase.name);
            Undo.RegisterCreatedObjectUndo(creatureRoot, "Undo creature creation.");

            if (creatureMesh.scene.IsValid())
                SceneManager.MoveGameObjectToScene(creatureRoot, creatureMesh.scene);

            creatureRoot.transform.position = creatureMesh.transform.position;
            creatureMesh.transform.parent = creatureRoot.transform;
            creatureMesh.name = "Mesh";

            Animator creatureAnimator = creatureMesh.GetComponent<Animator>();

            // Copy various GameObjects from TestChar prefab
            GameObject creatureTemplate = PrefabUtility.LoadPrefabContents("Assets/SDK/Examples/Characters/TestChar.prefab");

            // Make the custom creature copy the pose of the template
            Transform templateMeshRoot = creatureTemplate.transform.Find("Mesh");
            HumanPose pose = new();
            using (HumanPoseHandler poseHandler = new(templateMeshRoot.GetComponent<Animator>().avatar, templateMeshRoot))
                poseHandler.GetHumanPose(ref pose);
            using (HumanPoseHandler poseHandler = new(creatureAnimator.avatar, creatureMesh.transform))
                poseHandler.SetHumanPose(ref pose);

            // Add Rig between mesh and hips
            Transform hips = creatureAnimator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips.parent == creatureMesh)
            {
                GameObject rigRoot = new("Rig");
                rigRoot.transform.parent = hips.parent;
                hips.parent = rigRoot.transform;
            }

            foreach (Transform child in creatureTemplate.transform)
            {
                if (child.name == "Ragdoll")
                    continue;

                if (child.name == "Mesh")
                {
                    CopyGameObjectComponents(child.gameObject, creatureMesh);
                    continue;
                }

                GameObject childCopy = UnityEngine.Object.Instantiate(child.gameObject, creatureRoot.transform);
                childCopy.name = child.name;
                childCopy.transform.localPosition = child.localPosition;
            }

            // As a side note, there is a component called LightLevelIndicator
            // only on the left hand of both the human male and female
            // creatures that is references nowhere in code. I imagine its not
            // supposed to be there.
            List<CreatureEye> eyes = AddEyeComponents(creatureAnimator);
            AddMeshComponents(creatureRoot);
            new CreatureRagdollCreator(creatureRoot, creatureAnimator, creatureTemplate).CreateRagdoll();
            Creature creature = AddRootComponents(creatureRoot, creatureAnimator, eyes, config);
            // Only container components needs to be called to assign the created container to 
            // `linkedContainer` in holder
            AddContainerComponents(creatureRoot);
            creature.data = CreateCreatureData(config, creatureRoot.transform.Find("Ragdoll/Parts"));
            CreateHandPoses(config, creature, creature.data.name, creatureTemplate.GetComponent<Creature>());

            if (config.createWave)
            {
                CreateWaveData(config);
                CreateCreatureTable(config);
            }

            // Unload template and set selected object to new creature game object
            PrefabUtility.UnloadPrefabContents(creatureTemplate);
            Selection.activeGameObject = creatureRoot;

            // Create prefab file
            GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(creatureRoot, config.PrefabPathAbsolute, InteractionMode.AutomatedAction);

            // Add entry in addressable assets
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
            AddressableAssetEntry entry = AddressableAssetSettingsDefaultObject.Settings.CreateOrMoveEntry(guid, config.addressableAssetGroup);
            entry.address = creature.data.prefabAddress;
            entry.SetLabel("Windows", true);
            entry.SetLabel("Android", true);

            return prefab;
        }

        public static Creature AddRootComponents(GameObject creatureRoot, Animator animator, List<CreatureEye> eyes, CreatureCreatorConfig config)
        {
            creatureRoot.TryGetOrAddComponent(out Creature creature);
            creature.creatureId = config.id;
            creature.animator = animator;
            creature.container = creatureRoot.GetComponentInChildren<Container>();
            creature.jaw = animator.GetBoneTransform(HumanBodyBones.Jaw);
            creature.allEyes = eyes;

            // If the animator is missing an eye then centerEyes will need to be set manually
            // or Creature.Init will nullref
            Transform leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            Transform rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (leftEye == null && rightEye != null)
                creature.centerEyes = leftEye;
            else if (leftEye != null && rightEye == null)
                creature.centerEyes = rightEye;
            else if (leftEye == null && rightEye == null)
                creature.centerEyes = animator.GetBoneTransform(HumanBodyBones.Head);

            creatureRoot.TryGetOrAddComponent<Mana>(out _);
            creatureRoot.TryGetOrAddComponent(out Locomotion locomotion);
            locomotion.colliderGroundMaterial = AssetDatabase.LoadAssetAtPath<PhysicMaterial>("Assets/SDK/PhysicMaterials/LocomotionGround.physicMaterial");
            locomotion.colliderFlyMaterial = AssetDatabase.LoadAssetAtPath<PhysicMaterial>("Assets/SDK/PhysicMaterials/LocomotionFly.physicMaterial");

            creatureRoot.TryGetOrAddComponent<Rigidbody>(out _);
            creatureRoot.TryGetOrAddComponent<LightVolumeReceiver>(out _);
            creatureRoot.TryGetOrAddComponent<ConstantForce>(out _);
            
            return creature;
        }

        public static void AddMeshComponents(GameObject creatureRoot)
        {
            foreach (SkinnedMeshRenderer renderer in creatureRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.gameObject.TryGetOrAddComponent<MaterialInstance>(out _);
                renderer.gameObject.TryGetOrAddComponent(out RevealDecal decal);
                decal.type = RevealDecal.Type.Body;
            }
        }

        // TODO: add option to replace assets rather than create new ones
        // TODO: Find a better way to do this. This just seems like a lot of repeat
        public static void AddBrainComponents(GameObject creatureRoot)
        {
            Transform brainParent = creatureRoot.transform.FindOrAddTransform("Brain", creatureRoot.transform.position);
            brainParent.gameObject.TryGetOrAddComponent<Brain>(out _);
            brainParent.gameObject.TryGetOrAddComponent<NavMeshAgent>(out _);
        }

        public static void AddContainerComponents(GameObject creatureRoot)
        {
            Transform containerParent = creatureRoot.transform.FindOrAddTransform("Container", creatureRoot.transform.position);
            containerParent.gameObject.TryGetOrAddComponent(out Container container);
            foreach (Holder holder in creatureRoot.GetComponentsInChildren<Holder>(true))
                holder.linkedContainer = container;
            containerParent.gameObject.TryGetOrAddComponent<Equipment>(out _);
        }

        public static void AddFootstepComponents(GameObject creatureRoot)
        {
            Transform footstepParent = creatureRoot.transform.FindOrAddTransform("Footstep", creatureRoot.transform.position);
            footstepParent.gameObject.TryGetOrAddComponent<Footstep>(out _);
        }

        public static void AddClimberComponents(GameObject creatureRoot)
        {
            Transform climberParent = creatureRoot.transform.FindOrAddTransform("Climber", creatureRoot.transform.position);
            climberParent.gameObject.TryGetOrAddComponent<FeetClimber>(out _);
        }

        public static List<CreatureEye> AddEyeComponents(Animator animator)
        {
            List<CreatureEye> res = new();

            Transform eye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            if (eye != null)
            {
                CreatureEye eyeComponent = eye.gameObject.AddComponent<CreatureEye>();
                eyeComponent.eyeTag = "Left";
                res.Add(eyeComponent);

                Transform forwardTransform = new GameObject("ForwardTransform").transform;
                forwardTransform.position = eye.position;
                forwardTransform.parent = eye;
                forwardTransform.rotation = animator.transform.rotation;
            }

            eye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (eye != null)
            {
                CreatureEye eyeComponent = eye.gameObject.AddComponent<CreatureEye>();
                eyeComponent.eyeTag = "Right";
                res.Add(eyeComponent);

                Transform forwardTransform = new GameObject("ForwardTransform").transform;
                forwardTransform.position = eye.position;
                forwardTransform.parent = eye;
                forwardTransform.rotation = animator.transform.rotation;
            }
            return res;
        }

        public static CreatureData CreateCreatureData(CreatureCreatorConfig config, Transform ragdollPartsRoot)
        {
            CreatureData data = new()
            {
                name = config.GetActualName,
                id = config.id,
                prefabAddress = $"{config.addressableAssetGroup.Name}.Creature.{config.id}",
                animatorBundleAddress = config.animatorAddress,
                ragdollData = new CreatureData.RagdollData()
                {
                    gripEffectId = "GripAndGrab",
                    footItemId = "Foot",
                    footTrackedItemId = "FootTracked",
                    bodyDefaultDamagerID = "Ragdoll",
                    penetrationEffectId = "PenetrationBlood"
                },
                expressionAttackId = "Attack",
                expressionPainId = "Pain",
                expressionDeathId = "Death",
                expressionChokeId = "Choke",
                expressionAngryId = "Angry"
            };
            data.version = data.GetCurrentVersion();

            // Create ragdoll part data
            Dictionary<RagdollPart.Type, CreatureData.PartData> partDatas = new();
            foreach (Transform partTransform in ragdollPartsRoot.transform)
            {
                RagdollPart part = partTransform.GetComponent<RagdollPart>();
                if (part == null)
                    continue;

                // If it's a part on the left and right see if the opposite part exists
                CreatureData.PartData partData = null;
                string partTypeString = part.type.ToString();
                if (partTypeString.Contains("Left"))
                    partData = partDatas.GetValueOrDefault(Enum.Parse<RagdollPart.Type>(partTypeString.Replace("Left", "Right")));
                else if (part.type.ToString().Contains("Right"))
                    partData = partDatas.GetValueOrDefault(Enum.Parse<RagdollPart.Type>(partTypeString.Replace("Right", "Left")));
                if (partData == null)
                {
                    partData = new CreatureData.PartData()
                    {
                        bodyDamagerID = "Ragdoll"
                    };
                    data.ragdollData.parts.Add(partData);
                }

                partData.bodyPartTypes |= part.type;
                
                switch (part.type)
                {
                    case RagdollPart.Type.Head:
                        partData.bodyDamagerID = "Punch";
                        break;
                    case RagdollPart.Type.LeftHand:
                    case RagdollPart.Type.RightHand:
                        partData.bodyAttackDamagerID = "RagdollHead";
                        break;
                }

                partDatas[part.type] = partData;
            }

            Catalog.GetCategoryData(Category.Creature).AddCatalogData(data);

            // Save CreatureData json
            string creatureDataJson = JsonConvert.SerializeObject(data, Catalog.jsonSerializerSettings);
            File.WriteAllText(config.JsonPathAbsolute, creatureDataJson);

            return data;
        }

        public static WaveData CreateWaveData(CreatureCreatorConfig config)
        {
            WaveData data = new()
            {
                id = config.id,
                title = config.id,
                loopBehavior = WaveData.LoopBehavior.LoopSeamless,
                totalMaxAlive = 1,
                alwaysAvailable = true,
                factions = new List<WaveData.WaveFaction>()
                {
                    new WaveData.WaveFaction()
                    {
                        factionID = 0, // default aggressive faction
                        factionMaxAlive = 1
                    }
                },
                groups = new List<WaveData.Group>()
                {
                    new WaveData.Group()
                    {
                        reference = WaveData.Group.Reference.Table,
                        referenceID = config.id,
                        minMaxCount = new Vector2Int(1, 2)
                    }
                }
            };
            data.version = data.GetCurrentVersion();

            Catalog.GetCategoryData(Category.Wave).AddCatalogData(data);

            string json = JsonConvert.SerializeObject(data, Catalog.jsonSerializerSettings);
            File.WriteAllText(config.WaveJsonPathAbsolute, json);

            return data;
        }

        public static CreatureTable CreateCreatureTable(CreatureCreatorConfig config)
        {
            CreatureTable data = new()
            {
                id = config.id,
                drops = new List<CreatureTable.Drop> { 
                    new CreatureTable.Drop()
                    {
                        reference = CreatureTable.Drop.Reference.Creature,
                        referenceID = config.id,
                        probabilityWeights = new int[5] { 1, 0, 0, 0, 0 }
                    }
                }
            };
            data.version = data.GetCurrentVersion();

            Catalog.GetCategoryData(Category.CreatureTable).AddCatalogData(data);

            string json = JsonConvert.SerializeObject(data, Catalog.jsonSerializerSettings);
            File.WriteAllText(config.CreatureTableJsonPathAbsolute, json);

            return data;
        }

        // So, B&S store hand pose data (different ways a creature can grip something) using
        // hard coded positions, meaning it breaks the fuck out of anything that isn't
        // set up in the same way. The only way to workaround this (I can think of) is to 
        // make the created creature copy the pose of the template creature for each 
        // hand pose, and then store those hand poses. Yep, it's kinda dumb, but it is
        // what it is.
        // Futhermore, `ThunderRoadSettings.current.overrideData` has to be true for this to work
        // or else the game won't load the hand pose data
        // ;-; pls WarpFrog just use muscle anims
        public static void CreateHandPoses(CreatureCreatorConfig config, Creature creature, string creatureName, Creature templateCreature)
        {
            HumanPose humanPose = new();
            RagdollHand[] creatureHands = creature.GetComponentsInChildren<RagdollHand>();
            RagdollHand creatureLeft = creatureHands[0].side == Side.Left ? creatureHands[0] : creatureHands[1];
            RagdollHand creatureRight = creatureHands[0].side == Side.Left ? creatureHands[1] : creatureHands[0];
            RagdollHandPoser creatureHandPoserLeft = creatureLeft.poser;
            RagdollHandPoser creatureHandPoserRight = creatureRight.poser;
            Transform creatureGripLeft = creatureLeft.transform.Find("Grip");
            Transform creatureGripRight = creatureRight.transform.Find("Grip");

            RagdollHand[] templateHands = templateCreature.GetComponentsInChildren<RagdollHand>();
            RagdollHand templateLeft = templateHands[0].side == Side.Left ? templateHands[0] : templateHands[1];
            RagdollHand templateRight = templateHands[0].side == Side.Left ? templateHands[1] : templateHands[0];
            RagdollHandPoser templateHandPoserLeft = templateLeft.poser;
            RagdollHandPoser templateHandPoserRight = templateRight.poser;

            using HumanPoseHandler creaturePoseHandler = new(creature.animator.avatar, creature.animator.transform);
            using HumanPoseHandler templatePoseHandler = new(templateCreature.animator.avatar, templateCreature.animator.transform);

            HumanPose originalPose = new();
            creaturePoseHandler.GetHumanPose(ref originalPose);

            MethodInfo saveFingerMethod = typeof(RagdollHandPoser).GetMethod("SaveFinger", BindingFlags.NonPublic | BindingFlags.Instance);

            if (!Directory.Exists(config.HandPoseJsonPathAboslute))
                Directory.CreateDirectory(config.HandPoseJsonPathAboslute);

            // Stop assets from updating (this makes batch importing much faster)
            AssetDatabase.StartAssetEditing();

            try
            {
                foreach (HandPoseData poseData in Catalog.GetDataList<HandPoseData>())
                {
                    HandPoseData.Pose pose = poseData.GetCreaturePose("HumanMale");

                    // Set pose onto template
                    templateHandPoserLeft.defaultHandPoseFingers = pose.leftFingers;
                    // Target needs to be set or else the actual mesh bones wont be set
                    templateHandPoserLeft.targetHandPoseFingers = pose.leftFingers;
                    templateHandPoserLeft.hasTargetHandPose = true;
                    templateHandPoserLeft.UpdatePoseThumb(0);
                    templateHandPoserLeft.UpdatePoseIndex(0);
                    templateHandPoserLeft.UpdatePoseMiddle(0);
                    templateHandPoserLeft.UpdatePoseRing(0);
                    templateHandPoserLeft.UpdatePoseLittle(0);

                    templateHandPoserRight.defaultHandPoseFingers = pose.rightFingers;
                    templateHandPoserRight.targetHandPoseFingers = pose.leftFingers;
                    templateHandPoserRight.hasTargetHandPose = true;
                    templateHandPoserRight.UpdatePoseThumb(0);
                    templateHandPoserRight.UpdatePoseIndex(0);
                    templateHandPoserRight.UpdatePoseMiddle(0);
                    templateHandPoserRight.UpdatePoseRing(0);
                    templateHandPoserRight.UpdatePoseLittle(0);

                    HandPoseData poseDataCopy = (HandPoseData)poseData.Clone();
                    poseDataCopy.poses.Clear();
                    HandPoseData.Pose newPose = poseDataCopy.AddCreaturePose(creatureName);

                    // Set same pose onto creature
                    templatePoseHandler.GetHumanPose(ref humanPose);
                    creaturePoseHandler.SetHumanPose(ref humanPose);

                    if (creatureLeft.fingerThumb != null)
                        saveFingerMethod.Invoke(creatureHandPoserLeft, new object[2] { newPose.leftFingers.thumb, creatureLeft.fingerThumb });
                    if (creatureLeft.fingerIndex != null)
                        saveFingerMethod.Invoke(creatureHandPoserLeft, new object[2] { newPose.leftFingers.index, creatureLeft.fingerIndex });
                    if (creatureLeft.fingerMiddle != null)
                        saveFingerMethod.Invoke(creatureHandPoserLeft, new object[2] { newPose.leftFingers.middle, creatureLeft.fingerMiddle });
                    if (creatureLeft.fingerRing != null)
                        saveFingerMethod.Invoke(creatureHandPoserLeft, new object[2] { newPose.leftFingers.ring, creatureLeft.fingerRing });
                    if (creatureLeft.fingerLittle != null)
                        saveFingerMethod.Invoke(creatureHandPoserLeft, new object[2] { newPose.leftFingers.little, creatureLeft.fingerLittle });
                    newPose.leftFingers.gripLocalPosition = creatureGripLeft.localPosition;
                    newPose.leftFingers.gripLocalRotation = creatureGripLeft.localRotation;
                    newPose.leftFingers.rootLocalPosition = creatureGripLeft.InverseTransformPoint(creatureLeft.transform.position);

                    // I dont trust mirroring so let's just do both sides manually
                    if (creatureRight.fingerThumb != null)
                        saveFingerMethod.Invoke(creatureHandPoserRight, new object[2] { newPose.rightFingers.thumb, creatureRight.fingerThumb });
                    if (creatureRight.fingerIndex != null)
                        saveFingerMethod.Invoke(creatureHandPoserRight, new object[2] { newPose.rightFingers.index, creatureRight.fingerIndex });
                    if (creatureRight.fingerMiddle != null)
                        saveFingerMethod.Invoke(creatureHandPoserRight, new object[2] { newPose.rightFingers.middle, creatureRight.fingerMiddle });
                    if (creatureRight.fingerRing != null)
                        saveFingerMethod.Invoke(creatureHandPoserRight, new object[2] { newPose.rightFingers.ring, creatureRight.fingerRing });
                    if (creatureRight.fingerLittle != null)
                        saveFingerMethod.Invoke(creatureHandPoserRight, new object[2] { newPose.rightFingers.little, creatureRight.fingerLittle });
                    newPose.rightFingers.gripLocalPosition = creatureGripRight.localPosition;
                    newPose.rightFingers.gripLocalRotation = creatureGripRight.localRotation;
                    newPose.rightFingers.rootLocalPosition = creatureGripRight.InverseTransformPoint(creatureRight.transform.position);

                    // Save pose
                    string jsonString = JsonConvert.SerializeObject(poseDataCopy, Catalog.jsonSerializerSettings);
                    string fileName = $"HandPose_{poseDataCopy.id}.json";
                    File.WriteAllText(Path.Combine(config.HandPoseJsonPathAboslute, fileName), jsonString);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                creaturePoseHandler.SetHumanPose(ref originalPose);
            }
        }

        /* Manikin is not yet supported in the SDK, so this is here for
         * when it is fully supported.
         * https://github.com/KospY/BasSDK/issues/53
         *
        public static void AddManikinComponents()
        {

            
            Animator animator = meshRoot.GetComponent<Animator>();

            ManikinAvatar manikinAvatar = ScriptableObject.CreateInstance<ManikinAvatar>();
            manikinAvatar.avatar = animator.avatar;
            manikinAvatar.humanDescription = animator.avatar.humanDescription;
            manikinAvatar.OnBeforeSerialize();

            string manikinAvatarPath = Path.Combine("Assets", saveLocation, meshRoot.transform.parent.name + " Manikin Avatar.asset");
            AssetDatabase.CreateAsset(manikinAvatar, GenerateNonDuplicatePath(manikinAvatarPath));

            meshRoot.TryGetOrAddComponent(out ManikinRig rig);
            rig.defaultManikinAvatar = manikinAvatar;
            rig.animator = animator;

            // Get root bone
            Transform root = animator.GetBoneTransform(HumanBodyBones.Hips);
            while (root.parent != animator.transform)
                root = root.parent;
            rig.rootBone = root;

            string rigPrefabPath = Path.Combine("Assets", saveLocation, meshRoot.transform.parent.name + " Root.prefab");
            rig.rigPrefab = PrefabUtility.SaveAsPrefabAsset(
                root.gameObject,
                GenerateNonDuplicatePath(rigPrefabPath),
                out _);

            rig.InitializeBones();
        }
         */

        // TODO: Figure out where to put this method
        public static void CopyGameObjectComponents(GameObject src, GameObject dest)
        {
            foreach (Component component in src.GetComponents<Component>())
            {
                if (component is Transform)
                    continue;

                if (dest.GetComponent(component.GetType()) != null)
                    continue;

                Common.CloneComponent(component, dest);
            }
        }
    }
}
