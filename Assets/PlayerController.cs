using UnityEngine;

public class PlayerController : MonoBehaviour {
    public CharacterController controller;
    public Transform cam;
    public float gravity = 0.09f;
    public float movementSpeed = 0.5f;

    public float turnSmoothingTime = 0.1f;

    private Vector3 _velocity = new Vector3(0f, 0f, 0f);
    private bool _isSprinting;
    private float _turnSmoothingVel;
    private Vector2 _movementInput = new Vector2(0, 0);

    private void Update() {
        var horizInput = Input.GetAxisRaw("Horizontal");
        var vertInput = Input.GetAxisRaw("Vertical");
        var inputDirection = new Vector3(horizInput, 0f, vertInput).normalized;
        

        _velocity.x = movementSpeed * (_isSprinting ? 1.5f : 1f);
        _velocity.z = movementSpeed * (_isSprinting ? 1.5f : 1f);

        if (!controller.isGrounded) {
            _velocity.y -= gravity;
        }

        Vector3 finalVel;

        if (inputDirection.magnitude < 0.1f) {
            finalVel = new Vector3(0f, _velocity.y, 0f);
        } else {
            var targetAngle = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + cam.eulerAngles.y;
            var smoothedAngle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref _turnSmoothingVel, turnSmoothingTime);
            transform.rotation = Quaternion.Euler(0f, smoothedAngle, 0f);

            var playerDirection = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            finalVel = new Vector3(_velocity.x * playerDirection.x, playerDirection.y + _velocity.y, _velocity.z * playerDirection.z);
        }

        controller.Move(finalVel * Time.deltaTime);
    }
}