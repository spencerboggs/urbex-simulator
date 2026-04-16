using UnityEngine;
using UnityEngine.UIElements;

public class PlayerHUD : MonoBehaviour
{
    private VisualElement staminaFill;

    private void Start()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        staminaFill = root.Q<VisualElement>("staminaFill");
    }

    public void SetStamina(float percent)
    {
        percent = Mathf.Clamp01(percent);
        staminaFill.style.width = Length.Percent(percent * 100f);
        if (percent > 0.5f)
        {
            staminaFill.style.backgroundColor = Color.green;
        }
        else if (percent > 0.28f)
        {
            staminaFill.style.backgroundColor = Color.yellow;
        }
        else
        {
            staminaFill.style.backgroundColor = Color.red;
        }
    }
}
