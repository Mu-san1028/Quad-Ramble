using UnityEngine;

public class Pendulum : MonoBehaviour
{
    [SerializeField] private float maxSwingAngle = 30f; // 最大振れ角度
    [SerializeField] private float swingSpeed = 30f; // 振れ速度
    public enum SwingDirection
    {
        Clockwise,
        CounterClockwise
    }
    [SerializeField] private SwingDirection swingDirection = SwingDirection.Clockwise;
    public enum SwingAxis
    {
        XAxis,
        YAxis,
        ZAxis
    }
    [SerializeField] private SwingAxis swingAxis = SwingAxis.YAxis;
    private float initialRotation;
    void Start()
    {
        initialRotation = transform.eulerAngles.y;
    }
    void Update()
    {
        SwingOnAxis();
    }
    void SwingOnAxis()
    {
        float angle = Mathf.Sin(Time.time * swingSpeed) * maxSwingAngle;
        if (swingDirection == SwingDirection.CounterClockwise)
        {
            angle = -angle;
        }
        switch (swingAxis)
        {
            case SwingAxis.XAxis:
                transform.localRotation = Quaternion.Euler(angle, 0f, 0f);
                break;
            case SwingAxis.YAxis:
                transform.localRotation = Quaternion.Euler(0f, angle, 0f);
                break;
            case SwingAxis.ZAxis:
                transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                break;
        }
    }
}
