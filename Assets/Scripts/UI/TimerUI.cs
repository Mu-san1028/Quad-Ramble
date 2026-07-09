using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class TimerUI : MonoBehaviour
{
    [Header("UI�R���|�[�l���g")]
    public TextMeshProUGUI timerText; // �c�莞�Ԃ̐���
    public Image timerImage;     // �^�C�}�[�̔w�i�摜

    [Header("�^�C�}�[�ݒ�")]
    public float totalTime = 90f;     // �������ԁi�b�j
    private float _currentTime;

    [Header("�J���[�ݒ�")]
    public Color normalColor = Color.gray;  // �ʏ펞
    public Color warningColor = Color.yellow; // �����ȉ�
    public Color criticalColor = Color.red;    // 10�b�ȉ�

    void Start()
    {
        _currentTime = totalTime;   // �������Ԃ̃Z�b�g
    }

    void Update()
    {
        if (_currentTime > 0)
        {
            _currentTime -= Time.deltaTime;
        }
        else
        {
            _currentTime = 0;
            // �^�C���A�b�v���̏����Ƃ�
        }

        DisplayTime(_currentTime);
        UpdateUIColor(_currentTime);
    }

    // ���Ԃ̕\���`��
    void DisplayTime(float timeToDisplay)
    {
        int seconds = Mathf.CeilToInt(timeToDisplay);
        timerText.text = seconds.ToString();
    }

    // �c�莞�Ԃɍ��킹�ĐF�̕ύX
    void UpdateUIColor(float time)
    {
        Color targetColor;

        if (time <= 10f)
        {
            targetColor = criticalColor;
        }
        else if (time <= totalTime / 3)
        {
            targetColor = warningColor;
        }
        else
        {
            targetColor = normalColor;
        }

        // �e�L�X�g�Ɣw�i�摜�̗����̐F���X�V
        if (timerText != null) timerText.color = targetColor;
    }

    public float GetCurrentTime()
    {
        return _currentTime;
    }
}