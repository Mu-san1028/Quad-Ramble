using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public class ButtonSelectionArrow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Vector2 arrowSize = new Vector2(16f, 22f);
    [SerializeField] private Vector2 offset = new Vector2(8f, 0f);
    [SerializeField] private Color arrowColor = Color.white;
    [SerializeField] private Color outlineColor = new Color(0.08f, 0.12f, 0.22f, 1f);
    [SerializeField, Min(0f)] private float movement = 3f;
    [SerializeField, Min(0f)] private float frequency = 1.5f;

    private Button button;
    private RectTransform arrow;
    private bool hovered;
    private float elapsed;

    private void Awake()
    {
        this.button = this.GetComponent<Button>();
        GameObject visual = new GameObject("SelectionArrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(SelectionArrowGraphic));
        visual.layer = this.gameObject.layer;
        this.arrow = (RectTransform)visual.transform;
        this.arrow.SetParent(this.transform, false);
        this.arrow.anchorMin = this.arrow.anchorMax = new Vector2(0f, 0.5f);
        this.arrow.pivot = new Vector2(0f, 0.5f);
        this.arrow.sizeDelta = this.arrowSize;

        SelectionArrowGraphic graphic = visual.GetComponent<SelectionArrowGraphic>();
        graphic.color = this.arrowColor;
        graphic.OutlineColor = this.outlineColor;
        graphic.raycastTarget = false;
        visual.SetActive(false);
    }

    private void LateUpdate()
    {
        bool selected = EventSystem.current != null
            && EventSystem.current.currentSelectedGameObject == this.gameObject;
        bool visible = this.button.isActiveAndEnabled && this.button.IsInteractable()
            && (selected || this.hovered);

        this.arrow.gameObject.SetActive(visible);
        if (!visible)
        {
            this.elapsed = 0f;
            return;
        }

        this.elapsed += Time.unscaledDeltaTime;
        this.arrow.anchoredPosition = this.offset + Vector2.right
            * (Mathf.Sin(this.elapsed * this.frequency * Mathf.PI * 2f) * this.movement);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        this.hovered = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        this.hovered = false;
    }

    private void OnDisable()
    {
        this.hovered = false;
        this.elapsed = 0f;
        if (this.arrow != null) this.arrow.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (this.arrow != null) Destroy(this.arrow.gameObject);
    }
}
