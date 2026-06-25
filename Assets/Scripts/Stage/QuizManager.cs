using UnityEngine;
using System.Collections.Generic;

public class QuizManager : MonoBehaviour
{
    // 全床オブジェクトを保持するリスト
    private List<QuizFloor> floorList = new List<QuizFloor>();

    void Start()
    {
        // シーン内の全てのQuizFloorを取得してリストに格納
        // ※事前に床をプレハブ化している場合でも、シーン上に配置されていれば取得可能です
        QuizFloor[] foundFloors = FindObjectsByType<QuizFloor>(FindObjectsSortMode.None);
        floorList.AddRange(foundFloors);
    }

    /// <summary>
    /// 正解IDを送信し、全床に判定させる
    /// </summary>
    /// <param name="correctID">正解のID</param>
    public void NotifyAnswer(int correctID)
    {
        foreach (QuizFloor floor in floorList)
        {
            if (floor != null)
            {
                floor.CheckAnswer(correctID);
            }
        }
    }
}