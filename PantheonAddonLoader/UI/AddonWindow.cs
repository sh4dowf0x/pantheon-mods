using Il2Cpp;
using Il2CppTMPro;
using PantheonAddonFramework.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PantheonAddonLoader.UI;

public class AddonWindow : IAddonWindow
{
    private UIWindowPanel _window;
    private RectTransform _rectTransform;
    
    public AddonWindow(UIWindowPanel window)
    {
        _window = window;
        _rectTransform = window.GetComponent<RectTransform>();
    }

    public float Height => _rectTransform.sizeDelta.y;
    public float Width =>  _rectTransform.sizeDelta.x;
    public float X => _rectTransform.transform.position.x;
    public float Y => _rectTransform.transform.position.y;

    public void SetHeight(float height)
    {
        _rectTransform.sizeDelta = new Vector2(Width, height);
    }

    public void SetWidth(float newWidth)
    {
        _rectTransform.sizeDelta = new Vector2(newWidth, Height);
    }

    public void SetPosition(float newX, float newY)
    {
        _rectTransform.transform.position = new Vector2(newX, newY);
    }

    public IAddonImageComponent AddImageComponent(string objectName)
    {
        var go = new GameObject("Test");
        go.transform.SetParent(_window.transform);

        var imageComponent = go.AddComponent<Image>();

        return new AddonImageComponent(imageComponent, AddonLoader.CustomAssetManager);
    }

    public IAddonTextComponent AddTextComponent(string initialText)
    {
        var go = new GameObject("Test");
        go.transform.SetParent(_window.transform);
        
        var textComponent = go.AddComponent<TextMeshProUGUI>();
        textComponent.text = initialText;
        textComponent.fontSize = 18;
        textComponent.alignment = TextAlignmentOptions.Center;
        
        var rectTransform = go.GetComponent<RectTransform>();
        rectTransform.anchoredPosition = new Vector2(0, 0);
        
        return new AddonTextComponent(textComponent);
    }

    public IAddonButtonComponent AddButtonComponent(string text, Action onClick)
    {
        return AddButtonComponent(text, onClick, () => { });
    }

    public IAddonButtonComponent AddButtonComponent(string text, Action onClick, Action onRightClick)
    {
        var go = new GameObject(text);
        go.transform.SetParent(_window.transform);
        go.layer = Layers.UI;

        var rectTransform = go.AddComponent<RectTransform>();
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = new Vector2(0, 0);
        rectTransform.sizeDelta = new Vector2(120, 32);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.14f, 0.18f, 0.22f, 0.95f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.RemoveAllListeners();
        button.onClick.AddCall(new InvokableCall(new Action(onClick)));

        var clickHandler = go.AddComponent<AddonPointerClickHandler>();
        clickHandler.RightClicked = onRightClick;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform);
        textGo.layer = Layers.UI;

        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0, 0);
        textRect.anchorMax = new Vector2(1, 1);
        textRect.offsetMin = new Vector2(6, 2);
        textRect.offsetMax = new Vector2(-6, -2);

        var textComponent = textGo.AddComponent<TextMeshProUGUI>();
        textComponent.text = text;
        textComponent.fontSize = 15;
        textComponent.alignment = TextAlignmentOptions.Center;
        textComponent.color = Color.white;
        textComponent.raycastTarget = false;

        return new AddonButtonComponent(button, textComponent);
    }

    public IAddonTextInputComponent AddTextInputComponent(string initialText)
    {
        var go = new GameObject("Input");
        go.transform.SetParent(_window.transform);
        go.layer = Layers.UI;

        var rectTransform = go.AddComponent<RectTransform>();
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = new Vector2(0, 0);
        rectTransform.sizeDelta = new Vector2(180, 32);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.05f, 0.06f, 0.07f, 0.95f);

        var input = go.AddComponent<TMP_InputField>();
        input.targetGraphic = image;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform);
        textGo.layer = Layers.UI;

        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0, 0);
        textRect.anchorMax = new Vector2(1, 1);
        textRect.offsetMin = new Vector2(8, 3);
        textRect.offsetMax = new Vector2(-8, -3);

        var textComponent = textGo.AddComponent<TextMeshProUGUI>();
        textComponent.text = initialText;
        textComponent.fontSize = 14;
        textComponent.alignment = TextAlignmentOptions.MidlineLeft;
        textComponent.color = Color.white;

        input.textComponent = textComponent;
        input.text = initialText;

        return new AddonTextInputComponent(input, textComponent);
    }

    // TODO: Set this up from scratch instead of copying from an existing window
    public IAddonWindow AddResizeHandle(int maxWidth, int maxHeight, int minWidth, int minHeight)
    {
        var mainChatWindow = UIChatWindows.Instance.mainWindow;
        var resizeHandle = mainChatWindow.GetComponentInChildren<UIResizeHandle>();
        
        var resizeCopy = Object.Instantiate(resizeHandle, resizeHandle.transform.position, resizeHandle.transform.rotation, _rectTransform);
        var copyHandle = resizeCopy.GetComponent<UIResizeHandle>();
        copyHandle.ContainerRect = _rectTransform;
        copyHandle.MaxSize = new Vector2(maxWidth, maxHeight);
        copyHandle.MinSize = new Vector2(minWidth, minHeight);
        
        var copyRect = resizeCopy.GetComponent<RectTransform>();
        copyRect.pivot = new Vector2(1, 0);
        copyRect.sizeDelta = new Vector2(25, 25);
        copyRect.anchoredPosition = new Vector2(-5, 4);
        
        return this;
    }

    public void Enable(bool enabled)
    {
        _window.gameObject.SetActive(enabled);
    }

    public void Destroy()
    {
        Object.Destroy(_window.gameObject);
    }
}
