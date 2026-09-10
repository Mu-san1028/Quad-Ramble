using UnityEngine;

/// <summary>占領状態を読むだけの表示。得点やNetworkVariableを書き換えない。</summary>
public sealed class CaptureZoneVisual : MonoBehaviour
{
    [SerializeField] private Renderer ringRenderer;
    [SerializeField] private Color contestedColor = new(1f, .7f, .22f);
    [SerializeField, Min(.1f)] private float pulsePeriod = 1.8f;
    [SerializeField] private Color[] playerColors = {
        new(.25f,.75f,1f), new(1f,.4f,.45f), new(.55f,.85f,.3f), new(.8f,.5f,1f)
    };
    private CaptureZone zone;
    private Color neutral, fallback;
    private MaterialPropertyBlock properties;
    private Renderer[] areaRenderers;
    private bool[] originalEnabled;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    public void Bind(CaptureZone source, Renderer legacyRenderer, Color neutralColor, Color occupiedColor)
    {
        Unbind();
        zone = source;
        neutral = neutralColor;
        fallback = occupiedColor;

        if (!ringRenderer) ringRenderer = legacyRenderer;
        if (!ringRenderer)
            foreach (Renderer candidate in source.GetComponentsInChildren<Renderer>(true))
                if (candidate.name == "CZ_OuterRing") { ringRenderer = candidate; break; }
        if (!ringRenderer) Debug.LogWarning("CaptureZone: Zone Rendererに外周リングを設定してください。判定と得点は表示なしでも動作します。", source);
        properties ??= new MaterialPropertyBlock();
        var parts = new System.Collections.Generic.List<Renderer>();

        foreach (Renderer candidate in source.GetComponentsInChildren<Renderer>(true))
        {
            if (candidate == ringRenderer || candidate.name == "CZ_InnerFill" || candidate.name == "CZ_CenterMark")
                parts.Add(candidate);
        }

        if (ringRenderer && !parts.Contains(ringRenderer))
            parts.Add(ringRenderer);

        areaRenderers = parts.ToArray();
        originalEnabled = new bool[areaRenderers.Length];

        for (int i = 0; i < areaRenderers.Length; i++)
            originalEnabled[i] = areaRenderers[i].enabled;

        zone.StateChanged += Refresh;
        Refresh(new CaptureZoneSnapshot(zone.State, zone.OccupantClientId));
    }

    public void Unbind()
    {
        if (zone) zone.StateChanged -= Refresh;
        zone = null;
        Paint(neutral);
        SetVisible(true);
    }

    private void OnDestroy() => Unbind();

    private void Update()
    {
        if (!zone || !zone.IsSpawned) return;
        Refresh(new CaptureZoneSnapshot(zone.State, zone.OccupantClientId));
    }

    private void Refresh(CaptureZoneSnapshot state)
    {
        SetVisible(zone && zone.IsAreaActive);

        Color color = neutral;
        if (state.State == CaptureZoneState.Occupied)
        {
            // 識別色を持つ既存データはないため、名簿のIDに4色を割り当てる。
            ulong id = ConnectionContainer.Instance?.GetConnection(state.Owner)?.Id ?? state.Owner;
            color = playerColors != null && playerColors.Length > 0 ? playerColors[(int)(id % (ulong)playerColors.Length)] : fallback;
        }
        else if (state.State == CaptureZoneState.Contested)
        {
            double time = zone && zone.NetworkManager ? zone.NetworkManager.ServerTime.Time : Time.timeAsDouble;
            float phase = (float)(time % Mathf.Max(.1f, pulsePeriod)) / Mathf.Max(.1f, pulsePeriod);
            float brightness = .65f + .35f * (.5f + .5f * Mathf.Sin(phase * Mathf.PI * 2));
            color = contestedColor * brightness;
            color.a = contestedColor.a;
        }
        Paint(color);
    }

    private void SetVisible(bool visible)
    {
        if (areaRenderers == null)
            return;

        for (int i = 0; i < areaRenderers.Length; i++)
        {
            if (areaRenderers[i])
                areaRenderers[i].enabled = visible && originalEnabled[i];
        }
    }

    private void Paint(Color color)
    {
        if (!ringRenderer) return;
        properties ??= new MaterialPropertyBlock();
        ringRenderer.GetPropertyBlock(properties);
        properties.SetColor(ColorId, color);
        properties.SetColor(BaseColorId, color);
        ringRenderer.SetPropertyBlock(properties);
    }
}


