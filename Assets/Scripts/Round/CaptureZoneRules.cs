public enum CaptureZoneState : byte { Neutral, Occupied, Contested, Inactive }

public static class CaptureZoneRules
{
    public static bool Contains(float x, float y, float z, float radius, float below, float above)
        => y >= -below && y <= above && x * x + z * z <= radius * radius;
}

public static class CaptureZoneScheduleRules
{
    public static int PhaseAt(float remaining, float[] thresholds)
    {
        if (remaining <= 0 || thresholds == null || thresholds.Length == 0)
            return -1;

        int phase = 0;

        for (int i = 1; i < thresholds.Length; i++)
        {
            if (remaining <= thresholds[i])
                phase = i;
        }

        return phase;
    }

    public static bool ShouldAnnounce(float secondsUntilChange)
    {
        return secondsUntilChange > 0 && secondsUntilChange <= 5f;
    }

    public static bool ArrowVisible(double secondsSinceChange)
    {
        return secondsSinceChange >= 0 && secondsSinceChange < 5d;
    }
}

/// <summary>単独占領の連続区間だけ端数を保持する。</summary>
public sealed class CaptureZoneScoreClock
{
    private ulong owner;
    private bool active;
    private double carry;
    public void Reset() { active = false; carry = 0; }
    public int Step(bool occupied, ulong clientId, float rate, float deltaTime)
    {
        if (!occupied) { Reset(); return 0; }
        if (!active || owner != clientId) carry = 0;
        active = true; owner = clientId;
        if (rate <= 0 || deltaTime <= 0 || float.IsNaN(rate) || float.IsNaN(deltaTime)
            || float.IsInfinity(rate) || float.IsInfinity(deltaTime)) return 0;
        carry += (double)rate * deltaTime;
        int points = (int)System.Math.Min(int.MaxValue, System.Math.Floor(carry));
        carry -= points;
        return points;
    }
}

