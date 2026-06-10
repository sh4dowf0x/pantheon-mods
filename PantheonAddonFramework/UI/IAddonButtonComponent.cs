namespace PantheonAddonFramework.UI;

public interface IAddonButtonComponent
{
    void SetText(string text);
    void SetSize(float width, float height);
    void SetPosition(float x, float y);
    void SetFontSize(float fontSize);
    void Enable(bool enabled);
    void Destroy();
}
