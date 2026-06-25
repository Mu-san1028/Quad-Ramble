using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 風を起こすオブジェクトにアタッチ
/// - 触れたプレイヤーに加速度を与える
/// </summary>
public class WindMaker : MonoBehaviour
{
    //プレイヤーにかける加速度を入力
    [SerializeField] private Vector3 accelration;

    //現在の風が当たっているプレイヤーのリスト
    private List<PlayerController> windList = new List<PlayerController>(); 

    //範囲内に入ってきたプレイヤーのPlayerControllerをリストに加える
    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            windList.Add(other.GetComponent<PlayerController>());
        }
    }

    //範囲外に入ってきたプレイヤーのPlayerControllerをリストから除外
    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            windList.Remove(other.GetComponent<PlayerController>());
        }
    }

    //毎フレームリストにあるプレイヤーに加速度を与える
    void FixedUpdate()
    {
        for (int i = 0; i < windList.Count; i++)
        {

        }
    }
}
