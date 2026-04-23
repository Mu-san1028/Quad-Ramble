using UnityEngine;
using UnityEngine.InputSystem;


/// <summary>
/// プレイヤーの移動処理とか
/// </summary>
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    private CharacterController controller;
    private Vector2 moveInput;

    [SerializeField] private float gravity = -9.8f;
    private Vector3 velocity;

    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    // WASD入力があるたびに自動で呼ばれる
    void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    void Update()
    {
        Vector3 moveDirection = new Vector3(moveInput.x, 0f, moveInput.y);

        // Character Controllerを使ってキャラクターを動かす
        controller.Move(moveDirection * moveSpeed * Time.deltaTime);

        // 地面に触れているか判定
        if (controller.isGrounded && velocity.y < 0)
        {
            // 地面に着いていたら落下速度をリセット
            // 完全に0ではなく少しマイナスにしておくと、坂道等でガタガタしにくくなるらしい
            velocity.y = -2f; 
        }
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
}    

