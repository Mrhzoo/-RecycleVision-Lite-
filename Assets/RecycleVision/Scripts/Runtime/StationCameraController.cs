using UnityEngine;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RecycleVision
{
    public class StationCameraController : MonoBehaviour
    {
        public Camera playerCamera;
        public Transform holdAnchor;
        public LayerMask grabbableMask = ~0;
        public float moveSpeed = 4.6f;
        public float lookSensitivity = 0.11f;
        public float holdDistance = 2.1f;
        public float minHoldDistance = 1.2f;
        public float maxHoldDistance = 3.2f;
        public float rotateStep = 45f;
        public Vector2 xBounds = new Vector2(-3.8f, 3.8f);
        public Vector2 zBounds = new Vector2(-5.6f, 1.6f);
        public bool allowItemGrab = true;

        private WasteItem heldItem;
        private float yaw;
        private float pitch;

        private void Start()
        {
            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>();
            }

            Vector3 euler = transform.eulerAngles;
            yaw = euler.y;
            pitch = euler.x;
        }

        private void Update()
        {
            HandleMovement();
            HandleLook();
            HandleItemInteraction();
        }

        private void HandleMovement()
        {
            Vector2 moveInput = GetMoveInput();
            Vector3 right = transform.right;
            right.y = 0f;
            right.Normalize();

            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward.Normalize();

            Vector3 delta = (forward * moveInput.y + right * moveInput.x) * (moveSpeed * Time.deltaTime);
            Vector3 nextPosition = transform.position + delta;
            nextPosition.x = Mathf.Clamp(nextPosition.x, xBounds.x, xBounds.y);
            nextPosition.z = Mathf.Clamp(nextPosition.z, zBounds.x, zBounds.y);
            transform.position = nextPosition;
        }

        private void HandleLook()
        {
            if (!IsLookHeld())
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                return;
            }

            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;

            Vector2 lookDelta = GetLookDelta() * lookSensitivity;
            yaw += lookDelta.x;
            pitch = Mathf.Clamp(pitch - lookDelta.y, -18f, 42f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void HandleItemInteraction()
        {
            if (heldItem != null && (!heldItem.gameObject.activeInHierarchy || heldItem.IsResolved))
            {
                heldItem.SetHeld(false);
                heldItem = null;
            }

            if (WasLeftPressedThisFrame() && !IsPointerOverUi())
            {
                if (allowItemGrab && heldItem == null)
                {
                    TryGrabItem();
                }
            }

            if (WasLeftReleasedThisFrame() && heldItem != null)
            {
                ReleaseHeldItem();
            }

            if (heldItem == null)
            {
                return;
            }

            float scrollDelta = GetScrollDelta();

            if (Mathf.Abs(scrollDelta) > 0.01f)
            {
                holdDistance = Mathf.Clamp(holdDistance + (scrollDelta * 0.02f), minHoldDistance, maxHoldDistance);
            }

            if (WasRotatePressedThisFrame())
            {
                heldItem.RotateBy(rotateStep);
            }

            if (holdAnchor != null)
            {
                holdAnchor.localPosition = new Vector3(0f, -0.1f, holdDistance);
                heldItem.transform.position = Vector3.Lerp(
                    heldItem.transform.position,
                    holdAnchor.position,
                    Time.deltaTime * 18f);
            }
        }

        private void TryGrabItem()
        {
            if (playerCamera == null)
            {
                return;
            }

            Ray ray = playerCamera.ScreenPointToRay(GetPointerScreenPosition());

            if (!Physics.Raycast(ray, out RaycastHit hit, 12f, grabbableMask, QueryTriggerInteraction.Collide)
                && !Physics.SphereCast(ray, 0.14f, out hit, 12f, grabbableMask, QueryTriggerInteraction.Collide))
            {
                return;
            }

            WasteItem item = hit.collider.GetComponentInParent<WasteItem>();

            if (item == null || item.IsResolved)
            {
                return;
            }

            heldItem = item;
            heldItem.SetHeld(true);
        }

        private void ReleaseHeldItem()
        {
            heldItem.SetHeld(false);
            heldItem = null;
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private static Vector2 GetMoveInput()
        {
#if ENABLE_INPUT_SYSTEM
            Vector2 input = Vector2.zero;
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
            {
                return input;
            }

            if (keyboard.wKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed) input.x -= 1f;

            return input.normalized;
#else
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;
#endif
        }

        private static Vector2 GetLookDelta()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
        }

        private static bool IsLookHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
            return Input.GetMouseButton(1);
#endif
        }

        private static bool WasLeftPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        private static bool WasLeftReleasedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
#else
            return Input.GetMouseButtonUp(0);
#endif
        }

        private static bool WasRotatePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.R);
#endif
        }

        private static float GetScrollDelta()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
#else
            return Input.mouseScrollDelta.y;
#endif
        }

        private static Vector2 GetPointerScreenPosition()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }
    }
}
