using System.Collections;
using UnityEngine;

namespace RecycleVision
{
    public class RobotSorterArmController : MonoBehaviour
    {
        public SortingStationManager manager;
        public Transform ikTarget;
        public Transform carryAnchor;
        public float moveSpeed = 2.6f;
        public float hoverHeight = 0.65f;
        public float gripHeightOffset = 0.04f;
        public float settleDelay = 0.08f;
        public float minMoveDuration = 0.18f;
        public float maxMoveDuration = 1.4f;
        public AnimationCurve moveEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private Coroutine activeRoutine;
        private Vector3 homePosition;
        private bool hasHomePosition;

        public bool IsBusy => activeRoutine != null;

        private void Awake()
        {
            EnsureRigReferences(true);
        }

        public bool TrySort(WasteItem item, SortingBin targetBin)
        {
            EnsureRigReferences(false);

            if (item == null || targetBin == null || targetBin.DropAnchor == null || ikTarget == null || carryAnchor == null || IsBusy)
            {
                return false;
            }

            activeRoutine = StartCoroutine(SortRoutine(item, targetBin));
            return true;
        }

        private IEnumerator SortRoutine(WasteItem item, SortingBin targetBin)
        {
            if (item == null)
            {
                activeRoutine = null;
                yield break;
            }

            Vector3 pickupPoint = GetPickupPoint(item);
            Vector3 pickupHover = pickupPoint + Vector3.up * hoverHeight;
            Vector3 dropPoint = targetBin.DropAnchor.position + Vector3.up * 0.18f;
            Vector3 dropHover = dropPoint + Vector3.up * hoverHeight;

            yield return MoveTargetTo(pickupHover);
            if (item == null)
            {
                activeRoutine = null;
                yield break;
            }
            yield return MoveTargetTo(pickupPoint);
            if (item == null)
            {
                activeRoutine = null;
                yield break;
            }

            AttachItem(item);
            yield return new WaitForSeconds(settleDelay);

            if (item == null)
            {
                activeRoutine = null;
                yield break;
            }

            yield return MoveTargetTo(pickupHover);
            yield return MoveTargetTo(dropHover);
            yield return MoveTargetTo(dropPoint);

            if (item == null)
            {
                activeRoutine = null;
                yield break;
            }

            DetachItem(item);
            if (item != null)
            {
                manager?.HandleItemDroppedInBin(targetBin, item);
            }
            yield return new WaitForSeconds(settleDelay);

            yield return MoveTargetTo(dropHover);
            yield return MoveTargetTo(homePosition);
            activeRoutine = null;
        }

        private void EnsureRigReferences(bool refreshHomePosition)
        {
            if (ikTarget == null)
            {
                ikTarget = FindTransformByName("IK Target");
            }

            if (ikTarget == null)
            {
                GameObject target = new GameObject("IK Target");
                target.transform.SetParent(transform, false);
                target.transform.localPosition = new Vector3(0f, 1.6f, 0f);
                target.transform.localRotation = Quaternion.identity;
                ikTarget = target.transform;
            }

            if (carryAnchor == null)
            {
                Transform existingAnchor = FindTransformByName("CarryAnchor");

                if (existingAnchor != null)
                {
                    carryAnchor = existingAnchor;
                }
            }

            if (carryAnchor == null)
            {
                GameObject anchor = new GameObject("CarryAnchor");
                carryAnchor = anchor.transform;
            }

            carryAnchor.SetParent(ikTarget, false);
            carryAnchor.localPosition = new Vector3(0f, -0.08f, 0f);
            carryAnchor.localRotation = Quaternion.identity;

            if (refreshHomePosition || !hasHomePosition)
            {
                homePosition = ikTarget.position;
                hasHomePosition = true;
            }
        }

        private Transform FindTransformByName(string targetName)
        {
            Transform[] transforms = GetComponentsInChildren<Transform>(true);

            for (int index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name == targetName)
                {
                    return transforms[index];
                }
            }

            return null;
        }

        private IEnumerator MoveTargetTo(Vector3 targetPosition)
        {
            Vector3 startPosition = ikTarget.position;
            float distance = Vector3.Distance(startPosition, targetPosition);

            if (distance <= 0.001f)
            {
                ikTarget.position = targetPosition;
                yield break;
            }

            float speed = Mathf.Max(moveSpeed, 0.01f);
            float duration = Mathf.Clamp(distance / speed, minMoveDuration, maxMoveDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                t = moveEase != null ? moveEase.Evaluate(t) : t;
                ikTarget.position = Vector3.Lerp(startPosition, targetPosition, t);
                elapsed += Time.deltaTime;
                yield return null;
            }

            ikTarget.position = targetPosition;
        }

        private Vector3 GetPickupPoint(WasteItem item)
        {
            Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);

            if (renderers.Length == 0)
            {
                return item.transform.position + Vector3.up * gripHeightOffset;
            }

            Bounds bounds = renderers[0].bounds;

            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds.center + Vector3.up * gripHeightOffset;
        }

        private void AttachItem(WasteItem item)
        {
            if (item == null)
            {
                return;
            }

            item.SetHeld(true);
            item.transform.SetParent(carryAnchor, true);
            item.transform.position = carryAnchor.position;
        }

        private void DetachItem(WasteItem item)
        {
            if (item == null)
            {
                return;
            }

            item.transform.SetParent(null, true);
            item.SetHeld(false);
        }
    }
}
