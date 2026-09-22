using UnityEngine;
using UnityEngine.InputSystem;

public sealed class PlayerInputReader : MonoBehaviour
{
    [SerializeField] private Character targetCharacter;
    [SerializeField] private Transform cameraTransform;

    private GameInput input;

    private void Awake()
    {
        input = new GameInput();
    }

    private void OnEnable()
    {
        input.Player.Move.performed += OnMove;
        input.Player.Move.canceled += OnMoveCanceled;

        input.Player.Skill.performed += OnSkill;

        input.Player.Enable();
    }

    private void OnDisable()
    {
        input.Player.Move.performed -= OnMove;
        input.Player.Move.canceled -= OnMoveCanceled;
        input.Player.Skill.performed -= OnSkill;
        input.Player.Disable();

        // 输入组件被关闭时清空移动意图，避免角色继续沿旧方向移动。
        if (targetCharacter != null)
        {
            targetCharacter.SetMoveDirection(Vector3.zero);
        }
    }

    private void OnSkill(InputAction.CallbackContext context)
    {
        if (targetCharacter == null)
        {
            return;
        }

        targetCharacter.RequestTestSkill();
    }

    private void OnMove(InputAction.CallbackContext context)
    {
        if (targetCharacter == null || cameraTransform == null)
        {
            return;
        }

        Vector2 moveInput = context.ReadValue<Vector2>();

        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;

        // 只取相机在水平面上的朝向，避免相机俯仰影响角色移动。
        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        Vector3 moveDirection = forward * moveInput.y + right * moveInput.x;

        if (moveDirection.sqrMagnitude > 1f)
        {
            moveDirection.Normalize();
        }

        targetCharacter.SetMoveDirection(moveDirection);
    }

    private void OnMoveCanceled(InputAction.CallbackContext context)
    {
        if (targetCharacter == null)
        {
            return;
        }

        targetCharacter.SetMoveDirection(Vector3.zero);
    }

    private void OnDestroy()
    {
        input?.Dispose();
    }
}