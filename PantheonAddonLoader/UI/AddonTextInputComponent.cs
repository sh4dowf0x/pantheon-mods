using Il2CppTMPro;
using PantheonAddonFramework.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PantheonAddonLoader.UI;

public class AddonTextInputComponent : IAddonTextInputComponent
{
    private readonly TMP_InputField _input;
    private readonly TextMeshProUGUI _text;
    private readonly RectTransform _rectTransform;

    public AddonTextInputComponent(TMP_InputField input, TextMeshProUGUI text)
    {
        _input = input;
        _text = text;
        _rectTransform = input.GetComponent<RectTransform>();
    }

    public string GetText() => _input.text ?? "";

    public void SetText(string text) => _input.text = text;

    public void SetSize(float width, float height) => _rectTransform.sizeDelta = new Vector2(width, height);

    public void SetPosition(float x, float y) => _rectTransform.anchoredPosition = new Vector2(x, y);

    public void SetFontSize(float fontSize) => _text.fontSize = fontSize;

    public void Enable(bool enabled) => _input.gameObject.SetActive(enabled);

    public void Destroy() => Object.Destroy(_input.gameObject);
}
