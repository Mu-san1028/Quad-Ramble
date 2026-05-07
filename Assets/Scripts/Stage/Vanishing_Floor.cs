using UnityEngine;
using System.Collections;

public class Vanishing_Floor : MonoBehaviour
{
    //
    private float waitingTime = 3f;

    //TODO 仮置きで消滅に3秒の遅延を設けた。外部からフィールド変数WaitingTimeを操作することで変更可
    
    public float WaitingTime
    {
        get { return waitingTime; }
        set { waitingTime = value; }
    }
    [SerializeField] private Material SwappedMaterial;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }



    // Update is called once per frame
    void Update()
    {
        
    }
    public void FloorVanish()
    {
        StartCoroutine(DestroyDelayProcess());

    }
    //3秒後
    private IEnumerator DestroyDelayProcess()
    {
        Debug.Log("消滅処理開始: " + Time.time);
        //消滅処理が開始すると同時に床のマテリアルをSwappedMaterialに変更する
        this.gameObject.GetComponent<Renderer>().material = SwappedMaterial;
        yield return new WaitForSeconds(waitingTime);  // ← 一定時間待つ

        Debug.Log("消滅処理完了 " + Time.time);
        Destroy(this.gameObject);
    }
}
