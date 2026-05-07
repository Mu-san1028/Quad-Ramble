using UnityEngine;
using System.Collections;

public class Vanishing_Floor_Sensor : MonoBehaviour
{
    Vanishing_Floor VFScript;
    bool isSwitchedOn;
    private void Start()
    {
         VFScript = this.gameObject.GetComponentInChildren<Vanishing_Floor>();
    }
    private void OnTriggerEnter(Collider collider)
    {
        if (!isSwitchedOn)
        {
            Debug.Log("<color=yellow>落ちる床が何かに接触しました。</color>");
            if (collider.gameObject.CompareTag("Player"))
            {
                VFScript.FloorVanish();
                //子オブジェクトの1秒後に消滅させる・二重軌道の防止
                isSwitchedOn = true;
                StartCoroutine(DestroyDelayProcess());
            }
        }
    }
    private IEnumerator DestroyDelayProcess()
    {
        Debug.Log("センサー消滅処理開始: " + Time.time);

        yield return new WaitForSeconds(VFScript.WaitingTime+1f);  // ← 一定時間＋1秒待つ

        Debug.Log("センサー消滅処理完了 " + Time.time);
        Destroy(this.gameObject);
    }
}
