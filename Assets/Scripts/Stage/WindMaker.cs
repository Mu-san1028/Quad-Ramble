using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// �����N�����I�u�W�F�N�g�ɃA�^�b�`
/// - �G�ꂽ�v���C���[�ɉ����x��^����
/// </summary>
public class WindMaker : MonoBehaviour
{
    //�v���C���[�ɂ���������x�����
    [SerializeField] private Vector3 accelration;

    //���݂̕����������Ă���v���C���[�̃��X�g
    private List<PlayerController> windList = new List<PlayerController>(); 

    //�͈͓��ɓ����Ă����v���C���[��PlayerController�����X�g�ɉ�����
    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            windList.Add(other.GetComponent<PlayerController>());
        }
    }

    //�͈͊O�ɓ����Ă����v���C���[��PlayerController�����X�g���珜�O
    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            windList.Remove(other.GetComponent<PlayerController>());
        }
    }

    //���t���[�����X�g�ɂ���v���C���[�ɉ����x��^����
    void FixedUpdate()
    {
        for (int i = 0; i < windList.Count; i++)
        {

        }
    }
}
