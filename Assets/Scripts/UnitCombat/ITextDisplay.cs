using UnityEngine;
using UnityEngine.UI;

public class ITextDisplay : MonoBehaviour
{
    private Text legacyText;
    private Component tmpComponent;   // TextMeshProUGUI через reflection (нет TMP-зависимости)
    private System.Reflection.PropertyInfo tmpTextProperty;

    private void Awake()
    {
        legacyText = GetComponent<Text>();

        if (legacyText == null)
        {
            // Ищем TMP-компонент по имени типа — без compile-time ссылки на TMPro.
            foreach (var comp in GetComponents<Component>())
            {
                if (comp != null && comp.GetType().Name == "TextMeshProUGUI")
                {
                    tmpComponent = comp;
                    tmpTextProperty = comp.GetType().GetProperty("text");
                    break;
                }
            }
        }
    }

    /// <summary>Установить текст в активный компонент (legacy или TMP).</summary>
    public void SetText(string value)
    {
        if (legacyText != null)
        {
            legacyText.text = value;
        }
        else if (tmpComponent != null && tmpTextProperty != null)
        {
            tmpTextProperty.SetValue(tmpComponent, value);
        }
    }
}
