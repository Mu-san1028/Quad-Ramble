using UnityEngine;

public class Bounce : MonoBehaviour
{
    [SerializeField] private float bounceRatio = 1f;
    private enum collisionType { Trigger, Collision }
    private collisionType collisionDetection = collisionType.Collision;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Colliderコンポーネントを取得して、isTriggerの状態に応じてcollisionDetectionを設定
        Collider collider = GetComponent<Collider>();
        if (collider != null)
        {
            if (collider.isTrigger)
            {
                collisionDetection = collisionType.Trigger;
            }
            else
            {
                collisionDetection = collisionType.Collision;
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    //Triggerを使うかCollisionを使うかわからないので一応両方実装しておく
    void OnTriggerEnter(Collider other)
    {
        if (collisionDetection != collisionType.Trigger) return;

        if (other.CompareTag("Player"))
        {
            Rigidbody playerRigidbody = other.GetComponent<Rigidbody>();
            if (playerRigidbody != null)
            {
                // プレイヤーの速度を取得
                Vector3 playerVelocity = playerRigidbody.linearVelocity;

                // 速度の大きさを計算
                float speed = playerVelocity.magnitude;

                // 速度の大きさに応じて反発力を調整
                float bounceForce = speed * bounceRatio;

                //　反発力の方向
                Vector3 bounceDirection = (other.transform.position - transform.position).normalized;

                // 上方向に反発力を加える
                playerRigidbody.AddForce(bounceDirection * bounceForce, ForceMode.Impulse);
            }
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collisionDetection != collisionType.Collision) return;
        
        if (collision.gameObject.CompareTag("Player"))
        {
            Rigidbody playerRigidbody = collision.gameObject.GetComponent<Rigidbody>();
            if (playerRigidbody != null)
            {
                // プレイヤーの速度を取得
                Vector3 playerVelocity = playerRigidbody.linearVelocity;

                // 速度の大きさを計算
                float speed = playerVelocity.magnitude;

                // 速度の大きさに応じて反発力を調整
                float bounceForce = speed * bounceRatio;

                // 反発力の方向
                Vector3 bounceDirection = (collision.transform.position - transform.position).normalized;

                // 上方向に反発力を加える
                playerRigidbody.AddForce(bounceDirection * bounceForce, ForceMode.Impulse);
            }
        }
    }

}
