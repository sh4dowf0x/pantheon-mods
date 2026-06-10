using Il2CppTMPro;
using PantheonAddonFramework.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PantheonAddonLoader.UI;

public class AddonButtonComponent : IAddonButtonComponent
{
    private readonly Button _button;
    private readonly TextMeshProUGUI _text;
    private readonly RectTransform _rectTransform;

    public AddonButtonComponent(Button button, TextMeshProUGUI text)
    {
        _button = button;
        _text = text;
        _rectTransform = button.GetComponent<RectTransform>();
    }

    public void SetText(string text)
    {
        _text.text = text;
    }

    public void SetSize(float width, float height)
    {
        _rectTransform.sizeDelta = new Vector2(width, height);
    }

    public void SetPosition(float x, float y)
    {
        _rectTransform.anchoredPosition = new Vector2(x, y);
    }

    public void SetFontSize(float fontSize)
    {
        _text.fontSize = fontSize;
    }

    public void Enable(bool enabled)
    {
        _button.gameObject.SetActive(enabled);
    }

    public void Destroy()
    {
        Object.Destroy(_button.gameObject);
    }
}
