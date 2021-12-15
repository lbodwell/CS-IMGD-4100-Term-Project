using System;
using UnityEngine;

public class PlayerController : MonoBehaviour {
    public CharacterController controller;
    public Transform cam;
    public GameObject nearestDownwardHole;
    public GameObject nearestUpwardHole;
    public Vector3 velocity = new Vector3(0f, 0f, 0f);
    public float movementSpeed = 2f;
    public float turnSmoothingTime = 0.1f;
    public int currentFloor = 5;
    public bool isJumping;

    private const float Gravity = 0.08f;
    private float _turnSmoothingVel;

    private void Update() {
        var shortestHoleDist = float.MaxValue;
        nearestDownwardHole = null;
        nearestUpwardHole = null;
        foreach (var hole in HoleManager.Instance.holes) {
            var holeCollider = hole.GetComponent<HoleCollider>();
            var holePos = holeCollider.transform.position;
            var holeDist = Vector3.Distance(holePos, transform.position);
            if (holeDist < shortestHoleDist && hole.GetComponent<HoleCollider>().floorNumber == currentFloor) {
                shortestHoleDist = holeDist;
                nearestDownwardHole = hole;
            }
        }
        
        var horizInput = Input.GetAxisRaw("Horizontal");
        var vertInput = Input.GetAxisRaw("Vertical");
        var inputDirection = new Vector3(horizInput, 0f, vertInput).normalized;

        if (Input.GetKeyDown(KeyCode.Space) && controller.isGrounded) {
            if (Vector3.Distance(transform.position, nearestUpwardHole.transform.position) < 10) {
                
            }
            velocity.y = 12f;
            isJumping = true;
        }

        velocity.x = movementSpeed;
        velocity.z = movementSpeed;

        if (!controller.isGrounded) {
            velocity.y -= Gravity;
        }

        Vector3 finalVel;

        if (inputDirection.magnitude < 0.1f) {
            finalVel = new Vector3(0f, velocity.y, 0f);
        } else {
            var targetAngle = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + cam.eulerAngles.y;
            var smoothedAngle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref _turnSmoothingVel,
                turnSmoothingTime);
            transform.rotation = Quaternion.Euler(0f, smoothedAngle, 0f);

            var playerDirection = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            finalVel = new Vector3(velocity.x * playerDirection.x, playerDirection.y + velocity.y,
                velocity.z * playerDirection.z);
        }

        controller.Move(finalVel * Time.deltaTime);

        if (!controller.isGrounded || !isJumping) return;
        isJumping = false;
        velocity.y = 0f;
    }
}