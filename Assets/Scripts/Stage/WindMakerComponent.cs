using UnityEngine;
using System.Collections.Generic;

public class WindMakerComponent : MonoBehaviour
{
    //?ｿｽv?ｿｽ?ｿｽ?ｿｽC?ｿｽ?ｿｽ?ｿｽ[?ｿｽﾉゑｿｽ?ｿｽ?ｿｽ?ｿｽ?ｿｽ?ｿｽ?ｿｽ?ｿｽ?ｿｽx?ｿｽ?ｿｽ?ｿｽ?ｿｽ?ｿｽ
    [SerializeField] private Vector3 accelration;

    //現在の風が当たっているプレイヤーのリスト
    private List<PlayerController> windList = new List<PlayerController>(); 

    //?ｿｽﾍ囲難ｿｽ?ｿｽﾉ難ｿｽ?ｿｽ?ｿｽ?ｿｽﾄゑｿｽ?ｿｽ?ｿｽ?ｿｽv?ｿｽ?ｿｽ?ｿｽC?ｿｽ?ｿｽ?ｿｽ[?ｿｽ?ｿｽPlayerController?ｿｽ?ｿｽ?ｿｽ?ｿｽ?ｿｽX?ｿｽg?ｿｽﾉ会ｿｽ?ｿｽ?ｿｽ?ｿｽ?ｿｽ
    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            windList.Add(other.GetComponent<PlayerController>());
        }
    }

    //?ｿｽﾍ囲外?ｿｽﾉ難ｿｽ?ｿｽ?ｿｽ?ｿｽﾄゑｿｽ?ｿｽ?ｿｽ?ｿｽv?ｿｽ?ｿｽ?ｿｽC?ｿｽ?ｿｽ?ｿｽ[?ｿｽ?ｿｽPlayerController?ｿｽ?ｿｽ?ｿｽ?ｿｽ?ｿｽX?ｿｽg?ｿｽ?ｿｽ?ｿｽ迴懶ｿｽO
    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            windList.Remove(other.GetComponent<PlayerController>());
        }
    }

    //?ｿｽ?ｿｽ?ｿｽt?ｿｽ?ｿｽ?ｿｽ[?ｿｽ?ｿｽ?ｿｽ?ｿｽ?ｿｽX?ｿｽg?ｿｽﾉゑｿｽ?ｿｽ?ｿｽv?ｿｽ?ｿｽ?ｿｽC?ｿｽ?ｿｽ?ｿｽ[?ｿｽﾉ会ｿｽ?ｿｽ?ｿｽ?ｿｽx?ｿｽ?ｿｽ^?ｿｽ?ｿｽ?ｿｽ?ｿｽ
    void FixedUpdate()
    {
        for (int i = 0; i < windList.Count; i++)
        {

        }
    }
}
