using UnityEngine;
using System.Collections;

public class Rotation : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = 100f;
    [SerializeField] private Transform rotationAxis;
    void Start()
    {
        if (rotationAxis == null)
        {
            rotationAxis = transform; 
        }
    }
    // Update is called once per frame
    void Update()
    {
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
    }

    //即座に回転速度を設定するメソッド
    public void SetRotationSpeedInstant(float speed)
    {
        rotationSpeed = speed;
    }
    
    //徐々に回転速度を変化させるメソッド
    public void SetRotationSpeedGradually(float targetSpeed, float duration)
    {
        StartCoroutine(RotateGradually(targetSpeed, duration));
    }

    //徐々に回転速度を変化させるコルーチン
    private IEnumerator RotateGradually(float targetSpeed, float duration)
    {
        float initialSpeed = rotationSpeed;
        float elapsedTime = 0f;

        //徐々に回転速度を変化させるループ
        while (elapsedTime < duration)
        {
            rotationSpeed = Mathf.Lerp(initialSpeed, targetSpeed, elapsedTime / duration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        rotationSpeed = targetSpeed; 
    }

    public void SetRotationAxis(Transform axis)
    {
        rotationAxis = axis;
    }
}
