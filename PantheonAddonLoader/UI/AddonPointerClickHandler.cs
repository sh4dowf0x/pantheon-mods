using UnityEngine;

namespace PantheonAddonLoader.UI;

public class AddonPointerClickHandler : MonoBehaviour
{
    private static bool _rightClickThisFrame;
    private static int _checkedFrame = -1;
    private RectTransform? _rectTransform;

    public Action? RightClicked { get; set; }

    public AddonPointerClickHandler(IntPtr ptr) : base(ptr)
    {
    }

    public void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
    }

    public void Update()
    {
        var frame = Time.frameCount;
        if (_checkedFrame != frame)
        {
            _checkedFrame = frame;
            _rightClickThisFrame = Input.GetMouseButtonDown(1);
        }

        if (!_rightClickThisFrame)
        {
            return;
        }

        _rectTransform ??= GetComponent<RectTransform>();
        if (_rectTransform != null && RectTransformUtility.RectangleContainsScreenPoint(_rectTransform, Input.mousePosition))
        {
            RightClicked?.Invoke();
        }
    }
}
