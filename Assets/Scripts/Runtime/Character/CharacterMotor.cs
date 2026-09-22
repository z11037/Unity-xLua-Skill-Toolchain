using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public sealed class CharacterMotor : MonoBehaviour
{
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float groundedVerticalSpeed = -2f;

    private CharacterController characterController;
    private CharacterRuntime runtime;

    private Vector3 desiredMoveDirection;
    private float verticalSpeed;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    public void BindRuntime(CharacterRuntime characterRuntime)
    {
        runtime = characterRuntime;
    }

    public void SetMoveDirection(Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude > 1f)
        {
            direction.Normalize();
        }

        desiredMoveDirection = direction;
    }

    public void Tick(float deltaTime)
    {
        if (runtime == null || runtime.IsDead)
        {
            return;
        }

        UpdateVerticalSpeed(deltaTime);

        Vector3 velocity = desiredMoveDirection * runtime.MoveSpeed.Value;
        velocity.y = verticalSpeed;

        characterController.Move(velocity * deltaTime);

        UpdateRotation();
    }

    private void UpdateVerticalSpeed(float deltaTime)
    {
        if (characterController.isGrounded && verticalSpeed < 0f)
        {
            verticalSpeed = groundedVerticalSpeed;
            return;
        }

        verticalSpeed += gravity * deltaTime;
    }

    private void UpdateRotation()
    {
        if (desiredMoveDirection.sqrMagnitude <= 0.001f)
        {
            return;
        }

        transform.forward = desiredMoveDirection;
    }

    public void Stop()
    {
        desiredMoveDirection = Vector3.zero;
    }
}