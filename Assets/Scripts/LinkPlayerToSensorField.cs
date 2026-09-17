using UnityEngine;

public class LinkPlayerToObject : MonoBehaviour, IInteractable
{
    [SerializeField] private BombSensorField[] bombSensorFields; // リンクするオブジェクトを指定するためのフィールド

    public void Interact(PlayerController player)
    {
        // プレイヤーをリンクさせる
        foreach (var bombSensorField in bombSensorFields)
        {
            bombSensorField.SetPlayerController(player);
        }
    }
}
