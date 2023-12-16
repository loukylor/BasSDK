using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ThunderRoad
{
    public class CreatureRagdollCreator
    {
        public readonly GameObject creatureRoot;
        public readonly Animator animator;
        public readonly GameObject template;

        private readonly Dictionary<Transform, Transform> templateToCreatureBones = new();
        private Animator templateAnimator;

        private CapsuleCollider hipsCollider;

        public CreatureRagdollCreator(GameObject creatureRoot, Animator animator, GameObject template)
        {
            this.creatureRoot = creatureRoot;
            this.animator = animator;
            this.template = template;
        }

        // To anyone editing this code in the future: This really isn't supposed to be perfect, just a starting point
        // In my experience, trying to make this perfect is kinda a waste of time or just way more complicated and or
        // clever than my pea brain can muster. - louky
        public void CreateRagdoll()
        {
            Ragdoll templateRagdoll = template.GetComponentInChildren<Ragdoll>();
            Transform ragdollTransform = creatureRoot.transform.FindOrAddTransform("Ragdoll", creatureRoot.transform.position);
            CreatureCreator.CopyGameObjectComponents(templateRagdoll.gameObject, ragdollTransform.gameObject);

            // Assign some required fields in Ragdoll
            Ragdoll ragdoll = ragdollTransform.GetComponent<Ragdoll>();
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            ragdoll.meshRootBone = hips;

            // The meshRig should be the first child of animator that leads to the rest of
            // the rig. e.g.:
            // Animator
            // - meshRig
            //   - Hips
            //     - Spine
            //     - RightUpLeg
            //     - LeftUpLeg
            // etc.
            Transform rig = hips;
            while (rig.parent != animator.transform)
                rig = rig.parent;
            ragdoll.meshRig = rig;

            Transform partsTransform = ragdollTransform.transform.FindOrAddTransform("Parts", ragdollTransform.transform.position);
            Transform templatePartsTransform = templateRagdoll.transform.Find("Parts");

            // Get HumanBodyBone from Transform in template ragdoll parts
            templateAnimator = template.GetComponentInChildren<Animator>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                Transform templateBone = templateAnimator.GetBoneTransform((HumanBodyBones)i);
                Transform bone = GetClosestHumanBone(animator, (HumanBodyBones)i);
                if (bone != null && templateBone != null)
                    templateToCreatureBones[templateBone] = bone;
            }

            // Copy each ragdoll part if it exists on the body
            Dictionary<RagdollPart, RagdollPart> templateToCreatedParts = new();
            foreach (Transform templatePartTransform in templatePartsTransform)
            {
                RagdollPart templatePart = templatePartTransform.GetComponent<RagdollPart>();
                if (!templateToCreatureBones.TryGetValue(templatePart.meshBone, out Transform bone))
                {
                    Debug.Log("Skipping Ragdoll Part: " + templatePart.name);
                    continue;
                }

                // If upper chest is null and we're doing chest right now, then
                // skip so that chest is treated as upper chest
                if (animator.GetBoneTransform(HumanBodyBones.UpperChest) == null
                    && templatePart.meshBone == templateAnimator.GetBoneTransform(HumanBodyBones.Chest))
                {
                    continue;
                }

                // TODO: Prevent meshes with duplicate bone names
                if (bone == null || partsTransform.Find(bone.name) != null)
                    continue;

                GameObject partGO = UnityEngine.Object.Instantiate(templatePart.gameObject, partsTransform);
                partGO.name = bone.name;

                RagdollPart part = partGO.GetComponent<RagdollPart>();
                templateToCreatedParts[templatePart.GetComponent<RagdollPart>()] = part;
                part.meshBone = bone;
                part.SetPositionToBone();
                Matrix4x4 templateToPartMatrix = partGO.transform.worldToLocalMatrix * templatePartTransform.localToWorldMatrix;
                part.boneToChildDirection = GetAsLongestAxis(templateToPartMatrix.MultiplyVector(part.boneToChildDirection));
                // TODO: Check if bone to child direction matters

                CharacterJoint joint = partGO.GetComponent<CharacterJoint>();
                if (joint != null)
                {
                    joint.axis = GetAsLongestAxis(templateToPartMatrix.MultiplyVector(joint.axis));
                    joint.swingAxis = GetAsLongestAxis(templateToPartMatrix.MultiplyVector(joint.swingAxis));
                }

                CorrectPartChildren(partGO.transform, templatePartTransform, part, templatePart);
            }

            ragdoll.headPart = templateToCreatedParts[ragdoll.headPart];
            ragdoll.leftUpperArmPart = templateToCreatedParts[ragdoll.leftUpperArmPart];
            ragdoll.rightUpperArmPart = templateToCreatedParts[ragdoll.rightUpperArmPart];
            if (templateToCreatedParts.ContainsKey(ragdoll.targetPart))
                ragdoll.targetPart = templateToCreatedParts[ragdoll.targetPart];
            else
                ragdoll.targetPart = partsTransform.Find(templateToCreatureBones[ragdoll.targetPart.meshBone].name).GetComponent<RagdollPart>();
            ragdoll.rootPart = templateToCreatedParts[ragdoll.rootPart];

            foreach (Transform child in partsTransform)
            {
                RagdollPart part = child.GetComponent<RagdollPart>();
                // The parent part on the ragdoll part at this point is the 
                // parent part for the template, so let's get the equivalent
                // ragdoll part
                if (part.parentPart != null)
                {
                    if (templateToCreatedParts.ContainsKey(part.parentPart))
                        part.parentPart = templateToCreatedParts[part.parentPart];
                    else
                        part.parentPart = templateToCreatedParts[part.parentPart.parentPart];

                    CharacterJoint joint = child.GetComponent<CharacterJoint>();
                    joint.connectedBody = part.parentPart.GetComponent<Rigidbody>();
                }

                part.linkedMeshBones = part.linkedMeshBones
                    .Where(bone => templateToCreatureBones.ContainsKey(bone))
                    .Select(bone => templateToCreatureBones[bone])
                    .ToArray();

                part.ignoredParts = part.ignoredParts
                    .Where(part => templateToCreatedParts.ContainsKey(part))
                    .Select(part => templateToCreatedParts[part])
                    .ToList();

                part.sliceFillMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/SDK/Examples/Reveal/Slice.mat");
                if (part.sliceChildAndDisableSelf != null)
                    part.sliceChildAndDisableSelf = templateToCreatedParts[part.sliceChildAndDisableSelf];

                RagdollPart wearablePart = part.wearable.GetComponentInParent<RagdollPart>();
                if (templateToCreatedParts.ContainsKey(wearablePart))
                    part.wearable = templateToCreatedParts[wearablePart].GetComponentInChildren<Wearable>();

                // The should handle heights may vary due to proportional differences, so assign them
                // manually
                Transform shoulderHandleR = child.Find("HandleShoulderR");
                if (shoulderHandleR != null)
                {
                    shoulderHandleR.position = new Vector3(
                        shoulderHandleR.position.x,
                        animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position.y,
                        shoulderHandleR.position.z
                    );
                }

                Transform shoulderHandleL = child.Find("HandleShoulderL");
                if (shoulderHandleL != null)
                {
                    shoulderHandleL.position = new Vector3(
                        shoulderHandleL.position.x,
                        animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position.y,
                        shoulderHandleL.position.z
                    );
                }

                if (part is RagdollHand hand) 
                {
                    hand.wristStats = templateToCreatedParts[hand.wristStats.GetComponentInParent<RagdollPart>()].GetComponentInChildren<WristStats>();
                    hand.lowerArmPart = templateToCreatedParts[hand.lowerArmPart];
                    hand.upperArmPart = templateToCreatedParts[hand.upperArmPart];

                    for (int i = 0; i < hand.fingers.Count; i++)
                    {
                        RagdollHand.Finger finger = hand.fingers[i];
                        if (finger.proximal.collider == null || finger.intermediate.collider == null || finger.distal.collider == null)
                        {
                            hand.fingers.RemoveAt(i);
                            i--;
                            continue;
                        }

                        GetFingerBoneFromName(animator, finger.proximal.collider.name, hand.side, out finger.proximal.mesh);
                        GetFingerBoneFromName(animator, finger.intermediate.collider.name, hand.side, out finger.intermediate.mesh);
                        GetFingerBoneFromName(animator, finger.distal.collider.name, hand.side, out finger.distal.mesh);

                        // For some reason the RagdollHand.Finger in the fingers list and the finger fields
                        // aren't the same instance so we have to assign them manually
                        FieldInfo fingerField = hand.GetType().GetField("finger" + finger.tip.name[0].ToString().ToUpper() + finger.tip.name[1..^3]);
                        fingerField?.SetValue(hand, finger);
                    }

                    WristRelaxer wristRelaxer = hand.GetComponent<WristRelaxer>();
                    // The arm twist bone isn't a standard bone so just make it the hand bone
                    wristRelaxer.armTwistBone = templateToCreatureBones[wristRelaxer.handBone];
                    wristRelaxer.upperArmBone = templateToCreatureBones[wristRelaxer.upperArmBone];
                    wristRelaxer.lowerArmBone = templateToCreatureBones[wristRelaxer.lowerArmBone];
                    wristRelaxer.handBone = templateToCreatureBones[wristRelaxer.handBone];
                }
            }
        }

        private void CorrectPartChildren(Transform part, Transform template, RagdollPart partRoot, RagdollPart templatePartRoot)
        {
            // Find the difference between the part's and template's position
            Vector3 translation = partRoot.transform.position - templatePartRoot.transform.position;

            for (int i = part.childCount - 1; i >= 0; i--)
            {
                Transform partChild = part.GetChild(i);
                Transform templateChild = template.GetChild(i);

                // Add that difference to the part template's world position
                // and set that as the position of the part
                if (partChild.transform.localPosition != Vector3.zero)
                    partChild.transform.position = templateChild.transform.position + translation;

                if (partChild.transform.rotation != Quaternion.identity)
                    partChild.transform.rotation = templateChild.rotation;

                CorrectPartChildColliders(partChild, templateChild, partRoot, templatePartRoot);
                // This will happen occasionally as `CorrectPartChildColliders` will destroy some GameObjects
                if (partChild == null)
                    continue;

                // Loop through entire hierarchy recursively
                CorrectPartChildren(partChild, templateChild, partRoot, templatePartRoot);
            }
        }

        private void CorrectPartChildColliders(Transform partChild, Transform templateChild, RagdollPart partRoot, RagdollPart templatePartRoot)
        {
            Vector3 translation = partRoot.transform.position - templatePartRoot.transform.position;

            Collider[] partColliders = partChild.GetComponents<Collider>();
            Collider[] templateColliders = templateChild.GetComponents<Collider>();
            for (int k = 0; k < partColliders.Length; k++)
            {
                Collider partCollider = partColliders[k];
                Collider templateCollider = templateColliders[k];

                // Fingers have to be treated specially because they have a 
                // bone on the rig, but have no ragdoll part
                RagdollHand partHand = partRoot as RagdollHand;

                Transform fingerBone = null;
                Transform templateFingerBone = null;
                if (partHand != null)
                {
                    bool isValidBone = GetFingerBoneFromName(animator, partCollider.name, partHand.side, out fingerBone);

                    if (fingerBone != null)
                    {
                        GetFingerBoneFromName(templateAnimator, partCollider.name, partHand.side, out templateFingerBone);

                        translation = fingerBone.position - templateFingerBone.position;
                        partChild.SetPositionAndRotation(fingerBone.position, fingerBone.rotation);
                    }
                    else if (isValidBone)
                    {
                        // If the creature doesn't have a finger bone, then delete
                        // it and its children.
                        UnityEngine.Object.DestroyImmediate(partChild.gameObject);
                        continue;
                    }
                }

                // Fix collider centers
                CorrectPartChildCollidersPositions(partCollider, templateCollider, translation);

                // Then fix everything else
                Transform partMeshBone;
                Transform templateMeshBone;
                if (fingerBone != null)
                {
                    partMeshBone = fingerBone;
                    templateMeshBone = templateFingerBone;
                }
                else
                {
                    partMeshBone = partRoot.meshBone;
                    templateMeshBone = templatePartRoot.meshBone;
                }

                // Get children that are human bones
                Transform[] templateMeshChildren = templateMeshBone
                    .Cast<Transform>()
                    .Where(child => templateToCreatureBones.ContainsKey(child))
                    .ToArray();

                // The chest and upper is a special case, since it must be about as wide as the hips
                // Which is much wider than their children bones
                if (partRoot.meshBone == animator.GetBoneTransform(HumanBodyBones.Chest)
                    || partRoot.meshBone == animator.GetBoneTransform(HumanBodyBones.UpperChest))
                {
                    // The shoulders are just too hard to get right, so axe them
                    if (partCollider.name == "ShoulderLeft" || partCollider.name == "ShoulderRight")
                    {
                        UnityEngine.Object.DestroyImmediate(partCollider.gameObject);
                        continue;
                    }

                    CapsuleCollider chestCapsule = partCollider as CapsuleCollider;
                    chestCapsule.height = hipsCollider.height;
                    chestCapsule.radius = hipsCollider.radius * 0.75f;
                }
                else if (templateMeshChildren.Length == 0)
                    EditNoChildPartCollider(partCollider, templateChild);
                else if (templateMeshChildren.Length == 1)
                    EditOneChildPartCollider(partCollider, templateMeshBone, templateMeshChildren[0]);
                else
                    EditMultipleChildrenPartCollider(partRoot, partCollider, templateMeshChildren);
            }
        }

        private void CorrectPartChildCollidersPositions(Collider partCollider, Collider templateCollider, Vector3 translation)
        {
            // The same process as CorrectPartChildTransforms, but with centers
            // instead of positions (there's no "center" property on the base
            // class of sphere, capsule, and box colliders, which is why I'm
            // using reflection)
            PropertyInfo centerField = partCollider.GetType().GetProperty("center");
            if (centerField == null || centerField.PropertyType != typeof(Vector3))
                return;

            Vector3 templateCenter = (Vector3)centerField.GetValue(templateCollider);
            if (templateCenter == Vector3.zero)
                return;

            Vector3 worldCenter = templateCollider.transform.TransformPoint(templateCenter) + translation;
            centerField.SetValue(partCollider, partCollider.transform.InverseTransformPoint(worldCenter));
        }

        private void EditNoChildPartCollider(Collider partCollider, Transform template)
        {
            // Can't really assume anything about the size of the collider
            // without a child, so just leave size unedited. Center is already
            // corrected, so just the capsule direction is remaining.
            switch (partCollider)
            {
                case CapsuleCollider capsule:
                    Vector3 directionAsVector = Vector3.zero;
                    directionAsVector[capsule.direction] = 1;
                    Vector3 worldDirection = template.transform.TransformDirection(directionAsVector);
                    capsule.direction = GetLongestAxis(partCollider.transform.InverseTransformDirection(worldDirection));
                    break;
            }
        }

        private void EditOneChildPartCollider(Collider partCollider, Transform templateMeshBone, Transform templateChild)
        {
            Transform partMeshBone = templateToCreatureBones[templateMeshBone];
            Transform partChild = templateToCreatureBones[templateChild];
            Vector3 diff = partChild.position - partMeshBone.position;

            Vector3 templateDiff = templateChild.position - templateMeshBone.position;
            float scaleDifference = diff.magnitude / templateDiff.magnitude;
    
            switch (partCollider)
            {
                case BoxCollider box:
                    // Scale box size by ratio of bone length
                    box.size *= scaleDifference;
                    break;

                case CapsuleCollider capsule:
                    // Only correct capsule center because it works better than
                    // letting the other method correct it.
                    capsule.center = partCollider.transform.InverseTransformVector(diff / 2);
                    capsule.direction = GetLongestAxis(capsule.center);
                    capsule.height = partCollider.transform.InverseTransformVector(diff).magnitude;
                    // Make radius some random proportion of bone length
                    capsule.radius = capsule.height * 0.18f;
                    break;

                case SphereCollider sphere:
                    sphere.radius *= scaleDifference;
                    break;
            }
        }

        private void EditMultipleChildrenPartCollider(RagdollPart part, Collider partCollider, Transform[] templateChildren)
        {
            // Create bounds that encapsulates all the children bones
            Bounds bounds = new();
            foreach (Transform templateChild in templateChildren)
            {
                Transform partChild = templateToCreatureBones[templateChild];
                Vector3 localChildPosition = partCollider.transform.InverseTransformPoint(partChild.position);
                bounds.Encapsulate(localChildPosition);
            }
            
            // Resize and position collider based off that
            switch (partCollider)
            {
                case BoxCollider box:
                    box.center = bounds.center;
                    box.size = bounds.size;
                    break;
                case CapsuleCollider capsule:
                    capsule.center = bounds.center;
                    capsule.height = Mathf.Abs(2 * bounds.extents[capsule.direction]);
                 
                    if (part.meshBone == animator.GetBoneTransform(HumanBodyBones.Hips))
                    {
                        // This will be used later for the spine
                        hipsCollider = capsule;

                        // Hips are just very wide
                        capsule.height *= 2;
                    }
                    // The extra 2 is an arbitrary number
                    capsule.radius = capsule.height * 0.4f;
                    break;
                case SphereCollider sphere:
                    break;
            }
        }

        private static bool GetFingerBoneFromName(Animator animator, string name, Side side, out Transform bone)
        {
            bone = null;
            if (!Enum.TryParse(side.ToString() + name, out HumanBodyBones humanBone))
                return false;
            bone = animator.GetBoneTransform(humanBone);
            return true;
        }

        private static Transform GetClosestHumanBone(Animator animator, HumanBodyBones bone)
        {
            Transform boneTransform = animator.GetBoneTransform(bone);

            if (boneTransform != null)
                return boneTransform;

            switch (bone)
            {
                case HumanBodyBones.Chest:
                    // If there is no chest, return a spine (since there can't be an
                    // upper chest either)
                    return animator.GetBoneTransform(HumanBodyBones.Spine);
                case HumanBodyBones.UpperChest:
                    // If there's no upper chest, return chest if it exists
                    // else return spine
                    Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                    if (chest != null)
                        return chest;
                    else
                        return animator.GetBoneTransform(HumanBodyBones.Spine);
                case HumanBodyBones.Neck:
                    return animator.GetBoneTransform(HumanBodyBones.Head);
                default:
                    return null;
            }
        }

        private static int GetLongestAxis(Vector3 vector)
        {
            int axis = 0;
            if (Mathf.Abs(vector[axis]) < Mathf.Abs(vector.y))
                axis = 1;
            if (Mathf.Abs(vector[axis]) < Mathf.Abs(vector.z))
                axis = 2;

            return axis;
        }

        private static Vector3 GetAsLongestAxis(Vector3 vector)
        {
            int axis = GetLongestAxis(vector);
            Vector3 result = Vector3.zero;
            result[axis] = Mathf.Sign(vector[axis]);
            return result;
        }
    }
}
